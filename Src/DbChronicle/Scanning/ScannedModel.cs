using System.Collections.Generic;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Scanning
{
    public sealed class ScanResult
    {
        public List<ScannedTable> Tables { get; } = new List<ScannedTable>();

        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class ScannedTable
    {
        public string Name { get; set; } = "";

        public string Comment { get; set; }

        public List<ScannedColumn> Columns { get; } = new List<ScannedColumn>();

        public List<ScannedIndex> Indexes { get; } = new List<ScannedIndex>();
    }

    public sealed class ScannedColumn
    {
        public string Name { get; set; } = "";

        public LogicalType Type { get; set; }

        public int? Length { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }

        public bool Nullable { get; set; }

        public bool PrimaryKey { get; set; }

        public bool AutoIncrement { get; set; }

        public bool Unsigned { get; set; }

        public string Comment { get; set; }

        public string DefaultValue { get; set; }
    }

    public sealed class ScannedIndex
    {
        public string Name { get; set; } = "";

        public bool Unique { get; set; }

        public List<string> Columns { get; } = new List<string>();
    }
}
