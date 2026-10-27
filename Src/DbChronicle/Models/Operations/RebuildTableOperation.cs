using System.Collections.Generic;

using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class RebuildTableOperation : SchemaOperation
    {
        public List<ColumnDefinition> Columns { get; } = new List<ColumnDefinition>();
        public List<string> PrimaryKeyColumns { get; } = new List<string>();
        public List<KeyValuePair<string, string>> ColumnMappings { get; } = new List<KeyValuePair<string, string>>();
        public Dictionary<string, LogicalType> ColumnSourceTypes { get; } =
            new Dictionary<string, LogicalType>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>重建后需要保留的外键（历史外键 + 本单元新增，来自目标结构模型）。</summary>
        public List<ForeignKeyOperation> ForeignKeys { get; } = new List<ForeignKeyOperation>();

        /// <summary>重建后需要保留的检查约束（来自目标结构模型）。</summary>
        public List<CheckConstraintOperation> CheckConstraints { get; } = new List<CheckConstraintOperation>();
    }
}