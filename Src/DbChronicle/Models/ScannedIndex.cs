using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class ScannedIndex
    {
        public string Name { get; set; } = "";

        public bool Unique { get; set; }

        public List<string> Columns { get; } = new List<string>();
}
}