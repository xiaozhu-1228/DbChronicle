using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
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
