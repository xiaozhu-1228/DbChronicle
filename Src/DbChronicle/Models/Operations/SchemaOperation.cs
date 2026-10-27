using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public abstract class SchemaOperation
    {
        public string Table { get; set; } = "";
        public OperationStage Stage { get; set; } = OperationStage.After;

        /// <summary>table="*" 时的显式表清单（逗号分隔），用于固定范围。</summary>
        public string Tables { get; set; }

        /// <summary>是否由通配符展开而来（用于排序：通配符语句优先执行）。</summary>
        public bool FromWildcard { get; set; }

        /// <summary>浅拷贝，供通配符（table="*"）展开时按表复制。</summary>
        public SchemaOperation Clone()
        {
            return (SchemaOperation)MemberwiseClone();
        }
    }
}