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
namespace DbChronicle.Abstractions.Versioning
{
    /// <summary>一条已应用（或失败）的变更单元记录。</summary>
    public sealed class AppliedChange
    {
        public string ChangeId { get; set; } = "";
        public string Release { get; set; } = "";
        public string Checksum { get; set; } = "";
        public string Dialect { get; set; } = "";
        public string AppliedBy { get; set; } = "";
        public string Description { get; set; } = "";
        public string AppliedAt { get; set; } = "";
        public bool HasFailure { get; set; }
        public int? FailureStatement { get; set; }
    }
}
