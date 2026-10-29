using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Migrations.State;
using DbChronicle.Migrations.Templates;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Scanner;

namespace DbChronicle.Cli
{
    public static partial class Commands
    {
        /// <summary>插件宿主（由 Program 注入；未注入时按默认目录惰性创建）。</summary>
        internal static PluginHost Plugins { get; set; }

        private static PluginHost Host
        {
            get { return Plugins ?? (Plugins = new PluginHost()); }
        }

        public static int Init(CliArguments args)
        {
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (File.Exists(path))
            {
                Console.WriteLine("配置已存在：" + path);
                return ExitCodes.Success;
            }

            var config = ConfigLoader.CreateDefault(path);

            var dialect = args.Get("dialect", null);
            if (!string.IsNullOrEmpty(dialect))
            {
                if (!DialectFactory.IsSupported(dialect))
                {
                    throw new CliFailureException(ExitCodes.Usage,
                        "不支持的方言：" + dialect + "（可选：" + string.Join(" / ", DialectFactory.SupportedDialects) + "）");
                }
                config.Database.Dialect = dialect.Trim().ToLowerInvariant();
                config.Database.Version = DefaultVersionOf(config.Database.Dialect);
            }

            var version = args.Get("db-version", null) ?? args.Get("version", null);
            if (!string.IsNullOrEmpty(version))
            {
                config.Database.Version = version;
            }

            ConfigLoader.Save(config, path);
            Directory.CreateDirectory(config.ResolveMigrationsPath());
            Directory.CreateDirectory(config.ResolveVersionLogPath());
            Directory.CreateDirectory(config.ResolveOutputPath());

            Console.WriteLine("已生成配置：" + path);
            Console.WriteLine("  方言：" + config.Database.Dialect + " " + config.Database.Version);
            Console.WriteLine("  migrations：" + config.ResolveMigrationsPath());
            Console.WriteLine("  version_log：" + config.ResolveVersionLogPath());
            Console.WriteLine("  output：" + config.ResolveOutputPath());
            return ExitCodes.Success;
        }

        public static int New(CliArguments args)
        {
            var config = LoadConfig(args);
            var description = args.Get("description", null) ?? args.Get("desc", null);
            if (string.IsNullOrEmpty(description))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --description 参数");
            }

            var path = ChangeUnitFactory.Create(config.ResolveMigrationsPath(), description,
                args.Get("release", null), args.Get("author", null));
            Console.WriteLine(path);
            return ExitCodes.Success;
        }

        private static string DefaultVersionOf(string dialect)
        {
            switch (dialect)
            {
                case "mariadb":
                    return "10.5";
                case "postgresql":
                    return "14";
                case "sqlserver":
                    return "2019";
                case "sqlite":
                    return "3.35";
                default:
                    return "8.0";
            }
        }

        private static List<AppliedChange> LoadApplied(DbChronicleConfig config)
        {
            try
            {
                return new VersionStore(config).LoadAll();
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "读取状态文件失败：" + ex.Message);
            }
        }

        private static string ResolveConnection(DbChronicleConfig config)
        {
            try
            {
                return ConnectionResolver.Resolve(config);
            }
            catch (InvalidOperationException ex)
            {
                throw new CliFailureException(ExitCodes.Environment, ex.Message);
            }
        }

        /// <summary>供插件命令上下文使用（与内建命令同一套配置解析规则）。</summary>
        internal static DbChronicleConfig LoadConfigForPlugin(CliArguments args)
        {
            return LoadConfig(args);
        }

        private static DbChronicleConfig LoadConfig(CliArguments args)
        {
            var config = LoadConfigFromFile(args);
            ApplyConfigOverrides(config, args);
            return config;
        }

        /// <summary>命令行覆盖配置里的方言与数据库版本（一份配置可服务多种库）。</summary>
        private static void ApplyConfigOverrides(DbChronicleConfig config, CliArguments args)
        {
            var dialect = args.Get("dialect", null);
            if (!string.IsNullOrEmpty(dialect))
            {
                var normalized = dialect.Trim().ToLowerInvariant();
                if (!DialectFactory.IsSupported(normalized))
                {
                    throw new CliFailureException(ExitCodes.Usage,
                        "不支持的方言：" + dialect + "（可选：" + string.Join(" / ", DialectFactory.SupportedDialects) + "）");
                }
                config.Database.Dialect = normalized;
            }

            var version = args.Get("db-version", null);
            if (!string.IsNullOrEmpty(version))
            {
                config.Database.Version = version.Trim();
            }
        }

        private static DbChronicleConfig LoadConfigFromFile(CliArguments args)
        {
            var explicitPath = args.Has("config");
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (File.Exists(path))
            {
                return ConfigLoader.Load(path);
            }

            if (explicitPath)
            {
                throw new CliFailureException(ExitCodes.Usage,
                    "配置文件不存在：" + Path.GetFullPath(path) + "（先执行 db-chronicle init）");
            }

            var created = ConfigLoader.CreateDefault(path);
            ConfigLoader.Save(created, path);
            Console.WriteLine("未找到配置文件，已生成默认配置：" + created.ConfigPath + "（可用 db-chronicle init --dialect <name> 调整方言）");
            return created;
        }

        /// <summary>输出位置：新参数（--output-dir / --output-file）优先，兼容旧的 --out。</summary>
        private static string ResolveOutputPath(CliArguments args, string name, string fallback)
        {
            var value = args.Get(name, null);
            if (string.IsNullOrEmpty(value))
            {
                value = args.Get("out", null);
            }

            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static void PrintDiagnostics(IEnumerable<CompileDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Console.Error.WriteLine(diagnostic.ToString());
                }
                else
                {
                    Console.WriteLine(diagnostic.ToString());
                }
            }
        }
    }
}