namespace DbChronicle.Common.Enums
{
    /// <summary>SQL 语句拆分器的扫描状态。</summary>
    internal enum SqlScanState
    {
        Normal,
        SingleQuote,
        DoubleQuote,
        Backtick,
        Bracket,
        LineComment,
        BlockComment,
        DollarQuote
    }
}
