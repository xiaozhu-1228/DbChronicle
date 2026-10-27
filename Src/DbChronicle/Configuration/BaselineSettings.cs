using System.IO;

namespace DbChronicle.Configuration
{
    public sealed class BaselineSettings
    {
        public string File { get; set; } = "migrations/00000000_000000_baseline.xml";
        public bool Frozen { get; set; }
        public string FrozenAt { get; set; }
    }
}