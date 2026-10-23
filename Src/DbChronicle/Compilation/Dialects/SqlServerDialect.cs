using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DbChronicle.Domain.Operations;
namespace DbChronicle.Compilation.Dialects 
{
    public sealed class SqlServerDialect : SqlDialectBase
    {
        public override bool SupportsTransactionalDdl => true;

        public override bool RequiresFullColumnDefinition => false;

        public SqlServerDialect(string version, DialectContext context)
            : base("sqlserver", version, TypeMapper.Load("sqlserver"), context)
        {
        }

        public override string Quote(string identifier)
        {
            return "[" + identifier + "]";
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
                list.Add("  " + BuildColumnDefinition(column, operation.Table, inlineDefault: true));
            }
            if (operation.PrimaryKeyColumns.Count > 0)
            {
                string identifier = (string.IsNullOrEmpty(operation.PrimaryKeyName) ? ("PK_" + operation.Table) : operation.PrimaryKeyName);
                list.Add("  CONSTRAINT " + Quote(identifier) + " PRIMARY KEY (" + JoinQuoted(operation.PrimaryKeyColumns) + ")");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("CREATE TABLE ").Append(Quote(operation.Table)).Append(" (\n");
            stringBuilder.Append(string.Join(",\n", list));
            stringBuilder.Append("\n);");
            if (!string.IsNullOrEmpty(operation.Comment))
            {
                Warn("W006", operation.Table, null, "SQL Server 的表注释需要扩展属性，一期跳过");
            }
            return stringBuilder.ToString();
        }

        public override string DropTable(DropTableOperation operation)
        {
            return "DROP TABLE IF EXISTS " + Quote(operation.Table) + ";";
        }

        public override string RenameTable(RenameTableOperation operation)
        {
            return "EXEC sp_rename N'" + EscapeString(operation.Table) + "', N'" + EscapeString(operation.NewName) + "';";
        }

        public override string CommentTable(CommentTableOperation operation)
        {
            Warn("W006", operation.Table, null, "SQL Server 的表注释需要 sp_addextendedproperty，一期跳过");
            return "";
        }

