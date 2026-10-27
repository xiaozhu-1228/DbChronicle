using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class RollbackResult
    {
        public bool Success { get; set; }

        public int RolledBackCount { get; set; }

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        public List<string> Blockers { get; } = new List<string>();
    }
}