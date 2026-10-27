using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    /// <summary>
    /// 视图操作：只描述"有哪些列、来自哪个 SELECT"，由方言负责生成 CREATE / CREATE OR REPLACE / DROP。
    /// Table 与 Name 都存视图名（Table 供排序分组使用）。
    /// </summary>
    public sealed class ViewOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }

        public string Name { get; set; } = "";

        /// <summary>SELECT 语句体（不含 CREATE VIEW 前缀）。</summary>
        public string Select { get; set; }

        /// <summary>可选列清单（逗号分隔），空则由 SELECT 决定。</summary>
        public string Columns { get; set; }

        public string Comment { get; set; }
    }
}
