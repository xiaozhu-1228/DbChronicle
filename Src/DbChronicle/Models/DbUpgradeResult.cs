using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class DbUpgradeResult
    {
        public bool Success { get; set; }

        public int AppliedCount { get; set; }

        public int SkippedCount { get; set; }

        /// <summary>本次执行（或将要执行）的 SQL 脚本。</summary>
        public string Sql { get; set; } = "";

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        /// <summary>continueOnError 模式下的失败明细（单元 / 表 / 原因）。</summary>
        public IReadOnlyList<string> Failures { get; set; } = new List<string>();

        /// <summary>continueOnError 模式下失败的单元数。</summary>
        public int FailedCount { get; set; }

        public IReadOnlyList<CompileDiagnostic> Diagnostics { get; set; } = new List<CompileDiagnostic>();
}
}