using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Domain.Sql;

namespace DbChronicle.Compilation.Compiler
{
    public sealed class CompileResult
    {
        public string Sql { get; set; } = "";
        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();
        public List<CompileDiagnostic> Diagnostics { get; } = new List<CompileDiagnostic>();

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
