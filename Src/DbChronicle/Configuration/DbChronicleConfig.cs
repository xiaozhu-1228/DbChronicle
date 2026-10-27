using System.IO;
using System;

namespace DbChronicle.Configuration
{
    public sealed class DbChronicleConfig
    {
        public ComponentSettings Component { get; set; } = new ComponentSettings();
        public DatabaseSettings Database { get; set; } = new DatabaseSettings();
        public string Environment { get; set; } = "dev";
        public PathSettings Paths { get; set; } = new PathSettings();
        public BaselineSettings Baseline { get; set; } = new BaselineSettings();
        public OptionSettings Options { get; set; } = new OptionSettings();

        public string ConfigPath { get; set; }

        public string BaseDirectory { get; set; }

        public string ResolveMigrationsPath()
        {
            return ResolvePath(Paths.Migrations);
        }

        public string ResolveVersionLogPath()
        {
            return ResolvePath(Paths.VersionLog.Replace("${environment}", Environment ?? "dev"));
        }

        public string ResolveOutputPath()
        {
            return ResolvePath(Paths.Output);
        }

        public string ResolveBaselinePath()
        {
            if (string.IsNullOrEmpty(Baseline.File))
            {
                return null;
            }
            return ResolvePath(Baseline.File);
        }

        private string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BaseDirectory ?? Directory.GetCurrentDirectory();
            }
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            var root = BaseDirectory ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, path));
        }
    }
}