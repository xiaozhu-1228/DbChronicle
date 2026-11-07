using System.Collections.Generic;
using System;

namespace DbChronicle.Compilation.Dialects
{
    public static class DialectFactory
    {
        public static IReadOnlyList<string> SupportedDialects => new string[5] { "mysql", "mariadb", "postgresql", "sqlserver", "sqlite" };

        public static ISqlDialect Create(string dialectName, string version, DialectContext context)
        {
            return Normalize(dialectName) switch
            {
                "mysql" => new MySqlDialect(DefaultIfEmpty(version, "8.0"), context),
                "mariadb" => new MariaDbDialect(DefaultIfEmpty(version, "10.5"), context),
                "postgresql" => new PostgreSqlDialect(DefaultIfEmpty(version, "14"), context),
                "sqlserver" => new SqlServerDialect(DefaultIfEmpty(version, "2019"), context),
                "sqlite" => new SqliteDialect(DefaultIfEmpty(version, "3.35"), context),
                _ => throw new NotSupportedException("暂不支持该方言：" + dialectName),
            };
        }

        public static bool IsSupported(string dialectName)
        {
            string text = Normalize(dialectName);
            return text == "mysql"
                || text == "mariadb"
                || text == "postgresql"
                || text == "sqlserver"
                || text == "sqlite";
        }

        public static string Normalize(string dialectName)
        {
            var text = (dialectName ?? "").Trim().ToLowerInvariant();
            switch (text)
            {
                case "postgres":
                case "pg":
                case "pgsql":
                    return "postgresql";
                case "mssql":
                    return "sqlserver";
                default:
                    return text;
            }
        }

        private static string DefaultIfEmpty(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }

}
