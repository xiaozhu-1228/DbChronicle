namespace DbChronicle.Diagnostics
{
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
        public const string UnknownNodeIgnored = "W020";
        public const string WildcardExpanded = "W021";
        public const string WildcardPlaceholderKept = "W022";
        public const string CheckConstraintIgnored = "W023";
        public const string DefaultConstraintUnsupported = "W024";
        public const string ConstraintSkipped = "W025";
        public const string RebuildRequired = "W026";
        public const string PartialIndexIgnored = "W027";
        public const string RenameIndexUnsupported = "W028";
        public const string SeedConflictUnsupported = "W029";
        public const string RebuildLimitation = "W030";
        public const string ObjectCommentUnsupported = "W031";
        public const string DialectSqlMissing = "W032";
        public const string DataMigrationSqlMissing = "W033";
        public const string ColumnReorderUnsupported = "E044";
        public const string SequenceUnsupported = "E046";
        public const string TableOptionUnsupported = "E047";
        public const string StatementSplitFailed = "E042";
        public const string WildcardUnsupported = "E041";
        public const string RawSqlEmpty = "E051";
    }
}
