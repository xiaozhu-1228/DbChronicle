using System.Collections.Generic;

namespace DbChronicle.Models
{
    public sealed class ScanResult
    {
        public List<ScannedTable> Tables { get; } = new List<ScannedTable>();

        public List<string> Warnings { get; } = new List<string>();
    }
}