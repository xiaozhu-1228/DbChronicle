namespace DbChronicle.Configuration
{
    public sealed class PathSettings
    {
        public string Migrations { get; set; } = "./migrations";
        public string VersionLog { get; set; } = "./version_log/${environment}";
        public string Output { get; set; } = "./output";
    }
}