namespace DbChronicle.Models.Operations
{
    /// <summary>给已有约束（唯一 / 检查 / 主键 / 默认值）写注释。</summary>
    public sealed class ConstraintCommentOperation : SchemaOperation
    {
        public string Name { get; set; } = "";

        /// <summary>约束类型：unique / check / primaryKey / default，用于方言生成定位语句。</summary>
        public string Kind { get; set; }

        public string Comment { get; set; }
    }
}