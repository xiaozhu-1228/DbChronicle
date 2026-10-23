using System;
using System.Collections.Generic;
using System.Text;
using DbChronicle.Domain.Operations;
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
            return "\"" + identifier + "\"";
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
            List<string> list = new List<string>();
            string text = Quote(operation.Table);
            string text2 = Quote(operation.Name);
            list.Add("ALTER TABLE " + text + " ALTER COLUMN " + text2 + " TYPE " + ResolveType(definition, operation.Table) + ";");
            if (definition.Nullable.HasValue)
            {
                list.Add("ALTER TABLE " + text + " ALTER COLUMN " + text2 + (definition.Nullable.Value ? " DROP NOT NULL;" : " SET NOT NULL;"));
            }
            DefaultValueSpec defaultValueSpec = definition.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared)
            {
                list.Add(defaultValueSpec.IsNone ? ("ALTER TABLE " + text + " ALTER COLUMN " + text2 + " DROP DEFAULT;") : ("ALTER TABLE " + text + " ALTER COLUMN " + text2 + " SET DEFAULT " + (defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue)) + ";"));
            }
            if (definition.Comment != null)
            {
                list.Add(BuildCommentColumn(operation.Table, operation.Name, definition.Comment));
            }
            return string.Join("\n", list);
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
            Warn("W010", operation.Table, operation.Name, "PostgreSQL 不支持列顺序调整，已忽略");
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
            if (operation.SqlByDialect.TryGetValue("postgresql", out var value) || operation.SqlByDialect.TryGetValue("any", out value))
            {
                return value;
            }
            return "";
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

