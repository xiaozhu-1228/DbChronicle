using System.Collections.Generic;

namespace DbChronicle.Models
{
    /// <summary>数据库反向扫描的过滤选项。</summary>
    public sealed class ScanDatabaseOptions
    {
        /// <summary>只扫描这些表（为空表示全部）。</summary>
        public List<string> Tables { get; } = new List<string>();

        /// <summary>排除这些表（支持逗号分隔由调用方拆分）。</summary>
        public List<string> ExcludeTables { get; } = new List<string>();

        public bool IncludeIndexes { get; set; } = true;

        public bool IncludeForeignKeys { get; set; } = true;

        public bool IncludeCheckConstraints { get; set; } = true;

        public bool IncludeComments { get; set; } = true;

        public bool ShouldInclude(string table)
        {
            if (Tables.Count > 0 && !Tables.Exists(x => string.Equals(x, table, System.StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            return !ExcludeTables.Exists(x => string.Equals(x, table, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
