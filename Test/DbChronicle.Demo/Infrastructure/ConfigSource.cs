using System;
using System.IO;
using DbChronicle.Configuration;
using DbChronicle.Demo.Cli;

namespace DbChronicle.Demo.Infrastructure
{
    /// <summary>
    /// 配置来源：--config 指定文件时直接加载；否则按场景在临时工作区生成默认配置。
    /// </summary>
    internal static class ConfigSource
    {
        public static DbChronicleConfig Resolve(
            DemoOptions options,
            DemoWorkspace workspace,
            ConsoleReporter console,
            string defaultDialect,
            string defaultVersion,
            string defaultEnvironment,
            string logicModelsScenario)
        {
            if (!string.IsNullOrEmpty(options.ConfigPath))
            {
                if (!File.Exists(options.ConfigPath))
                {
                    throw new FileNotFoundException("配置文件不存在：" + options.ConfigPath, options.ConfigPath);
                }

                var loaded = ConfigLoader.Load(options.ConfigPath);
                console.Line("配置：" + loaded.ConfigPath + "（--config）");
                console.Line("方言：" + loaded.Database.Dialect + " " + loaded.Database.Version);
                console.Line("逻辑模型目录：" + loaded.ResolveMigrationsPath());

                if (!IsCompatible(loaded.Database.Dialect, defaultDialect))
                {
                    console.Line("warning: 配置方言（" + loaded.Database.Dialect
                        + "）与场景（" + defaultDialect + "）不一致，按配置执行");
                }

                return loaded;
            }

            var bundledPath = DemoWorkspace.BundledConfig(logicModelsScenario);
            if (File.Exists(bundledPath))
            {
                var bundled = ConfigLoader.Load(bundledPath);
                console.Line("配置：" + bundled.ConfigPath + "（Demo 自带）");
                console.Line("方言：" + bundled.Database.Dialect + " " + bundled.Database.Version);
                console.Line("逻辑模型目录：" + bundled.ResolveMigrationsPath());
                return bundled;
            }

            var configPath = workspace.File(ConfigLoader.DefaultFileName);
            var config = ConfigLoader.CreateDefault(configPath);
            config.Database.Dialect = defaultDialect;
            config.Database.Version = defaultVersion;
            config.Environment = defaultEnvironment;
            config.Paths.Migrations = DemoWorkspace.LogicModelsDirectory(logicModelsScenario);
            ConfigLoader.Save(config, configPath);

            config = ConfigLoader.Load(configPath);
            console.Line("配置：" + configPath + "（默认生成）");
            console.Line("逻辑模型目录：" + config.ResolveMigrationsPath());
            return config;
        }

        private static bool IsCompatible(string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var a = (actual ?? "").ToLowerInvariant();
            var b = (expected ?? "").ToLowerInvariant();
            return (a == "mysql" && b == "mariadb") || (a == "mariadb" && b == "mysql");
        }
    }
}
