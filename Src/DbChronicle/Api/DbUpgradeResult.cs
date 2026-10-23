using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle
{
    public sealed class DbUpgradeOptions
    {
        /// <summary>只生成并打印 SQL，不执行。</summary>
        public bool DryRun { get; set; }

        /// <summary>记录到版本表的执行人；默认当前用户。</summary>
        public string AppliedBy { get; set; }
    }

    public sealed class DbUpgradeResult
    {
        public bool Success { get; set; }

        public int AppliedCount { get; set; }

        public int SkippedCount { get; set; }

        /// <summary>本次执行（或将要执行）的 SQL 脚本。</summary>
        public string Sql { get; set; } = "";

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        public IReadOnlyList<CompileDiagnostic> Diagnostics { get; set; } = new List<CompileDiagnostic>();
    }
}
