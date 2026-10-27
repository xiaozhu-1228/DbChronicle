using System.Collections.Generic;

using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class ForeignKeyOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public List<string> Columns { get; } = new List<string>();
        public string RefTable { get; set; }
        public List<string> RefColumns { get; } = new List<string>();
        public string OnDelete { get; set; }
        public string OnUpdate { get; set; }

        /// <summary>是否属于某个 &lt;table action="CREATE"&gt; 单元（已随建表内联，不应再触发 rebuild）。</summary>
        public bool PartOfCreateTable { get; set; }
    }
}