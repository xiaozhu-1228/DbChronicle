namespace DbChronicle.Configuration
{
    public sealed class OptionSettings
    {
        public bool QuoteIdentifiers { get; set; } = true;
        public bool IncludeComments { get; set; } = true;
        public bool IncludeRollback { get; set; }
        public string StatementDelimiter { get; set; } = ";";
        public bool AllowDestructive { get; set; }
        public int LockTimeoutSeconds { get; set; } = 10;
        public bool IncrementalModify { get; set; } = true;

        /// <summary>提交粒度：unit（整单元一个事务，默认）/ table（按表提交）/ batch（每 N 张表提交）。</summary>
        public string CommitScope { get; set; } = "unit";

        /// <summary>commitScope=batch 时每批的表数。</summary>
        public int CommitBatchSize { get; set; } = 10;
    }
}