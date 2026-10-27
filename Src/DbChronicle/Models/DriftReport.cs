using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class DriftReport
    {
        public List<string> MissingColumns { get; } = new List<string>();

        public List<string> ExtraColumns { get; } = new List<string>();

        public List<string> UnreadableTables { get; } = new List<string>();

        public bool HasDrift
        {
            get { return MissingColumns.Count > 0; }
        }
    }
}