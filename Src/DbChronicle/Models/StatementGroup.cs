using System.Collections.Generic;

namespace DbChronicle.Models
{
    /// <summary>
    /// 归属于同一张表的语句组。commitScope=table / batch 时按组提交，失败只回滚当前组。
    /// </summary>
    public sealed class StatementGroup
    {
        public StatementGroup(string table)
        {
            Table = table ?? "";
        }

        /// <summary>组对应的表名；空字符串表示不归属具体表（如独立 rawSql）。</summary>
        public string Table { get; private set; }

        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();
    }
}