        public override string AddColumn(ColumnOperation operation)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD ")
                .Append(BuildColumnDefinition(operation.Definition, operation.Table, inlineDefault: false))
                .Append(';');
            DefaultValueSpec defaultValueSpec = operation.Definition.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                string identifier = BuildDefaultConstraintName(operation.Table, operation.Name);
                stringBuilder.Append("\nALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD CONSTRAINT ")
                    .Append(Quote(identifier))
                    .Append(" DEFAULT ")
                    .Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue))
                    .Append(" FOR ")
                    .Append(Quote(operation.Name))
                    .Append(';');
            }
            if (operation.Definition.Comment != null)
            {
                Warn("W007", operation.Table, operation.Name, "SQL Server 的列注释需要 sp_addextendedproperty，一期跳过");
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
            string identifier = BuildDefaultConstraintName(operation.Table, operation.Name);
            string text = Quote(operation.Table);
            list.Add("ALTER TABLE " + text + " DROP CONSTRAINT IF EXISTS " + Quote(identifier) + ";");
            list.Add("ALTER TABLE " + text + " ALTER COLUMN " + Quote(operation.Name) + " " + ResolveType(definition, operation.Table) + ((definition.Nullable == false) ? " NOT NULL" : " NULL") + ";");
            DefaultValueSpec defaultValueSpec = definition.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                list.Add("ALTER TABLE " + text + " ADD CONSTRAINT " + Quote(identifier) + " DEFAULT " + (defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue)) + " FOR " + Quote(operation.Name) + ";");
            }
            if (definition.Comment != null)
            {
                Warn("W007", operation.Table, operation.Name, "SQL Server 的列注释需要 sp_addextendedproperty，一期跳过");
            }
            return string.Join("\n", list);
        }

        public override string DropColumn(ColumnOperation operation)
        {
            return "ALTER TABLE " + Quote(operation.Table) + " DROP COLUMN " + Quote(operation.Name) + ";";
        }

        public override string RenameColumn(ColumnOperation operation)
        {
            return "EXEC sp_rename N'" + EscapeString(operation.Table + "." + operation.RenamedFrom) + "', N'" + EscapeString(operation.Name) + "', N'COLUMN';";
        }

        public override string CommentColumn(ColumnOperation operation)
        {
            Warn("W007", operation.Table, operation.Name, "SQL Server 的列注释需要 sp_addextendedproperty，一期跳过");
            return "";
        }

        public override string ReorderColumn(ColumnOperation operation)
        {
            Warn("W010", operation.Table, operation.Name, "SQL Server 不支持列顺序调整，已忽略");
            return "";
        }

        public override string AddPrimaryKey(PrimaryKeyOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? ("PK_" + operation.Table) : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(identifier) + " PRIMARY KEY (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropPrimaryKey(PrimaryKeyOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? ("PK_" + operation.Table) : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(identifier) + ";";
        }

        public override string CreateIndex(IndexOperation operation)
        {
            WarnIndexTypeUnsupported(operation);
            if (!string.IsNullOrEmpty(operation.Expression) || !string.IsNullOrEmpty(operation.Where))
            {
                Warn("W021", operation.Table, null, "SQL Server 不支持该索引写法，expression / where 被忽略");
            }
            return "CREATE " + (operation.Unique ? "UNIQUE " : "") + "INDEX " + Quote(operation.Name) + " ON " + Quote(operation.Table) + " (" + JoinQuoted(operation.Columns) + ");";
        }

        public override string DropIndex(IndexOperation operation)
        {
            return "DROP INDEX " + Quote(operation.Name) + " ON " + Quote(operation.Table) + ";";
        }

        public override string RenameIndex(IndexOperation operation)
        {
            string value = (string.IsNullOrEmpty(operation.NewName) ? operation.Name : operation.NewName);
            return "EXEC sp_rename N'" + EscapeString(operation.Table + "." + operation.Name) + "', N'" + EscapeString(value) + "', N'INDEX';";
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
            string identifier = (string.IsNullOrEmpty(operation.Name) ? BuildDefaultConstraintName(operation.Table, operation.Column) : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " ADD CONSTRAINT " + Quote(identifier) + " DEFAULT " + FormatDefaultValue(operation.Value) + " FOR " + Quote(operation.Column) + ";";
        }

        public override string DropDefaultConstraint(DefaultConstraintOperation operation)
        {
            string identifier = (string.IsNullOrEmpty(operation.Name) ? BuildDefaultConstraintName(operation.Table, operation.Column) : operation.Name);
            return "ALTER TABLE " + Quote(operation.Table) + " DROP CONSTRAINT " + Quote(identifier) + ";";
        }

        public override string RawSql(RawSqlOperation operation)
        {
            if (operation.SqlByDialect.TryGetValue("sqlserver", out var value) || operation.SqlByDialect.TryGetValue("any", out value))
            {
                return value;
            }
            return "";
        }

        private string BuildColumnDefinition(ColumnDefinition column, string table, bool inlineDefault)
        {
            if (!column.Type.HasValue)
            {
                throw new InvalidOperationException("列 " + table + "." + column.Name + " 缺少类型定义");
            }
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(Quote(column.Name)).Append(' ').Append(ResolveType(column, table));
            if (column.AutoIncrement.GetValueOrDefault())
            {
                stringBuilder.Append(" IDENTITY(1,1)");
            }
            stringBuilder.Append(column.Nullable.GetValueOrDefault() ? " NULL" : " NOT NULL");
            DefaultValueSpec defaultValueSpec = column.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (inlineDefault && defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone && !column.AutoIncrement.GetValueOrDefault())
            {
                stringBuilder.Append(" CONSTRAINT ").Append(Quote(BuildDefaultConstraintName(table, column.Name))).Append(" DEFAULT ")
                    .Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue));
            }
            if (!string.IsNullOrEmpty(column.Charset) || !string.IsNullOrEmpty(column.Collate))
            {
                Warn("W015", table, column.Name, "SQL Server 不支持列级 charset / collate（由列的排序规则决定），已忽略");
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
                Warn("W001", table, column.Name, "unsigned 被 SQL Server 忽略");
            }
            if (!string.IsNullOrEmpty(column.OnUpdate))
            {
                Warn("W002", table, column.Name, "onUpdate 被 SQL Server 忽略，需要触发器实现");
            }
            return result;
        }

        private static string BuildDefaultConstraintName(string table, string column)
        {
            string text = "DF_" + table + "_" + column;
            if (text.Length <= 128)
            {
                return text;
            }
            string text2 = ((table.Length > 40) ? table.Substring(0, 40) : table);
            string text3 = ((column.Length > 40) ? column.Substring(0, 40) : column);
            return "DF_" + text2 + "_" + text3 + "_" + ShortHash(text);
        }

        private static string ShortHash(string value)
        {
            using SHA256 sHA = SHA256.Create();
            byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(value));
            StringBuilder stringBuilder = new StringBuilder(8);
            for (int i = 0; i < 4; i++)
            {
                stringBuilder.Append(array[i].ToString("x2"));
            }
            return stringBuilder.ToString();
        }
    }
}


