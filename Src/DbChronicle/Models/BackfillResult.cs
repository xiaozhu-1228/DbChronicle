namespace DbChronicle.Models
{
    public sealed class BackfillResult
    {
        public bool Success { get; set; }

        public int BackfilledCount { get; set; }

        public int SkippedCount { get; set; }

        public string Message { get; set; }

        public int? ExitCode { get; set; }
    }
}