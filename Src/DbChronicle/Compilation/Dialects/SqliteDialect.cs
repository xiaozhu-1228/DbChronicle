using System.Collections.Generic;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Dialects
{
    public sealed class SqliteDialect : SqlDialectBase
    {
        public override bool SupportsTransactionalDdl => true;

        public override bool RequiresFullColumnDefinition => true;

        public SqliteDialect(string version, DialectContext context)
            : base("sqlite", version, TypeMapper.Load("sqlite"), context)
        {
        }

        public override string Quote(string identifier)
        {
            return "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";
        }

        public override string EscapeString(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        public override string CreateTable(CreateTableOperation operation)
        {
            List<string> list = new List<string>();
            string text = FindAutoIncrementColumn(operation.Columns, operation.PrimaryKeyColumns, operation.Table);
            foreach (ColumnDefinition column in operation.Columns)
            {
                list.Add("  " + BuildColumnDefinition(column, operation.Table, text));
            }
            List<string> list2 = new List<string>();
            foreach (string primaryKeyColumn in operation.PrimaryKeyColumns)
            {
                if (!string.Equals(primaryKeyColumn, text, StringComparison.OrdinalIgnoreCase))
                {
                    list2.Add(primaryKeyColumn);
                }
            }
            if (list2.Count > 0)
            {
                list.Add("  PRIMARY KEY (" + JoinQuoted(list2) + ")");
            }
            foreach (ForeignKeyOperation foreignKey in operation.ForeignKeys)
            {
                list.Add("  " + BuildTableForeignKey(foreignKey));
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(operation.Table)).Append(" (\n");
            stringBuilder.Append(string.Join(",\n", list));
            stringBuilder.Append("\n);");
            if (!string.IsNullOrEmpty(operation.Comment))
            {
                Warn("W006", operation.Table, null, "SQLite 不支持表注释，已跳过");
            }
            return BuildTimeFormatHints(operation.Columns) + stringBuilder;
        }

        public override string DropTable(DropTableOperation operation)
        {
            return "DROP TABLE IF EXISTS " + Quote(operation.Table) + ";";
        }

        public override string RenameTable(RenameTableOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " RENAME TO " + Quote(operation.NewName) + ";";
        }

        public override string CommentTable(CommentTableOperation operation)
        {
            Warn("W006", operation.Table, null, "SQLite 不支持表注释，已跳过");
            return "";
        }

        public override string View(ViewOperation operation)
        {
            if (operation.Action == ObjectAction.Drop)
            {
                return "DROP VIEW IF EXISTS " + Quote(operation.Name) + ";";
            }

            string columns = string.IsNullOrEmpty(operation.Columns) ? "" : " (" + operation.Columns + ")";
            string create = "CREATE VIEW " + Quote(operation.Name) + columns + " AS\n" + operation.Select + ";";
            if (operation.Action == ObjectAction.Replace || operation.Action == ObjectAction.Modify)
            {
                // SQLite 不支持 CREATE OR REPLACE VIEW，只能先删后建
                return "DROP VIEW IF EXISTS " + Quote(operation.Name) + ";\n" + create;
            }
            return create;
        }

        public override string AddColumn(ColumnOperation operation)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD COLUMN ")
                .Append(BuildColumnDefinition(operation.Definition, operation.Table, null))
                .Append(';');
            if (operation.Definition.Comment != null)
            {
                Warn("W007", operation.Table, operation.Name, "SQLite 不支持列注释，已跳过");
            }
            return BuildTimeFormatHints(new ColumnDefinition[1] { operation.Definition }) + stringBuilder;
        }

        public override string ModifyColumn(ColumnOperation operation)
        {
            Warn("W026", operation.Table, operation.Name, "SQLite 不支持 ALTER COLUMN，应走 rebuild");
            return "";
        }

        public override string DropColumn(ColumnOperation operation)
        {
            if (!VersionAtLeast(3, 35))
            {
                Warn("W026", operation.Table, operation.Name, "SQLite 3.35 以下不支持 DROP COLUMN，应走 rebuild");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " DROP COLUMN " + Quote(operation.Name) + ";";
        }

        public override string RenameColumn(ColumnOperation operation)
        {
            if (!VersionAtLeast(3, 25))
            {
                Warn("W026", operation.Table, operation.Name, "SQLite 3.25 以下不支持 RENAME COLUMN，应走 rebuild");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " RENAME COLUMN " + Quote(operation.RenamedFrom) + " TO " + Quote(operation.Name) + ";";
        }

        public override string CommentColumn(ColumnOperation operation)
        {
            Warn("W007", operation.Table, operation.Name, "SQLite 不支持列注释，已跳过");
            return "";
        }

        public override string ReorderColumn(ColumnOperation operation)
        {
            Error("E044", operation.Table, operation.Name,
                "SQLite 不支持调整列顺序。请删除该 REORDER 操作，或改用 <rawSql> 重建表");
            return "";
        }

        public override string AddPrimaryKey(PrimaryKeyOperation operation)
        {
            Warn("W026", operation.Table, null, "SQLite 不支持新增主键，应走 rebuild");
            return "";
        }

        public override string DropPrimaryKey(PrimaryKeyOperation operation)
        {
            Warn("W026", operation.Table, null, "SQLite 不支持删除主键，应走 rebuild");
            return "";
        }

        public override string CreateIndex(IndexOperation operation)
        {
            WarnIndexTypeUnsupported(operation);
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE ").Append(operation.Unique ? "UNIQUE " : "").Append("INDEX IF NOT EXISTS ")
                .Append(Quote(operation.Name))
                .Append(" ON ")
                .Append(Quote(operation.Table))
                .Append(" (");
            if (!string.IsNullOrEmpty(operation.Expression))
            {
                stringBuilder.Append(operation.Expression);
            }
            else
            {
                stringBuilder.Append(JoinQuoted(operation.Columns));
            }
            stringBuilder.Append(')');
            if (!string.IsNullOrEmpty(operation.Where))
            {
                stringBuilder.Append(" WHERE ").Append(operation.Where);
            }
            stringBuilder.Append(';');
            return stringBuilder.ToString();
        }

        public override string DropIndex(IndexOperation operation)
        {
            return "DROP INDEX IF EXISTS " + Quote(operation.Name) + ";";
        }

        public override string RenameIndex(IndexOperation operation)
        {
            Warn("W028", operation.Table, null, "SQLite 不支持重命名索引，请改用 DROP + CREATE");
            return "";
        }

        public override string AddForeignKey(ForeignKeyOperation operation)
        {
            if (operation.PartOfCreateTable)
            {
                // 已随 CREATE TABLE 内联，无需 rebuild
                return "";
            }
            Warn("W026", operation.Table, null, "SQLite 的外键只能在建表时声明，应走 rebuild");
            return "";
        }

        public override string DropForeignKey(ForeignKeyOperation operation)
        {
            Warn("W026", operation.Table, null, "SQLite 不支持删除外键，应走 rebuild");
            return "";
        }

        public override string AddUniqueConstraint(UniqueConstraintOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? ("uk_" + operation.Table + "_" + string.Join("_", operation.Columns)) : operation.Name);
            return "CREATE UNIQUE INDEX IF NOT EXISTS " + Quote(identifier) + " ON " + Quote(operation.Table) + " (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropUniqueConstraint(UniqueConstraintOperation operation)
        {
            return "DROP INDEX IF EXISTS " + Quote(operation.Name) + ";";
        }

        public override string AddCheckConstraint(CheckConstraintOperation operation)
        {
            Warn("W025", operation.Table, null, "SQLite 不支持新增检查约束，已跳过");
            return "";
        }

        public override string DropCheckConstraint(CheckConstraintOperation operation)
        {
            Warn("W025", operation.Table, null, "SQLite 不支持删除检查约束，已跳过");
            return "";
        }

        public override string AddDefaultConstraint(DefaultConstraintOperation operation)
        {
            Warn("W025", operation.Table, operation.Column, "SQLite 不支持独立默认值约束，已跳过");
            return "";
        }

        public override string DropDefaultConstraint(DefaultConstraintOperation operation)
        {
            Warn("W025", operation.Table, operation.Column, "SQLite 不支持独立默认值约束，已跳过");
            return "";
        }

        public override string RawSql(RawSqlOperation operation)
        {
            return ResolveDialectSql(operation.SqlByDialect, operation.Table, "W032");
        }

        /// <summary>
        /// SQLite：IGNORE → INSERT OR IGNORE；UPDATE → ON CONFLICT (主键) DO UPDATE（3.24+）。
        /// 版本低于 3.24 或拿不到主键时降级为普通 INSERT 并警告。
        /// </summary>
        public override string BuildSeed(SeedOperation operation, IReadOnlyList<string> primaryKeyColumns)
        {
            string text = BuildSeedInsert(operation);
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            if (string.Equals(operation.Conflict, "IGNORE", StringComparison.OrdinalIgnoreCase))
            {
                return BuildSeedInsert(operation, "INSERT OR IGNORE INTO") + ";";
            }
            if (string.Equals(operation.Conflict, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                if (!VersionAtLeast(3, 24) || primaryKeyColumns == null || primaryKeyColumns.Count == 0)
                {
                    Warn("W029", operation.Table, null,
                        "sqlite 的 SEED conflict=UPDATE 需要 3.24+ 与主键冲突目标，当前条件不满足，已按普通 INSERT 生成");
                    return text + ";";
                }
                List<string> list = new List<string>();
                foreach (string column in operation.Columns)
                {
                    list.Add(Quote(column) + " = EXCLUDED." + Quote(column));
                }
                return text + "\nON CONFLICT (" + JoinQuoted(primaryKeyColumns) + ") DO UPDATE SET "
                    + string.Join(", ", list) + ";";
            }
            return text + ";";
        }

        public override bool RequiresRebuild(SchemaOperation operation)
        {
            if (operation is ColumnOperation columnOperation)
            {
                return columnOperation.Action switch
                {
                    ColumnAction.Modify => true,
                    ColumnAction.Add => columnOperation.Definition.PrimaryKey.GetValueOrDefault() || columnOperation.Definition.Unique.GetValueOrDefault(),
                    ColumnAction.Drop => !VersionAtLeast(3, 35),
                    ColumnAction.Rename => !VersionAtLeast(3, 25),
                    _ => false,
                };
            }
            if (operation is PrimaryKeyOperation)
            {
                return true;
            }
            if (operation is ForeignKeyOperation foreignKeyOperation)
            {
                // 建表单元内的外键已随 CREATE TABLE 内联，不需要 rebuild
                return !foreignKeyOperation.PartOfCreateTable;
            }
            if (operation is CheckConstraintOperation || operation is DefaultConstraintOperation)
            {
                // 检查约束与默认值约束在 SQLite 上只能通过重建表实现
                return true;
            }
            return false;
        }

        public override string RebuildTable(RebuildTableOperation operation)
        {
            string identifier = operation.Table + "__rebuild";
            List<string> list = new List<string>();
            List<string> list2 = new List<string>();
            string text = FindAutoIncrementColumn(operation.Columns, operation.PrimaryKeyColumns, operation.Table);
            foreach (ColumnDefinition column in operation.Columns)
            {
                list2.Add("  " + BuildColumnDefinition(column, operation.Table, text));
            }
            List<string> list3 = new List<string>();
            foreach (string primaryKeyColumn in operation.PrimaryKeyColumns)
            {
                if (!string.Equals(primaryKeyColumn, text, StringComparison.OrdinalIgnoreCase))
                {
                    list3.Add(primaryKeyColumn);
                }
            }
            if (list3.Count > 0)
            {
                list2.Add("  PRIMARY KEY (" + JoinQuoted(list3) + ")");
            }
            foreach (CheckConstraintOperation check in operation.CheckConstraints)
            {
                list2.Add("  CONSTRAINT " + Quote(check.Name) + " CHECK (" + check.Expression + ")");
            }
            foreach (ForeignKeyOperation foreignKey in operation.ForeignKeys)
            {
                list2.Add("  " + BuildTableForeignKey(foreignKey));
            }
            list.Add("DROP TABLE IF EXISTS " + Quote(identifier) + ";");
            list.Add("CREATE TABLE " + Quote(identifier) + " (\n" + string.Join(",\n", list2) + "\n);");
            if (operation.ColumnMappings.Count > 0)
            {
                List<string> list4 = new List<string>();
                List<string> list5 = new List<string>();
                foreach (KeyValuePair<string, string> columnMapping in operation.ColumnMappings)
                {
                    list4.Add(Quote(columnMapping.Value));
                    list5.Add(BuildSelectExpression(operation, columnMapping.Value, columnMapping.Key));
                }
                list.Add("INSERT INTO " + Quote(identifier) + " (" + string.Join(", ", list4) + ") SELECT " + string.Join(", ", list5) + " FROM " + Quote(operation.Table) + ";");
            }
            list.Add("DROP TABLE " + Quote(operation.Table) + ";");
            list.Add("ALTER TABLE " + Quote(identifier) + " RENAME TO " + Quote(operation.Table) + ";");
            return BuildTimeFormatHints(operation.Columns) + string.Join("\n", list);
        }

        private string BuildTableForeignKey(ForeignKeyOperation operation)
        {
            var stringBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(operation.Name))
            {
                stringBuilder.Append("CONSTRAINT ").Append(Quote(operation.Name)).Append(' ');
            }
            stringBuilder.Append("FOREIGN KEY (").Append(JoinQuoted(operation.Columns)).Append(") ")
                .Append("REFERENCES ").Append(Quote(operation.RefTable)).Append(" (")
                .Append(JoinQuoted(operation.RefColumns)).Append(')');
            if (!string.IsNullOrEmpty(operation.OnDelete))
            {
                stringBuilder.Append(" ON DELETE ").Append(operation.OnDelete);
            }
            if (!string.IsNullOrEmpty(operation.OnUpdate))
            {
                stringBuilder.Append(" ON UPDATE ").Append(operation.OnUpdate);
            }
            return stringBuilder.ToString();
        }

        private string BuildSelectExpression(RebuildTableOperation operation, string targetColumn, string sourceColumn)
        {
            string expression = Quote(sourceColumn);
            LogicalType sourceType;
            ColumnDefinition target;
            if (!operation.ColumnSourceTypes.TryGetValue(targetColumn, out sourceType)
                || !TryFindColumn(operation.Columns, targetColumn, out target)
                || !target.Type.HasValue)
            {
                return expression;
            }

            string sourceClass = StorageClassOf(sourceType);
            string targetClass = StorageClassOf(target.Type.Value);
            if (string.Equals(sourceClass, targetClass, StringComparison.OrdinalIgnoreCase))
            {
                return expression;
            }

            return "CAST(" + expression + " AS " + targetClass + ")";
        }

        private static string StorageClassOf(LogicalType type)
        {
            switch (type)
            {
                case LogicalType.Boolean:
                case LogicalType.Int8:
                case LogicalType.Int16:
                case LogicalType.Int32:
                case LogicalType.Int64:
                    return "INTEGER";
                case LogicalType.Float32:
                case LogicalType.Float64:
                    return "REAL";
                case LogicalType.Decimal:
                    return "NUMERIC";
                case LogicalType.Binary:
                case LogicalType.Blob:
                    return "BLOB";
                default:
                    return "TEXT";
            }
        }

        private static bool TryFindColumn(IEnumerable<ColumnDefinition> columns, string name, out ColumnDefinition column)
        {
            foreach (ColumnDefinition item in columns)
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    column = item;
                    return true;
                }
            }

            column = null;
            return false;
        }

        private string BuildColumnDefinition(ColumnDefinition column, string table, string autoIncrementColumn)
        {
            if (!column.Type.HasValue)
            {
                throw new InvalidOperationException("列 " + table + "." + column.Name + " 缺少类型定义");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(Quote(column.Name)).Append(' ').Append(ResolveType(column, table));
            if (string.Equals(column.Name, autoIncrementColumn, StringComparison.OrdinalIgnoreCase))
            {
                stringBuilder.Append(" PRIMARY KEY AUTOINCREMENT");
            }
            else if (column.Nullable == false)
            {
                stringBuilder.Append(" NOT NULL");
            }
            if (column.Unique.GetValueOrDefault())
            {
                stringBuilder.Append(" UNIQUE");
            }
            DefaultValueSpec defaultValueSpec = column.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                stringBuilder.Append(" DEFAULT ").Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue));
            }
            if (!string.IsNullOrEmpty(column.Charset) || !string.IsNullOrEmpty(column.Collate))
            {
                Warn("W015", table, column.Name, "SQLite 不支持列级 charset / collate，已忽略");
            }
            if (!string.IsNullOrEmpty(column.ForeignKey))
            {
                string[] array = column.ForeignKey.Split(new char[1] { '.' });
                if (array.Length == 2)
                {
                    stringBuilder.Append(" REFERENCES ").Append(Quote(array[0])).Append(" (")
                        .Append(Quote(array[1]))
                        .Append(")");
                }
                else
                {
                    Warn("W016", table, column.Name, "列级 foreignKey 必须为 表名.列名，已忽略：" + column.ForeignKey);
                }
            }
            return stringBuilder.ToString();
        }

        private static string BuildTimeFormatHints(IEnumerable<ColumnDefinition> columns)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (ColumnDefinition column in columns)
            {
                if (column.Type.GetValueOrDefault() == LogicalType.DateTime)
                {
                    stringBuilder.Append("-- ").Append(column.Name).AppendLine(" 列存储格式：ISO8601 无时区（如 2026-10-22T10:30:00），格式一致性由应用层保证");
                }
                else if (column.Type.GetValueOrDefault() == LogicalType.DateTimeOffset)
                {
                    stringBuilder.Append("-- ").Append(column.Name).AppendLine(" 列存储格式：ISO8601 带偏移（如 2026-10-22T10:30:00+08:00），格式一致性由应用层保证");
                }
            }
            return stringBuilder.ToString();
        }

        private string FindAutoIncrementColumn(IEnumerable<ColumnDefinition> columns, IEnumerable<string> primaryKeyColumns, string table)
        {
            foreach (ColumnDefinition column in columns)
            {
                if (!column.AutoIncrement.GetValueOrDefault())
                {
                    continue;
                }
                if (column.Type.GetValueOrDefault() != LogicalType.Int64 || !column.PrimaryKey.GetValueOrDefault())
                {
                    throw new InvalidOperationException("SQLite 自增列必须同时满足：类型为 Int64 且 primaryKey=true（表 " + table + "，列 " + column.Name + "）");
                }
                foreach (string primaryKeyColumn in primaryKeyColumns)
                {
                    if (!string.Equals(primaryKeyColumn, column.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("SQLite 的自增列必须单独作为主键，不能与其他列组成复合主键（表 "
                            + table + "，自增列 " + column.Name + "，另一主键列 " + primaryKeyColumn + "）");
                    }
                }
                return column.Name;
            }
            return null;
        }

        private string ResolveType(ColumnDefinition column, string table)
        {
            LogicalType value = column.Type.Value;
            string result = base.TypeMapper.Resolve(value, column.Length, column.Precision, column.Scale, unsigned: false);
            if (column.Unsigned.GetValueOrDefault())
            {
                Warn("W001", table, column.Name, "unsigned 被 SQLite 忽略");
            }
            if (!string.IsNullOrEmpty(column.OnUpdate))
            {
                Warn("W002", table, column.Name, "onUpdate 被 SQLite 忽略，需要触发器实现");
            }
            if (value == LogicalType.Json || value == LogicalType.Xml)
            {
                Warn("W004", table, column.Name, value.ToString() + " 降级为 TEXT，格式一致性由应用层保证");
            }
            return result;
        }
    }
}
