using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class ApplyResult
    {
        public bool Success { get; set; }

        public int AppliedCount { get; set; }

        public int SkippedCount { get; set; }

        /// <summary>本次编译出的 SQL（含跳过单元的编译结果，dry-run 也可取）。</summary>
        public string Sql { get; set; } = "";

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        /// <summary>continueOnError 模式下的失败明细（单元 / 表 / 原因）。</summary>
        public List<string> Failures { get; } = new List<string>();

        /// <summary>continueOnError 模式下失败的单元数。</summary>
        public int FailedCount { get; set; }

        public List<CompileDiagnostic> Diagnostics { get; } = new List<CompileDiagnostic>();
    }
}