using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

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

    public sealed class ComponentSettings
    {
        public string Name { get; set; } = "DbChronicle";
        public string Version { get; set; } = "1.0.0";
    }

    public sealed class DatabaseSettings
    {
        public string Dialect { get; set; } = "mysql";
        public string Version { get; set; } = "8.0";
    }

    public sealed class PathSettings
    {
        public string Migrations { get; set; } = "./migrations";
        public string VersionLog { get; set; } = "./version_log/${environment}";
        public string Output { get; set; } = "./output";
    }

    public sealed class BaselineSettings
    {
        public string File { get; set; } = "migrations/00000000_000000_baseline.xml";
        public bool Frozen { get; set; }
        public string FrozenAt { get; set; }
    }

    public sealed class OptionSettings
    {
        public bool QuoteIdentifiers { get; set; } = true;
        public bool IncludeComments { get; set; } = true;
        public bool IncludeRollback { get; set; }
        public string StatementDelimiter { get; set; } = ";";
        public bool AllowDestructive { get; set; }
        public int LockTimeoutSeconds { get; set; } = 10;
        public bool IncrementalModify { get; set; } = true;
    }

    public static class ConfigLoader
    {
        public const string DefaultFileName = "DbchronicleConfig.xml";

        public static DbChronicleConfig Load(string configPath)
        {
            var fullPath = Path.GetFullPath(configPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("配置文件不存在：" + fullPath, fullPath);
            }

            XDocument document;
            using (var stream = File.OpenRead(fullPath))
            {
                document = XDocument.Load(stream, LoadOptions.None);
            }

            var root = document.Root;
            if (root == null)
            {
                throw new InvalidOperationException("配置文件内容为空：" + fullPath);
            }

            var config = new DbChronicleConfig();
            config.ConfigPath = fullPath;
            config.BaseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

            var component = root.Element("component");
            if (component != null)
            {
                config.Component.Name = Attr(component, "name") ?? config.Component.Name;
                config.Component.Version = Attr(component, "version") ?? config.Component.Version;
            }

            var database = root.Element("database");
            if (database != null)
            {
                config.Database.Dialect = Attr(database, "dialect") ?? config.Database.Dialect;
                config.Database.Version = Attr(database, "version") ?? config.Database.Version;
            }

            var environment = root.Element("environment");
            if (environment != null && !string.IsNullOrEmpty(environment.Value.Trim()))
            {
                config.Environment = environment.Value.Trim();
            }

            var paths = root.Element("paths");
            if (paths != null)
            {
                config.Paths.Migrations = Attr(paths, "migrations") ?? config.Paths.Migrations;
                config.Paths.VersionLog = Attr(paths, "versionLog") ?? config.Paths.VersionLog;
                config.Paths.Output = Attr(paths, "output") ?? config.Paths.Output;
            }

            var baseline = root.Element("baseline");
            if (baseline != null)
            {
                config.Baseline.File = Attr(baseline, "file") ?? config.Baseline.File;
                config.Baseline.Frozen = BoolAttr(baseline, "frozen", config.Baseline.Frozen);
                config.Baseline.FrozenAt = Attr(baseline, "frozenAt");
            }

            var options = root.Element("options");
            if (options != null)
            {
                config.Options.QuoteIdentifiers = BoolAttr(options, "quoteIdentifiers", config.Options.QuoteIdentifiers);
                config.Options.IncludeComments = BoolAttr(options, "includeComments", config.Options.IncludeComments);
                config.Options.IncludeRollback = BoolAttr(options, "includeRollback", config.Options.IncludeRollback);
                config.Options.StatementDelimiter = Attr(options, "statementDelimiter") ?? config.Options.StatementDelimiter;
                config.Options.AllowDestructive = BoolAttr(options, "allowDestructive", config.Options.AllowDestructive);
                config.Options.LockTimeoutSeconds = IntAttr(options, "lockTimeoutSeconds", config.Options.LockTimeoutSeconds);
                config.Options.IncrementalModify = BoolAttr(options, "incrementalModify", config.Options.IncrementalModify);
            }

            Normalize(config);
            return config;
        }

        public static DbChronicleConfig CreateDefault(string configPath)
        {
            var config = new DbChronicleConfig();
            config.ConfigPath = Path.GetFullPath(configPath);
            config.BaseDirectory = Path.GetDirectoryName(config.ConfigPath) ?? Directory.GetCurrentDirectory();
            Normalize(config);
            return config;
        }

        public static void Save(DbChronicleConfig config, string configPath)
        {
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("dbchronicle",
                    new XElement("component",
                        new XAttribute("name", config.Component.Name ?? "DbChronicle"),
                        new XAttribute("version", config.Component.Version ?? "1.0.0")),
                    new XElement("database",
                        new XAttribute("dialect", config.Database.Dialect ?? "mysql"),
                        new XAttribute("version", config.Database.Version ?? "8.0")),
                    new XElement("environment", config.Environment ?? "dev"),
                    new XElement("paths",
                        new XAttribute("migrations", config.Paths.Migrations ?? "./migrations"),
                        new XAttribute("versionLog", config.Paths.VersionLog ?? "./version_log/${environment}"),
                        new XAttribute("output", config.Paths.Output ?? "./output")),
                    new XElement("baseline",
                        new XAttribute("file", config.Baseline.File ?? ""),
                        new XAttribute("frozen", config.Baseline.Frozen ? "true" : "false"),
                        new XAttribute("frozenAt", config.Baseline.FrozenAt ?? "")),
                    new XElement("options",
                        new XAttribute("quoteIdentifiers", config.Options.QuoteIdentifiers ? "true" : "false"),
                        new XAttribute("includeComments", config.Options.IncludeComments ? "true" : "false"),
                        new XAttribute("includeRollback", config.Options.IncludeRollback ? "true" : "false"),
                        new XAttribute("statementDelimiter", config.Options.StatementDelimiter ?? ";"),
                        new XAttribute("allowDestructive", config.Options.AllowDestructive ? "true" : "false"),
                        new XAttribute("lockTimeoutSeconds", config.Options.LockTimeoutSeconds),
                        new XAttribute("incrementalModify", config.Options.IncrementalModify ? "true" : "false"))));

            var directory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false)
            };

            using (var writer = XmlWriter.Create(configPath, settings))
            {
                document.Save(writer);
            }
        }

        private static string Attr(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? null : attribute.Value;
        }

        private static bool BoolAttr(XElement element, string name, bool fallback)
        {
            var value = Attr(element, name);
            bool parsed;
            return value != null && bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int IntAttr(XElement element, string name, int fallback)
        {
            var value = Attr(element, name);
            int parsed;
            return value != null && int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static void Normalize(DbChronicleConfig config)
        {
            if (config.Component == null)
            {
                config.Component = new ComponentSettings();
            }
            if (config.Database == null)
            {
                config.Database = new DatabaseSettings();
            }
            if (config.Paths == null)
            {
                config.Paths = new PathSettings();
            }
            if (config.Baseline == null)
            {
                config.Baseline = new BaselineSettings();
            }
            if (config.Options == null)
            {
                config.Options = new OptionSettings();
            }
            if (string.IsNullOrEmpty(config.Environment))
            {
                config.Environment = "dev";
            }
            if (string.IsNullOrEmpty(config.Database.Dialect))
            {
                config.Database.Dialect = "mysql";
            }
            if (config.Options.LockTimeoutSeconds < 0)
            {
                config.Options.LockTimeoutSeconds = 0;
            }
            if (config.Options.LockTimeoutSeconds > 300)
            {
                config.Options.LockTimeoutSeconds = 300;
            }
        }
    }
}
