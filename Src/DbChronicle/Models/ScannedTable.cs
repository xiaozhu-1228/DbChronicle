using System.Collections.Generic;

using DbChronicle.Models.Operations;

namespace DbChronicle.Models
{
    /// <summary>C# 实体扫描与数据库反向扫描共用的表模型。</summary>
    public sealed class ScannedTable
    {
        public string Name { get; set; } = "";

        public string Comment { get; set; }

        public List<ScannedColumn> Columns { get; } = new List<ScannedColumn>();

        public List<ScannedIndex> Indexes { get; } = new List<ScannedIndex>();

        /// <summary>复合主键的列顺序（单列主键也记录，便于 SQLite 等按顺序输出）。</summary>
        public List<string> PrimaryKeyColumns { get; } = new List<string>();

        /// <summary>表级外键（数据库反向扫描填充；C# 扫描暂不产生）。</summary>
        public List<ForeignKeyOperation> ForeignKeys { get; } = new List<ForeignKeyOperation>();

        /// <summary>表级检查约束（数据库反向扫描填充）。</summary>
        public List<CheckConstraintOperation> CheckConstraints { get; } = new List<CheckConstraintOperation>();

        /// <summary>表级唯一约束（数据库反向扫描填充，区别于唯一索引）。</summary>
        public List<UniqueConstraintOperation> UniqueConstraints { get; } = new List<UniqueConstraintOperation>();
    }
}