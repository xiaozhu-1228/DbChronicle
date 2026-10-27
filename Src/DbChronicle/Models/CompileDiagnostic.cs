using DbChronicle.Common.Enums;

namespace DbChronicle.Models
{
    public sealed class CompileDiagnostic
    {
        public string Code { get; set; } = "";
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Warning;
        public string ChangeId { get; set; }
        public string Table { get; set; }
        public string Column { get; set; }
        public string Message { get; set; } = "";

        public static CompileDiagnostic Warning(string code, string message)
        {
            return new CompileDiagnostic { Code = code, Severity = DiagnosticSeverity.Warning, Message = message };
        }

        public override string ToString()
        {
            var target = Table;
            if (!string.IsNullOrEmpty(Column))
            {
                target = string.IsNullOrEmpty(target) ? Column : Table + "." + Column;
            }

            var prefix = Severity == DiagnosticSeverity.Error ? "error" : (Severity == DiagnosticSeverity.Info ? "info" : "warning");
            return string.IsNullOrEmpty(target)
                ? prefix + " " + Code + ": " + Message
                : prefix + " " + Code + " " + target + ": " + Message;
}
}
}
