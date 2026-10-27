using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class CompilerOptions
    {
        public bool IncrementalModify { get; set; } = true;


        public bool IncludeComments { get; set; } = true;


        public string StatementDelimiter { get; set; }

        public IReadOnlyList<string> WildcardTables { get; set; }
    }
}