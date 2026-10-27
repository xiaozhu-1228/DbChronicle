using System;

namespace DbChronicle.Migrations.Executor
{
    /// <summary>
    /// 单条语句执行失败：携带表名、语句序号与已完成进度，便于定位到具体表。
    /// </summary>
    public sealed class StatementExecutionException : Exception
    {
        public StatementExecutionException(string table, int statementIndex, int statementInGroup,
            string sql, int completed, int total, Exception inner)
            : base(inner == null ? "执行失败" : inner.Message, inner)
        {
            Table = table;
            StatementIndex = statementIndex;
            StatementInGroup = statementInGroup;
            Sql = sql;
            Completed = completed;
            Total = total;
        }

        /// <summary>语句所属表。</summary>
        public string Table { get; private set; }

        /// <summary>在整个单元中的语句序号（从 0 开始）。</summary>
        public int StatementIndex { get; private set; }

        /// <summary>在所属表组内的序号（从 0 开始）。</summary>
        public int StatementInGroup { get; private set; }

        public string Sql { get; private set; }

        /// <summary>失败前已完成的数量（表级提交时是表数，语句级时是语句数）。</summary>
        public int Completed { get; private set; }

        public int Total { get; private set; }

        public string Describe()
        {
            var table = string.IsNullOrEmpty(Table) ? "（未指定表）" : "表 " + Table;
            return table + " 的第 " + (StatementInGroup + 1) + " 条语句失败（整体第 " + (StatementIndex + 1)
                + "/" + Total + " 条，此前已完成 " + Completed + "）："
                + (InnerException == null ? Message : InnerException.Message);
        }
    }
}
