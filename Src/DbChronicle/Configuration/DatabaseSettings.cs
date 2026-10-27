using System;

namespace DbChronicle.Configuration
{
    public sealed class DatabaseSettings
    {
        public string Dialect { get; set; } = "mysql";
        public string Version { get; set; } = "8.0";
    }
}