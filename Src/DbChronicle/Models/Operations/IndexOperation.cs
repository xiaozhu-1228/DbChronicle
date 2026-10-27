using System.Collections.Generic;

using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class IndexOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string NewName { get; set; }
        public bool Unique { get; set; }
        public string Type { get; set; }
        public List<string> Columns { get; } = new List<string>();
        public string Expression { get; set; }
        public string Where { get; set; }
        public string Comment { get; set; }
    }
}