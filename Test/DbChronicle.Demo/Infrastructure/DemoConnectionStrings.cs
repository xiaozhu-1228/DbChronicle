using System;

namespace DbChronicle.Demo.Infrastructure
{
    /// <summary>
    /// 连接串：默认用本地业务库，可用环境变量覆盖；方言不在这里决定（由 DbchronicleConfig.xml 决定）。
    /// </summary>
    internal static class DemoConnectionStrings
    {
        public const string MySqlEnvironmentVariable = "DBCHRONICLE_DEMO_MYSQL";
        public const string PostgreSqlEnvironmentVariable = "DBCHRONICLE_DEMO_PG";

        // 仅用于本地演示；生产请通过环境变量或密钥管理注入
        private const string DefaultMySql =
            "Server=127.0.0.1;Port=3806;Database=snibemiddlewaredb;User ID=root;Password=sa;AllowPublicKeyRetrieval=true;SslMode=None;";

        private const string DefaultPostgreSql =
            "Host=127.0.0.1;Port=5432;Database=snibemiddlewaredb;Username=postgres;Password=sa;Search Path=public;Timeout=5;";

        public static string ResolveMySql()
        {
            return FirstNotEmpty(
                Environment.GetEnvironmentVariable(MySqlEnvironmentVariable),
                DefaultMySql);
        }

        public static string ResolvePostgreSql()
        {
            return FirstNotEmpty(
                Environment.GetEnvironmentVariable(PostgreSqlEnvironmentVariable),
                DefaultPostgreSql);
        }

        private static string FirstNotEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }
    }
}
