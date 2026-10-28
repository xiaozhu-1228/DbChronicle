using System.Collections.Generic;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Dialects
{
    public sealed class PostgreSqlDialect : SqlDialectBase
    {
        public override bool SupportsTransactionalDdl => true;

        public override bool RequiresFullColumnDefinition => false;

        public PostgreSqlDialect(string version, DialectContext context)
            : base("postgresql", version, TypeMapper.Load("postgresql"), context)
        {
        }

        public override string Quote(string identifier)
        {
            return "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";
        }

        /// <summary>PostgreSQL 的 boolean 字面量是 true/false，不能沿用基类的 1/0。</summary>
        protected override string FormatDefaultValue(string value)
        {
            if (value != null)
            {
                var text = value.Trim();
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return "true";
                }
                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return "false";
                }
            }

            return base.FormatDefaultValue(value);
        }

        public override string EscapeString(string value)
        {
            return (value ?? "").Replace("'", "''");
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
                string identifier = (string.IsNullOrEmpty(operation.PrimaryKeyName) ? (operation.Table + "_pkey") : operation.PrimaryKeyName);
                list.Add("  CONSTRAINT " + Quote(identifier) + " PRIMARY KEY (" + JoinQuoted(operation.PrimaryKeyColumns) + ")");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(operation.Table)).Append(" (\n");
            stringBuilder.Append(string.Join(",\n", list));
            stringBuilder.Append("\n);");
            if (!string.IsNullOrEmpty(operation.Comment))
            {
                stringBuilder.Append("\nCOMMENT ON TABLE ").Append(Quote(operation.Table)).Append(" IS '")
                    .Append(EscapeString(operation.Comment))
                    .Append("';");
            }
            return stringBuilder.ToString();
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
            return "COMMENT ON TABLE " + Quote(operation.Table) + " IS " + ((operation.Comment == null) ? "NULL" : ("'" + EscapeString(operation.Comment) + "'")) + ";";
        }

        public override string CommentIndex(IndexOperation operation)
        {
            return "COMMENT ON INDEX " + Quote(operation.Name) + " IS " + ((operation.Comment == null) ? "NULL" : ("'" + EscapeString(operation.Comment) + "'")) + ";";
        }

        public override string Sequence(SequenceOperation operation)
        {
            if (operation.Action == ObjectAction.Drop)
            {
                return "DROP SEQUENCE IF EXISTS " + Quote(operation.Name) + ";";
            }

            StringBuilder stringBuilder = new StringBuilder();
            if (operation.Action == ObjectAction.Replace || operation.Action == ObjectAction.Modify)
            {
                stringBuilder.Append("DROP SEQUENCE IF EXISTS ").Append(Quote(operation.Name)).AppendLine(";");
            }
            stringBuilder.Append("CREATE SEQUENCE ").Append(Quote(operation.Name));
            if (operation.StartWith.HasValue)
            {
                stringBuilder.Append(" START WITH ").Append(operation.StartWith.Value);
            }
            if (operation.IncrementBy.HasValue)
            {
                stringBuilder.Append(" INCREMENT BY ").Append(operation.IncrementBy.Value);
            }
            if (operation.MinValue.HasValue)
            {
                stringBuilder.Append(" MINVALUE ").Append(operation.MinValue.Value);
            }
            if (operation.MaxValue.HasValue)
            {
                stringBuilder.Append(" MAXVALUE ").Append(operation.MaxValue.Value);
            }
            if (operation.Cycle.HasValue)
            {
                stringBuilder.Append(operation.Cycle.Value ? " CYCLE" : " NO CYCLE");
            }
            if (operation.Cache.HasValue)
            {
                stringBuilder.Append(" CACHE ").Append(operation.Cache.Value);
            }
            stringBuilder.Append(';');
            return stringBuilder.ToString();
        }

        public override string TableOption(TableOptionOperation operation)
        {
            if (!string.IsNullOrEmpty(operation.Engine) || !string.IsNullOrEmpty(operation.Charset) || !string.IsNullOrEmpty(operation.Collate))
            {
                Error("E047", operation.Table, null,
                    "PostgreSQL 的存储引擎与字符集由数据库/表空间决定，无法按表调整；仅支持 TABLESPACE（请改用 <rawSql>）");
                return "";
            }
            if (string.IsNullOrEmpty(operation.Tablespace))
            {
                return "";
            }
            return "ALTER TABLE " + Quote(operation.Table) + " SET TABLESPACE " + Quote(operation.Tablespace) + ";";
        }

        public override string CommentConstraint(ConstraintCommentOperation operation)
        {
            return "COMMENT ON CONSTRAINT " + Quote(operation.Name) + " ON " + Quote(operation.Table)
                + " IS " + ((operation.Comment == null) ? "NULL" : ("'" + EscapeString(operation.Comment) + "'")) + ";";
        }

        public override string AddColumn(ColumnOperation operation)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD COLUMN ")
                .Append(BuildColumnDefinition(operation.Definition, operation.Table))
                .Append(';');
            if (operation.Definition.Comment != null)
            {
                stringBuilder.Append('\n').Append(BuildCommentColumn(operation.Table, operation.Name, operation.Definition.Comment));
            }
            return stringBuilder.ToString();
        }

        public override string ModifyColumn(ColumnOperation operation)
        {
            ColumnDefinition definition = operation.Definition;
            if (!definition.Type.HasValue)
            {
                Warn("W008", operation.Table, operation.Name, "缺少完整列定义，无法生成 ALTER COLUMN");
                return "";
            }

            if (definition.AutoIncrement.GetValueOrDefault()
                && (operation.PreviousDefinition == null || !operation.PreviousDefinition.AutoIncrement.GetValueOrDefault()))
            {
                Error("E052", operation.Table, operation.Name,
                    "PostgreSQL 不支持把已有列改为 identity；请改用 <rawSql> 重建列（ADD COLUMN ... GENERATED BY DEFAULT AS IDENTITY）");
                return "";
            }

            if (!string.IsNullOrEmpty(definition.Charset) || !string.IsNullOrEmpty(definition.Collate))
            {
                Warn("W015", operation.Table, operation.Name,
                    "PostgreSQL 不支持列级 charset / collate（由库级排序规则决定），MODIFY 时已忽略");
            }

            List<string> list = new List<string>();
            string table = Quote(operation.Table);
            string column = Quote(operation.Name);
            ColumnDefinition previous = operation.PreviousDefinition;

            if (previous == null || TypeChanged(definition, previous))
            {
                list.Add("ALTER TABLE " + table + " ALTER COLUMN " + column + " TYPE " + ResolveType(definition, operation.Table) + ";");
            }

            if (definition.Nullable.HasValue && (previous == null || definition.Nullable != previous.Nullable))
            {
                list.Add("ALTER TABLE " + table + " ALTER COLUMN " + column + (definition.Nullable.Value ? " DROP NOT NULL;" : " SET NOT NULL;"));
            }

            DefaultValueSpec defaultValueSpec = definition.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && (previous == null || !SameDefault(defaultValueSpec, previous.DefaultValue)))
            {
                list.Add(defaultValueSpec.IsNone
                    ? ("ALTER TABLE " + table + " ALTER COLUMN " + column + " DROP DEFAULT;")
                    : ("ALTER TABLE " + table + " ALTER COLUMN " + column + " SET DEFAULT " + (defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue)) + ";"));
            }

            if (definition.Comment != null && (previous == null || !string.Equals(definition.Comment, previous.Comment, StringComparison.Ordinal)))
            {
                list.Add(BuildCommentColumn(operation.Table, operation.Name, definition.Comment));
            }

            if (definition.Unique.HasValue)
            {
                var constraint = Quote("uk_" + operation.Table + "_" + operation.Name);
                var wasUnique = previous != null && previous.Unique.GetValueOrDefault();
                if (definition.Unique.Value && !wasUnique)
                {
                    list.Add("ALTER TABLE " + table + " ADD CONSTRAINT " + constraint + " UNIQUE (" + column + ");");
                }
                else if (!definition.Unique.Value && (previous == null || wasUnique))
                {
                    list.Add("ALTER TABLE " + table + " DROP CONSTRAINT IF EXISTS " + constraint + ";");
                }
            }

            return string.Join("\n", list);
        }

        private static bool TypeChanged(ColumnDefinition current, ColumnDefinition previous)
        {
            return current.Type != previous.Type
                || current.Length != previous.Length
                || current.Precision != previous.Precision
                || current.Scale != previous.Scale
                || current.Unsigned.GetValueOrDefault() != previous.Unsigned.GetValueOrDefault();
        }

        private static bool SameDefault(DefaultValueSpec current, DefaultValueSpec previous)
        {
            if (previous == null)
            {
                return false;
            }
            if (current.IsNone != previous.IsNone || current.IsNullLiteral != previous.IsNullLiteral)
            {
                return false;
            }
            return string.Equals(current.RawValue ?? "", previous.RawValue ?? "", StringComparison.Ordinal);
        }

        public override string DropColumn(ColumnOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP COLUMN IF EXISTS " + Quote(operation.Name) + ";";
        }

        public override string RenameColumn(ColumnOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " RENAME COLUMN " + Quote(operation.RenamedFrom) + " TO " + Quote(operation.Name) + ";";
        }

        public override string CommentColumn(ColumnOperation operation)
        {
            return BuildCommentColumn(operation.Table, operation.Name, operation.Comment ?? operation.Definition.Comment);
        }

        public override string ReorderColumn(ColumnOperation operation)
        {
            Error("E044", operation.Table, operation.Name,
                "PostgreSQL 不支持调整列顺序。请删除该 REORDER 操作，或改用 <rawSql> 按方言重建表");
            return "";
        }

        public override string AddPrimaryKey(PrimaryKeyOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? (operation.Table + "_pkey") : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(identifier) + " PRIMARY KEY (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropPrimaryKey(PrimaryKeyOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? (operation.Table + "_pkey") : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(identifier) + ";";
        }

        public override string CreateIndex(IndexOperation operation)
        {
            WarnIndexTypeUnsupported(operation);
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE ").Append(operation.Unique ? "UNIQUE " : "").Append("INDEX ")
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
            string identifier = (string.IsNullOrEmpty(operation.NewName) ? operation.Name : operation.NewName);
            return "ALTER INDEX " + Quote(operation.Name) + " RENAME TO " + Quote(identifier) + ";";
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
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(operation.Name) + ";";
        }

        public override string AddUniqueConstraint(UniqueConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(operation.Name) + " UNIQUE (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropUniqueConstraint(UniqueConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(operation.Name) + ";";
        }

        public override string AddCheckConstraint(CheckConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(operation.Name) + " CHECK (" + operation.Expression + ");";
        }

        public override string DropCheckConstraint(CheckConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(operation.Name) + ";";
        }

        public override string AddDefaultConstraint(DefaultConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ALTER COLUMN " + Quote(operation.Column) + " SET DEFAULT " + FormatDefaultValue(operation.Value) + ";";
        }

        public override string DropDefaultConstraint(DefaultConstraintOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " ALTER COLUMN " + Quote(operation.Column) + " DROP DEFAULT;";
        }

        public override string RawSql(RawSqlOperation operation)
        {
            return ResolveDialectSql(operation.SqlByDialect, operation.Table, "W032");
        }

        /// <summary>
        /// PostgreSQL：IGNORE → ON CONFLICT DO NOTHING；UPDATE → ON CONFLICT (主键) DO UPDATE。
        /// 冲突目标取累积模型的主键列；拿不到主键时降级为普通 INSERT 并警告。
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
                return text + "\nON CONFLICT DO NOTHING;";
            }
            if (string.Equals(operation.Conflict, "UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                if (primaryKeyColumns == null || primaryKeyColumns.Count == 0)
                {
                    Warn("W029", operation.Table, null,
                        "postgresql 的 SEED conflict=UPDATE 需要主键作为冲突目标，但累积模型中没有该表主键，已按普通 INSERT 生成");
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

        private string BuildColumnDefinition(ColumnDefinition column, string table)
        {
            if (!column.Type.HasValue)
            {
                throw new InvalidOperationException("列 " + table + "." + column.Name + " 缺少类型定义");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(Quote(column.Name)).Append(' ').Append(ResolveType(column, table));
            if (column.AutoIncrement.GetValueOrDefault())
            {
                stringBuilder.Append(" GENERATED BY DEFAULT AS IDENTITY");
            }
            if (column.Nullable.HasValue)
            {
                stringBuilder.Append(column.Nullable.Value ? " NULL" : " NOT NULL");
            }
            if (column.Unique.GetValueOrDefault())
            {
                stringBuilder.Append(" UNIQUE");
            }
            DefaultValueSpec defaultValueSpec = column.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                if (column.AutoIncrement.GetValueOrDefault())
                {
                    Warn("W008", table, column.Name, "自增列不能同时声明 DEFAULT，默认值被忽略");
                }
                else
                {
                    stringBuilder.Append(" DEFAULT ").Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue));
                }
            }
            if (!string.IsNullOrEmpty(column.Charset) || !string.IsNullOrEmpty(column.Collate))
            {
                Warn("W015", table, column.Name, "PostgreSQL 不支持列级 charset / collate（由库级排序规则决定），已忽略");
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

        private string ResolveType(ColumnDefinition column, string table)
        {
            LogicalType value = column.Type.Value;
            string result = base.TypeMapper.Resolve(value, column.Length, column.Precision, column.Scale, column.Unsigned.GetValueOrDefault());
            if (column.Unsigned.GetValueOrDefault())
            {
                Warn("W001", table, column.Name, "unsigned 被 PostgreSQL 忽略，需要非负约束请用 CHECK (" + column.Name + " >= 0)");
            }
            if (!string.IsNullOrEmpty(column.OnUpdate))
            {
                Warn("W002", table, column.Name, "onUpdate 被 PostgreSQL 忽略，需要触发器实现");
            }
            return result;
        }

        private string BuildCommentColumn(string table, string column, string comment)
        {
            return "COMMENT ON COLUMN " + Quote(table) + "." + Quote(column) + " IS " + ((comment == null) ? "NULL" : ("'" + EscapeString(comment) + "'")) + ";";
        }
    }

}