using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Domain.Operations;

namespace DbChronicle.Domain.Model
{
    public sealed class ChangeUnit
    {
        public string Id { get; set; } = "";
        public string Author { get; set; } = "";
        public string Release { get; set; } = "";
        public string Description { get; set; } = "";
        public string SourcePath { get; set; }
        public string Checksum { get; set; }
        public List<string> Depends { get; } = new List<string>();
        public List<SchemaOperation> Operations { get; } = new List<SchemaOperation>();
        public List<CompileDiagnostic> Diagnostics { get; } = new List<CompileDiagnostic>();

        public bool IsBaseline
        {
            get { return Id == "00000000_000000_baseline" || Release == "00000000"; }
        }

        public override string ToString()
        {
            return Id;
        }
    }
}
