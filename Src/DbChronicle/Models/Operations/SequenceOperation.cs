using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    /// <summary>
    /// 序列操作。仅 PostgreSQL / SQL Server 有原生序列；MySQL / MariaDB / SQLite 编译时报错。
    /// Table 与 Name 都存序列名（Table 供排序分组使用）。
    /// </summary>
    public sealed class SequenceOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }

        public string Name { get; set; } = "";

        public long? StartWith { get; set; }

        public long? IncrementBy { get; set; }

        public long? MinValue { get; set; }

        public long? MaxValue { get; set; }

        public bool? Cycle { get; set; }

        public long? Cache { get; set; }
    }
}
