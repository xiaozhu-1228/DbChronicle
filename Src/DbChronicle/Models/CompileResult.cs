using System.Collections.Generic;
using System;

using DbChronicle.Common.Enums;

namespace DbChronicle.Models
{
    public sealed class CompileResult
    {
        public string Sql { get; set; } = "";
        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();
        public List<CompileDiagnostic> Diagnostics { get; } = new List<CompileDiagnostic>();

        /// <summary>按表分组的语句（组内顺序与 Statements 一致），供 commitScope=table / batch 使用。</summary>
        public List<StatementGroup> Groups { get; } = new List<StatementGroup>();

        public StatementGroup GroupFor(string table)
        {
            var key = table ?? "";
            foreach (var group in Groups)
            {
                if (string.Equals(group.Table, key, StringComparison.OrdinalIgnoreCase))
                {
                    return group;
                }
            }

            var created = new StatementGroup(key);
            Groups.Add(created);
            return created;
        }

        public bool HasErrors
        {
            get
            {
                foreach (var d in Diagnostics)
                {
                    if (d.Severity == DiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool HasWarning(string code)
        {
            foreach (var d in Diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Warning && d.Code == code)
                {
                    return true;
                }
            }
            return false;
        }
    }
}