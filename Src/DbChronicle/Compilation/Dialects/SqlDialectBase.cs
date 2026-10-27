using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;

namespace DbChronicle.Compilation.Dialects
{
    public abstract class SqlDialectBase : ISqlDialect
    {
        /// <summary>函数调用形态的默认值（如 UTC_TIMESTAMP()、datetime('now')），原样透传不加引号。</summary>
        private static readonly Regex FunctionCallPattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_]*\s*\(.*\)$", RegexOptions.Singleline);

        public string Name { get; private set; }

        public string Version { get; private set; }

        public TypeMapper TypeMapper { get; private set; }

        public DialectContext Context { get; private set; }

        public abstract bool SupportsTransactionalDdl { get; }

        public abstract bool RequiresFullColumnDefinition { get; }

        protected SqlDialectBase(string name, string version, TypeMapper typeMapper, DialectContext context)
        {
            Name = name;
            Version = version;
            TypeMapper = typeMapper;
            Context = context;
        }

        public abstract string Quote(string identifier);

        public abstract string EscapeString(string value);

        public abstract string CreateTable(CreateTableOperation operation);

        public abstract string DropTable(DropTableOperation operation);

        public abstract string RenameTable(RenameTableOperation operation);

        public abstract string CommentTable(CommentTableOperation operation);

        /// <summary>视图是所有方言都支持的对象，默认按标准语法生成；方言按需覆盖（SQL Server 用 CREATE OR ALTER，SQLite 不支持 OR REPLACE）。</summary>
        public virtual string View(ViewOperation operation)
        {
            if (operation.Action == ObjectAction.Drop)
            {
                return "DROP VIEW IF EXISTS " + Quote(operation.Name) + ";";
            }
            string columns = string.IsNullOrEmpty(operation.Columns) ? "" : " (" + operation.Columns + ")";
            return "CREATE OR REPLACE VIEW " + Quote(operation.Name) + columns + " AS\n" + operation.Select + ";";
        }

        /// <summary>默认无原生序列：MySQL / MariaDB / SQLite 走此实现并报错。</summary>
        public virtual string Sequence(SequenceOperation operation)
        {
            Error("E046", operation.Name, null, Name + " 没有原生序列对象，请改用自增列或 <rawSql>");
            return "";
        }

        /// <summary>默认不支持表选项：SQL Server / SQLite 走此实现并报错。</summary>
        public virtual string TableOption(TableOptionOperation operation)
        {
            Error("E047", operation.Table, null,
                Name + " 不支持调整表选项（ENGINE / CHARSET / COLLATE / TABLESPACE），请改用 <rawSql>");
            return "";
        }

        /// <summary>默认不支持索引注释；支持的方言（PG / SQL Server）覆盖。</summary>
        public virtual string CommentIndex(IndexOperation operation)
        {
            Warn("W031", operation.Table, null, Name + " 不支持索引注释，已跳过（可用 <rawSql> 实现）");
            return "";
        }

        /// <summary>默认不支持约束注释；支持的方言（PG / SQL Server）覆盖。</summary>
        public virtual string CommentConstraint(ConstraintCommentOperation operation)
        {
            Warn("W031", operation.Table, null, Name + " 不支持约束注释，已跳过（可用 <rawSql> 实现）");
            return "";
        }

        public abstract string AddColumn(ColumnOperation operation);

        public abstract string ModifyColumn(ColumnOperation operation);

        public abstract string DropColumn(ColumnOperation operation);

        public abstract string RenameColumn(ColumnOperation operation);

        public abstract string CommentColumn(ColumnOperation operation);

        public abstract string ReorderColumn(ColumnOperation operation);

        public abstract string AddPrimaryKey(PrimaryKeyOperation operation);

        public abstract string DropPrimaryKey(PrimaryKeyOperation operation);

        public abstract string CreateIndex(IndexOperation operation);

        public abstract string DropIndex(IndexOperation operation);

        public abstract string RenameIndex(IndexOperation operation);

        public abstract string AddForeignKey(ForeignKeyOperation operation);

        public abstract string DropForeignKey(ForeignKeyOperation operation);

        public abstract string AddUniqueConstraint(UniqueConstraintOperation operation);

        public abstract string DropUniqueConstraint(UniqueConstraintOperation operation);

        public abstract string AddCheckConstraint(CheckConstraintOperation operation);

        public abstract string DropCheckConstraint(CheckConstraintOperation operation);

        public abstract string AddDefaultConstraint(DefaultConstraintOperation operation);

        public abstract string DropDefaultConstraint(DefaultConstraintOperation operation);

        public abstract string RawSql(RawSqlOperation operation);

        /// <summary>
        /// 从"按方言的 SQL 表"里取当前方言的语句。缺当前方言时输出警告而不是静默丢弃，
        /// 避免视图 / 触发器 / 存储过程等对象在某个方言上无声消失。
        /// </summary>
        protected string ResolveDialectSql(IDictionary<string, string> sqlByDialect, string table, string code)
        {
            if (sqlByDialect == null || sqlByDialect.Count == 0)
            {
                return "";
            }

            string value;
            if (sqlByDialect.TryGetValue(Name, out value))
            {
                return value;
            }
            if (sqlByDialect.TryGetValue("any", out value))
            {
                return value;
            }

            Warn(code, table, null, "未提供 " + Name + " 方言的 SQL（节点只声明了 "
                + string.Join(" / ", new List<string>(sqlByDialect.Keys)) + "），已跳过");
            return "";
        }

