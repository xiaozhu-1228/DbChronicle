using System;
using DbChronicle.Configuration;

namespace DbChronicle.Cli
{
    public static class ConnectionResolver
    {
        public const string EnvironmentVariable = "DBCHRONICLE_CONN";

        public static string Resolve(DbChronicleConfig config)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return fromEnvironment;
            }

            if (!Console.IsInputRedirected)
            {
                return Prompt(config);
            }

            throw new InvalidOperationException("非交互环境未找到连接信息。请设置 " + EnvironmentVariable + " 环境变量。");
        }

        private static string Prompt(DbChronicleConfig config)
        {
            var dialect = (config.Database.Dialect ?? "mysql").ToLowerInvariant();
            Console.WriteLine("数据库类型：" + dialect);
            Console.WriteLine("请输入连接信息：");

            if (dialect == "sqlite")
            {
                var file = ReadValue("  数据库文件路径 (dbchronicle.db): ", "dbchronicle.db");
                return "Data Source=" + file;
            }

            var defaultPort = dialect == "postgresql" ? "5432" : (dialect == "sqlserver" ? "1433" : "3306");
            var defaultUser = dialect == "postgresql" ? "postgres" : (dialect == "sqlserver" ? "sa" : "root");

            var host = ReadValue("  主机 (localhost): ", "localhost");
            var port = ReadValue("  端口 (" + defaultPort + "): ", defaultPort);
            var database = ReadRequired("  数据库名: ");
            var user = ReadValue("  用户名 (" + defaultUser + "): ", defaultUser);
            var password = ReadPassword("  密码: ");

            if (dialect == "postgresql")
            {
                return "Host=" + host + ";Port=" + port + ";Database=" + database + ";Username=" + user + ";Password=" + password;
            }

            if (dialect == "sqlserver")
            {
                return "Server=" + host + "," + port + ";Database=" + database + ";User Id=" + user
                    + ";Password=" + password + ";TrustServerCertificate=True";
            }

            return "Server=" + host + ";Port=" + port + ";Database=" + database + ";User ID=" + user + ";Password=" + password;
        }

        private static string ReadValue(string prompt, string fallback)
        {
            Console.Write(prompt);
            var value = Console.ReadLine();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string ReadRequired(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                var value = Console.ReadLine();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var buffer = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                    }
                    continue;
                }
                buffer.Append(key.KeyChar);
            }
            return buffer.ToString();
        }
    }
}
