using System.Collections.Generic;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Dialects
{
    public sealed class DialectContext
    {
        private readonly List<CompileDiagnostic> _diagnostics = new List<CompileDiagnostic>();

        public string ChangeId { get; set; }

        public bool IncrementalModify { get; set; } = true;


        public IReadOnlyList<CompileDiagnostic> Diagnostics => _diagnostics;

        public void Begin(string changeId)
        {
            ChangeId = changeId;
            _diagnostics.Clear();
        }

        public void Warn(string code, string table, string column, string message)
        {
            Add(code, DiagnosticSeverity.Warning, table, column, message);
        }

        /// <summary>
        /// 方言无法实现且不能降级的操作（如 PG 的列顺序调整）：报错并阻断编译，避免静默丢弃用户意图。
        /// </summary>
        public void Error(string code, string table, string column, string message)
        {
            Add(code, DiagnosticSeverity.Error, table, column, message);
        }

        private void Add(string code, DiagnosticSeverity severity, string table, string column, string message)
        {
            _diagnostics.Add(new CompileDiagnostic
            {
                Code = code,
                Severity = severity,
                ChangeId = ChangeId,
                Table = table,
                Column = column,
                Message = message
            });
        }
    }
}