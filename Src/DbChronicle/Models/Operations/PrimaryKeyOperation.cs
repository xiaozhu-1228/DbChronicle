using System.Collections.Generic;

using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class PrimaryKeyOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; }
        public List<string> Columns { get; } = new List<string>();
    }
}