using System.Collections.Generic;

namespace DbChronicle.Models.Operations
{
    public sealed class SeedOperation : SchemaOperation
    {
        public string Conflict { get; set; }
        public List<string> Columns { get; } = new List<string>();
        public List<List<string>> Rows { get; } = new List<List<string>>();
    }
}
