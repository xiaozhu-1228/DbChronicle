using System.Collections.Generic;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Dialects
{
    public class MySqlDialect : SqlDialectBase
    {
        public override bool SupportsTransactionalDdl => false;

        public override bool RequiresFullColumnDefinition => true;

        public MySqlDialect(string version, DialectContext context)
            : base("mysql", version, TypeMapper.Load("mysql"), context)
        {
        }

        protected MySqlDialect(string name, string typeDialect, string version, DialectContext context)
            : base(name, version, TypeMapper.Load(typeDialect), context)
        {
        }

        public override string Quote(string identifier)
        {
            return "`" + (identifier ?? "").Replace("`", "``") + "`";
        }

        public override string EscapeString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\0", "\\0").Replace("'", "''");
        }

        public override string CreateTable(CreateTableOperation operation)
        {
            List<string> list = new List<string>();
            foreach (ColumnDefinition column in operation.Columns)
            {
                list.Add("  " + BuildColumnDefinition(column, operation.Table));
            }
            if (operation.PrimaryKeyColumns.Count > 0)
            {
                list.Add("  PRIMARY KEY (" + JoinQuoted(operation.PrimaryKeyColumns) + ")");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(operation.Table)).Append(" (\n");
            stringBuilder.Append(string.Join(",\n", list));
            stringBuilder.Append("\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            if (!string.IsNullOrEmpty(operation.Comment))
            {
                stringBuilder.Append(" COMMENT='").Append(EscapeString(operation.Comment)).Append("'");
            }
            stringBuilder.Append(';');
            return stringBuilder.ToString();
        }

        public override string DropTable(DropTableOperation operation)
        {
            return "DROP TABLE IF EXISTS " + Quote(operation.Table) + ";";
        }

        public override string RenameTable(RenameTableOperation operation)
        {
            return "RENAME TABLE " + Quote(operation.Table) + " TO " + Quote(operation.NewName) + ";";
        }

        public override string CommentTable(CommentTableOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " COMMENT='" + EscapeString(operation.Comment) + "';";
        }

        public override string TableOption(TableOptionOperation operation)
        {
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(operation.Engine))
            {
                list.Add("ENGINE=" + operation.Engine);
            }
            if (!string.IsNullOrEmpty(operation.Charset))
            {
                list.Add("DEFAULT CHARSET=" + operation.Charset);
            }
            if (!string.IsNullOrEmpty(operation.Collate))
            {
                list.Add("COLLATE=" + operation.Collate);
            }
            if (!string.IsNullOrEmpty(operation.Tablespace))
            {
                list.Add("TABLESPACE " + Quote(operation.Tablespace));
            }
            if (list.Count == 0)
            {
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " " + string.Join(" ", list) + ";";
        }

        public override string AddColumn(ColumnOperation operation)
        {
            if (!string.IsNullOrEmpty(operation.Definition.ForeignKey))
            {
                Warn("W016", operation.Table, operation.Name, "MySQL 在 ALTER TABLE ADD COLUMN 中会忽略列级外键引用，请改用 <foreignKey action=\"ADD\">");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD COLUMN ")
                .Append(BuildColumnDefinition(operation.Definition, operation.Table, allowInlineForeignKey: false));
            if (!string.IsNullOrEmpty(operation.After))
            {
                stringBuilder.Append(string.Equals(operation.After, "FIRST", StringComparison.OrdinalIgnoreCase) ? " FIRST" : (" AFTER " + Quote(operation.After)));
            }
            stringBuilder.Append(';');
            return stringBuilder.ToString();
        }

        public override string ModifyColumn(ColumnOperation operation)
        {
            if (!operation.Definition.Type.HasValue)
            {
                Warn("W008", operation.Table, operation.Name, "缺少完整列定义，无法生成 MODIFY COLUMN");
                return "";
            }
            if (!string.IsNullOrEmpty(operation.Definition.ForeignKey))
            {
                Warn("W016", operation.Table, operation.Name, "MySQL 在 ALTER TABLE MODIFY COLUMN 中会忽略列级外键引用，请改用 <foreignKey>");
            }
            return "ALTER TABLE " + Quote(operation.Table) + " MODIFY COLUMN " + BuildColumnDefinition(operation.Definition, operation.Table, allowInlineForeignKey: false) + ";";
        }

        public override string DropColumn(ColumnOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP COLUMN " + Quote(operation.Name) + ";";
        }

        public override string RenameColumn(ColumnOperation operation)
        {
            if ((base.Name == "mariadb") ? VersionAtLeast(10, 5) : VersionAtLeast(8, 0))
            {
                return "ALTER TABLE " + Quote(operation.Table) + " RENAME COLUMN " + Quote(operation.RenamedFrom) + " TO " + Quote(operation.Name) + ";";
            }
            if (!operation.Definition.Type.HasValue)
            {
                Warn("W008", operation.Table, operation.Name, "MySQL 5.7 的 CHANGE 需要完整列定义");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " CHANGE " + Quote(operation.RenamedFrom) + " " + BuildColumnDefinition(operation.Definition, operation.Table) + ";";
        }

        public override string CommentColumn(ColumnOperation operation)
        {
            if (!operation.Definition.Type.HasValue)
            {
                Warn("W007", operation.Table, operation.Name, "MySQL 的列注释只能随 MODIFY 一起写入，缺少完整列定义");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " MODIFY COLUMN " + BuildColumnDefinition(operation.Definition, operation.Table, allowInlineForeignKey: false) + ";";
        }

        public override string ReorderColumn(ColumnOperation operation)
        {
            if (!operation.Definition.Type.HasValue || string.IsNullOrEmpty(operation.After))
            {
                Warn("W010", operation.Table, operation.Name, "REORDER 需要完整列定义与 after 属性");
                return "";
            }
            string text = (string.Equals(operation.After, "FIRST", StringComparison.OrdinalIgnoreCase) ? " FIRST" : (" AFTER " + Quote(operation.After)));
            return "ALTER TABLE " + Quote(operation.Table) + " MODIFY COLUMN " + BuildColumnDefinition(operation.Definition, operation.Table, allowInlineForeignKey: false) + text + ";";
        }

        public override string AddPrimaryKey(PrimaryKeyOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ADD PRIMARY KEY (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropPrimaryKey(PrimaryKeyOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP PRIMARY KEY;";
        }

        public override string CreateIndex(IndexOperation operation)
        {
            if (!string.IsNullOrEmpty(operation.Where))
            {
                Warn("W027", operation.Table, null, "MySQL 不支持部分索引，where 被忽略");
            }
            string value = (operation.Unique ? "UNIQUE " : "");
            if (!string.IsNullOrEmpty(operation.Type))
            {
                if (string.Equals(operation.Type, "FULLTEXT", StringComparison.OrdinalIgnoreCase))
                {
                    value = "FULLTEXT ";
                }
                else if (!string.Equals(operation.Type, "BTREE", StringComparison.OrdinalIgnoreCase) && !string.Equals(operation.Type, "HASH", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("W017", operation.Table, null, "MySQL 不支持的索引类型 " + operation.Type + "，已按普通索引生成");
                }
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE ").Append(value).Append("INDEX ")
                .Append(Quote(operation.Name))
                .Append(" ON ")
                .Append(Quote(operation.Table))
                .Append(" (");
            if (!string.IsNullOrEmpty(operation.Expression))
            {
                stringBuilder.Append('(').Append(operation.Expression).Append(')');
            }
            else
            {
                stringBuilder.Append(JoinQuoted(operation.Columns));
            }
            stringBuilder.Append(");");
            return stringBuilder.ToString();
        }

        public override string DropIndex(IndexOperation operation)
        {
            return "DROP INDEX " + Quote(operation.Name) + " ON " + Quote(operation.Table) + ";";
        }

        public override string RenameIndex(IndexOperation operation)
        {
            if (!VersionAtLeast(5, 7))
            {
                Warn("W028", operation.Table, null, "MySQL 5.7 以下不支持 RENAME INDEX，请改用 DROP + CREATE");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " RENAME INDEX " + Quote(operation.Name) + " TO " + Quote(operation.NewName) + ";";
        }

        public override string AddForeignKey(ForeignKeyOperation operation)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD CONSTRAINT ")
                .Append(Quote(operation.Name))
                .Append(" FOREIGN KEY (")
                .Append(JoinQuoted(operation.Columns))
                .Append(")")
                .Append(" REFERENCES ")
                .Append(Quote(operation.RefTable))
                .Append(" (")
                .Append(JoinQuoted(operation.RefColumns))
                .Append(")");
            if (!string.IsNullOrEmpty(operation.OnDelete))
            {
                stringBuilder.Append(" ON DELETE ").Append(operation.OnDelete);
            }
            if (!string.IsNullOrEmpty(operation.OnUpdate))
            {
                stringBuilder.Append(" ON UPDATE ").Append(operation.OnUpdate);
            }
            stringBuilder.Append(';');
            return stringBuilder.ToString();
        }

        public override string DropForeignKey(ForeignKeyOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP FOREIGN KEY " + Quote(operation.Name) + ";";
        }

        public override string AddUniqueConstraint(UniqueConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(operation.Name) + " UNIQUE (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropUniqueConstraint(UniqueConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP INDEX " + Quote(operation.Name) + ";";
        }

        public override string AddCheckConstraint(CheckConstraintOperation operation)
        {
            if (!VersionAtLeast(8, 0) && base.Name == "mysql")
            {
                Warn("W023", operation.Table, null, "MySQL 5.7 接受 CHECK 语法但不生效");
            }
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(operation.Name) + " CHECK (" + operation.Expression + ");";
        }

        public override string DropCheckConstraint(CheckConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CHECK " + Quote(operation.Name) + ";";
        }

        public override string AddDefaultConstraint(DefaultConstraintOperation operation)
        {
            if (!VersionAtLeast(8, 0) && base.Name == "mysql")
            {
                Warn("W024", operation.Table, operation.Column, "MySQL 5.7 不支持 ALTER COLUMN SET DEFAULT");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " ALTER COLUMN " + Quote(operation.Column) + " SET DEFAULT " + FormatDefaultValue(operation.Value) + ";";
        }

        public override string DropDefaultConstraint(DefaultConstraintOperation operation)
        {
            if (!VersionAtLeast(8, 0) && base.Name == "mysql")
            {
                Warn("W024", operation.Table, operation.Column, "MySQL 5.7 不支持 ALTER COLUMN DROP DEFAULT");
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " ALTER COLUMN " + Quote(operation.Column) + " DROP DEFAULT;";
        }

        public override string RawSql(RawSqlOperation operation)
        {
            return ResolveDialectSql(operation.SqlByDialect, operation.Table, "W032");
        }

        /// <summary>
        /// MySQL / MariaDB：IGNORE → INSERT IGNORE；UPDATE → ON DUPLICATE KEY UPDATE（无需冲突目标）。
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
                return BuildSeedInsert(operation, "INSERT IGNORE INTO") + ";";
            }
            if (string.Equals(operation.Conflict, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                List<string> list = new List<string>();
                foreach (string column in operation.Columns)
                {
                    list.Add(Quote(column) + " = VALUES(" + Quote(column) + ")");
                }
                return text + "\nON DUPLICATE KEY UPDATE " + string.Join(", ", list) + ";";
            }
            return text + ";";
        }

        protected string BuildColumnDefinition(ColumnDefinition column, string table, bool allowInlineForeignKey = true)
        {
            if (!column.Type.HasValue)
            {
                throw new InvalidOperationException("列 " + table + "." + column.Name + " 缺少类型定义");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(Quote(column.Name)).Append(' ').Append(ResolveType(column, table));
            if (!string.IsNullOrEmpty(column.Charset))
            {
                stringBuilder.Append(" CHARACTER SET ").Append(column.Charset);
            }
            if (!string.IsNullOrEmpty(column.Collate))
            {
                stringBuilder.Append(" COLLATE ").Append(column.Collate);
            }
            if (column.Nullable.HasValue)
            {
                stringBuilder.Append(column.Nullable.Value ? " NULL" : " NOT NULL");
            }
            DefaultValueSpec defaultValueSpec = column.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                stringBuilder.Append(" DEFAULT ").Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue));
            }
            if (column.AutoIncrement.GetValueOrDefault())
            {
                stringBuilder.Append(" AUTO_INCREMENT");
            }
            if (!string.IsNullOrEmpty(column.OnUpdate))
            {
                stringBuilder.Append(" ON UPDATE ").Append(column.OnUpdate);
            }
            if (!string.IsNullOrEmpty(column.Comment))
            {
                stringBuilder.Append(" COMMENT '").Append(EscapeString(column.Comment)).Append("'");
            }
            if (allowInlineForeignKey && !string.IsNullOrEmpty(column.ForeignKey))
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

        protected string ResolveType(ColumnDefinition column, string table)
        {
            LogicalType value = column.Type.Value;
            string result = base.TypeMapper.Resolve(value, column.Length, column.Precision, column.Scale, column.Unsigned.GetValueOrDefault());
            if (value == LogicalType.DateTimeOffset)
            {
                Warn("W003", table, column.Name, "DateTimeOffset 映射为 TIMESTAMP：存 UTC、按会话时区转换，不保留原始偏移，范围 1970-2038");
            }
            if (value == LogicalType.Xml)
            {
                Warn("W004", table, column.Name, "Xml 降级为 LONGTEXT：无原生 XML 类型，XPath 查询不可用");
            }
            return result;
        }
    }
}
