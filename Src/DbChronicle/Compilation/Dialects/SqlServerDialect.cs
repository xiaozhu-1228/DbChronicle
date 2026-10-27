using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

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
            return "[" + (identifier ?? "").Replace("]", "]]") + "]";
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
                stringBuilder.Append('\n').Append(BuildCommentScript(operation.Table, null, null, operation.Comment));
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
            return BuildCommentScript(operation.Table, null, null, operation.Comment);
        }

        public override string CommentIndex(IndexOperation operation)
        {
            return BuildCommentScript(operation.Table, "INDEX", operation.Name, operation.Comment);
        }

        public override string View(ViewOperation operation)
        {
            if (operation.Action == ObjectAction.Drop)
            {
                return "DROP VIEW IF EXISTS " + Quote(operation.Name) + ";";
            }
            string columns = string.IsNullOrEmpty(operation.Columns) ? "" : " (" + operation.Columns + ")";
            return "CREATE OR ALTER VIEW " + Quote(operation.Name) + columns + " AS\n" + operation.Select + ";";
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

        public override string CommentConstraint(ConstraintCommentOperation operation)
        {
            return BuildCommentScript(operation.Table, "CONSTRAINT", operation.Name, operation.Comment);
        }

        /// <summary>
        /// SQL Server 的注释通过扩展属性实现：表级为 level2=null，列/索引/约束分别带 level2type。
        /// 已存在则走 sp_updateextendedproperty，避免重复执行报错。
        /// </summary>
        private string BuildCommentScript(string table, string level2Type, string level2Name, string comment)
        {
            string objectId = "OBJECT_ID(N'" + EscapeString(Quote(table)) + "')";
            string minorId = "0";
            string levelArgs = "";
            if (!string.IsNullOrEmpty(level2Type))
            {
                levelArgs = ", N'" + level2Type + "', N'" + EscapeString(level2Name) + "'";
                if (level2Type == "COLUMN")
                {
                    minorId = "COLUMNPROPERTY(" + objectId + ", N'" + EscapeString(level2Name) + "', 'ColumnId')";
                }
                else if (level2Type == "INDEX")
                {
                    minorId = "(SELECT index_id FROM sys.indexes WHERE object_id = " + objectId + " AND name = N'" + EscapeString(level2Name) + "')";
                }
                else
                {
                    minorId = "(SELECT object_id FROM sys.objects WHERE parent_object_id = " + objectId + " AND name = N'" + EscapeString(level2Name) + "')";
                }
            }

            string value = (comment == null) ? "NULL" : ("N'" + EscapeString(comment) + "'");
            string args = "N'MS_Description', " + value + ", N'SCHEMA', N'dbo', N'TABLE', N'" + EscapeString(table) + "'" + levelArgs;
            return "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND name = N'MS_Description' AND major_id = "
                + objectId + " AND minor_id = " + minorId + ")\n"
                + "    EXEC sp_updateextendedproperty " + args + ";\n"
                + "ELSE\n"
                + "    EXEC sp_addextendedproperty " + args + ";";
        }

        public override string AddColumn(ColumnOperation operation)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD ")
                .Append(BuildColumnDefinition(operation.Definition, operation.Table, inlineDefault: true))
                .Append(';');
            if (!string.IsNullOrEmpty(operation.Definition.Comment))
            {
                stringBuilder.Append('\n').Append(BuildCommentScript(operation.Table, "COLUMN", operation.Name, operation.Definition.Comment));
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

            if (!definition.Nullable.HasValue)
            {
                Warn("W008", operation.Table, operation.Name, "MODIFY 未声明 nullable，按 NULL 生成可空列");
            }

            List<string> list = new List<string>();
            string identifier = BuildDefaultConstraintName(operation.Table, operation.Name);
            string text = Quote(operation.Table);
            list.Add("IF OBJECT_ID(N'" + EscapeString(identifier) + "', N'D') IS NOT NULL ALTER TABLE " + text + " DROP CONSTRAINT " + Quote(identifier) + ";");
            list.Add("ALTER TABLE " + text + " ALTER COLUMN " + Quote(operation.Name) + " " + ResolveType(definition, operation.Table) + ((definition.Nullable == false) ? " NOT NULL" : " NULL") + ";");
            DefaultValueSpec defaultValueSpec = definition.DefaultValue ?? DefaultValueSpec.Undeclared;
            if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
            {
                list.Add("ALTER TABLE " + text + " ADD CONSTRAINT " + Quote(identifier) + " DEFAULT " + (defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue)) + " FOR " + Quote(operation.Name) + ";");
            }
            if (!string.IsNullOrEmpty(definition.Comment))
            {
                list.Add(BuildCommentScript(operation.Table, "COLUMN", operation.Name, definition.Comment));
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
            return BuildCommentScript(operation.Table, "COLUMN", operation.Name, operation.Comment ?? operation.Definition.Comment);
        }

        public override string ReorderColumn(ColumnOperation operation)
        {
            Error("E044", operation.Table, operation.Name,
                "SQL Server 不支持调整列顺序。请删除该 REORDER 操作，或改用 <rawSql> 按方言重建表");
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
                Warn("W027", operation.Table, null, "SQL Server 不支持该索引写法，expression / where 被忽略");
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
            return ResolveDialectSql(operation.SqlByDialect, operation.Table, "W032");
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