        public virtual string Update(UpdateOperation operation)
        {
            string text = "UPDATE " + Quote(operation.Table) + " SET " + operation.Set;
            if (!string.IsNullOrEmpty(operation.Where))
            {
                text = text + " WHERE " + operation.Where;
            }
            return text + ";";
        }

        public virtual string Delete(DeleteOperation operation)
        {
            string text = "DELETE FROM " + Quote(operation.Table);
            if (!string.IsNullOrEmpty(operation.Where))
            {
                text = text + " WHERE " + operation.Where;
            }
            return text + ";";
        }

        public virtual string Clean(CleanOperation operation)
        {
            string text = (operation.Rules ?? "").Replace(";", ",");
            string text2 = "UPDATE " + Quote(operation.Table) + " SET " + text;
            if (!string.IsNullOrEmpty(operation.Where))
            {
                text2 = text2 + " WHERE " + operation.Where;
            }
            return text2 + ";";
        }

        public virtual string Backfill(BackfillOperation operation)
        {
            string text = "UPDATE " + Quote(operation.Table) + " SET " + Quote(operation.Column) + " = " + operation.Value;
            if (!string.IsNullOrEmpty(operation.Where))
            {
                text = text + " WHERE " + operation.Where;
            }
            return text + ";";
        }

        /// <summary>
        /// 默认种子数据：普通 INSERT。声明了冲突策略但本方言不支持的，降级并警告（W029）。
        /// </summary>
        public virtual string BuildSeed(SeedOperation operation, IReadOnlyList<string> primaryKeyColumns)
        {
            string text = BuildSeedInsert(operation);
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            if (!string.IsNullOrEmpty(operation.Conflict))
            {
                Warn("W029", operation.Table, null,
                    Name + " 未实现 SEED conflict=" + operation.Conflict + "，已按普通 INSERT 生成（冲突会中止执行）");
            }
            return text + ";";
        }

        /// <summary>INSERT ... VALUES 主体（不含结尾分号与冲突子句）。</summary>
        protected string BuildSeedInsert(SeedOperation operation, string insertVerb = "INSERT INTO")
        {
            if (operation.Rows.Count == 0 || operation.Columns.Count == 0)
            {
                return "";
            }

            List<string> rows = new List<string>();
            foreach (List<string> row in operation.Rows)
            {
                rows.Add("(" + string.Join(", ", row) + ")");
            }

            List<string> columns = new List<string>();
            foreach (string column in operation.Columns)
            {
                columns.Add(Quote(column));
            }

            return insertVerb + " " + Quote(operation.Table) + " (" + string.Join(", ", columns) + ") VALUES\n"
                + string.Join(",\n", rows);
        }

        public virtual bool RequiresRebuild(SchemaOperation operation)
        {
            return false;
        }

        public virtual string RebuildTable(RebuildTableOperation operation)
        {
            return "";
        }

        protected bool VersionAtLeast(int major, int minor)
        {
            if (!System.Version.TryParse(Version, out var result))
            {
                return true;
            }
            if (result.Major > major)
            {
                return true;
            }
            return result.Major == major && result.Minor >= minor;
        }

        protected string JoinQuoted(IEnumerable<string> identifiers)
        {
            List<string> list = new List<string>();
            foreach (string identifier in identifiers)
            {
                list.Add(Quote(identifier));
            }
            return string.Join(", ", list);
        }

        protected virtual string FormatDefaultValue(string value)
        {
            if (value == null)
            {
                return "NULL";
            }
            string text = value.Trim();
            if (text.Length == 0)
            {
                return "''";
            }
            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                return "NULL";
            }
            if (text.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase) || text.StartsWith("NOW()", StringComparison.OrdinalIgnoreCase) || text.StartsWith("SYSUTCDATETIME", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
            if (FunctionCallPattern.IsMatch(text))
            {
                // 函数默认值（UTC_TIMESTAMP() / datetime('now') 等）原样透传，不加引号
                return text;
            }
            if ((text.StartsWith("'") && text.EndsWith("'") && text.Length >= 2) || (text.StartsWith("(") && text.EndsWith(")")))
            {
                return text;
            }
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var _))
            {
                return text;
            }
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
            }
            return "'" + EscapeString(value) + "'";
        }

        protected void Warn(string code, string table, string column, string message)
        {
            Context.Warn(code, table, column, message);
        }

        protected void Error(string code, string table, string column, string message)
        {
            Context.Error(code, table, column, message);
        }

        protected void WarnIndexTypeUnsupported(IndexOperation operation)
        {
            if (!string.IsNullOrEmpty(operation.Type) && !string.Equals(operation.Type, "BTREE", StringComparison.OrdinalIgnoreCase) && !string.Equals(operation.Type, "HASH", StringComparison.OrdinalIgnoreCase))
            {
                Warn("W017", operation.Table, null, Name + " 不支持索引类型 " + operation.Type + "，已按普通索引生成");
            }
        }

        protected static string Append(params string[] parts)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (string value in parts)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    stringBuilder.Append(value);
                }
            }
            return stringBuilder.ToString();
        }
    }
}