namespace DbChronicle.Migrations.State
{
    /// <summary>一条已应用（或失败）的变更单元记录。</summary>
    public sealed class AppliedChange
    {
        public string ChangeId { get; set; } = "";
        public string Release { get; set; } = "";
        public string Checksum { get; set; } = "";
        public string Dialect { get; set; } = "";
        public string AppliedBy { get; set; } = "";
        public string Description { get; set; } = "";
        public string AppliedAt { get; set; } = "";
        public bool HasFailure { get; set; }
        public int? FailureStatement { get; set; }
    }
}