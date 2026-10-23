using System;
using System.Text.RegularExpressions;

namespace DbChronicle.Demo.Infrastructure
{
    internal static class MySqlConnectionStrings
    {
        public const string EnvironmentVariable = "DBCHRONICLE_DEMO_CONN";

        // 仅用于本地演示；生产请通过环境变量或密钥管理注入，不要写进仓库
        public const string Default =
            "Server=127.0.0.1;Port=3806;Database=snibemiddlewaredb;User ID=root;Password=sa;AllowPublicKeyRetrieval=true;SslMode=None;";

        public static string Resolve()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrEmpty(fromEnvironment) ? Default : fromEnvironment;
        }

        public static string WithDatabase(string connectionString, string database)
        {
            if (Regex.IsMatch(connectionString, "Database=", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(connectionString, "Database=[^;]*", "Database=" + database, RegexOptions.IgnoreCase);
            }
            return connectionString.TrimEnd(';') + ";Database=" + database + ";";
        }

        public static string WithoutDatabase(string connectionString)
        {
            return Regex.Replace(connectionString, "Database=[^;]*;?", "", RegexOptions.IgnoreCase);
        }
    }
}
