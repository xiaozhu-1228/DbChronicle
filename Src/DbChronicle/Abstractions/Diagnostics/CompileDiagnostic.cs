
namespace DbChronicle.Abstractions.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public static class DiagnosticCodes
    {
        public const string UnsignedIgnored = "W001";
        public const string OnUpdateIgnored = "W002";
        public const string DateTimeOffsetDowngraded = "W003";
        public const string XmlDowngraded = "W004";
        public const string BlobLengthIgnored = "W005";
        public const string TableCommentSkipped = "W006";
        public const string ColumnCommentSkipped = "W007";
        public const string ModifyDefaultNotDeclared = "W008";
        public const string FileEncodingNotStandard = "W009";
        public const string ColumnOrderIgnored = "W010";
        public const string ReleaseDateMismatch = "W012";
        public const string ReleaseCrossDay = "W013";
        public const string IncrementalBaseMissing = "W014";
        public const string WildcardExpanded = "W021";
        public const string WildcardPlaceholderKept = "W022";
        public const string WildcardUnsupported = "E041";
    }

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
