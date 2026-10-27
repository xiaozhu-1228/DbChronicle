using System.IO;
using System;

using DbChronicle.Configuration;

namespace DbChronicle.Demo.Infrastructure
{
    internal static class DemoConfig
    {
        /// <summary>
        /// 加载 Demo 配置。方言完全取自配置文件的 &lt;database dialect="..."&gt;（支持 pgsql/postgres 别名），
        /// 仅把状态目录按方言隔离（versionLog 里的 ${environment}）。
        /// </summary>
        public static DbChronicleConfig Load(ConsoleReporter console)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", ConfigLoader.DefaultFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("配置文件不存在：" + path, path);
            }

            var config = ConfigLoader.Load(path);
            config.Database.Dialect = NormalizeDialect(config.Database.Dialect);
            config.Environment = config.Database.Dialect == "postgresql" ? "pgsql" : config.Database.Dialect;

            console.Line("配置：" + path);
            console.Line("方言：" + config.Database.Dialect + " " + config.Database.Version);
            return config;
        }

        private static string NormalizeDialect(string dialect)
        {
            switch ((dialect ?? "").Trim().ToLowerInvariant())
            {
                case "pgsql":
                case "postgres":
                case "postgresql":
                    return "postgresql";

                case "mariadb":
                case "mysql":
                    return "mysql";

                default:
                    return (dialect ?? "").Trim();
            }
        }
    }
}