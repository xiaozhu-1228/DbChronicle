using System.Globalization;
using System;

namespace DbChronicle.Demo.OperationTests
{
    internal sealed class OpsTestOptions
    {
        public string Dialect { get; private set; }

        public string Only { get; private set; }

        public bool KeepObjects { get; private set; }

        public string Database { get; private set; }

        public static OpsTestOptions Parse(string[] args, string fallbackDialect)
        {
            var options = new OpsTestOptions
            {
                Dialect = (fallbackDialect ?? "").Trim().ToLowerInvariant()
            };

            foreach (var arg in args)
            {
                var value = arg ?? "";
                if (value.StartsWith("--dialect=", StringComparison.OrdinalIgnoreCase))
                {
                    options.Dialect = value.Substring("--dialect=".Length).Trim().ToLowerInvariant();
                }
                else if (value.StartsWith("--only=", StringComparison.OrdinalIgnoreCase))
                {
                    options.Only = value.Substring("--only=".Length).Trim();
                }
                else if (value.StartsWith("--database=", StringComparison.OrdinalIgnoreCase))
                {
                    options.Database = value.Substring("--database=".Length).Trim();
                }
                else if (string.Equals(value, "--keep", StringComparison.OrdinalIgnoreCase))
                {
                    options.KeepObjects = true;
                }

            }

            if (options.Dialect == "pgsql" || options.Dialect == "postgres")
            {
                options.Dialect = "postgresql";
            }

            if (options.Dialect == "mariadb")
            {
                options.Dialect = "mysql";
            }

            return options;
        }

        public bool Matches(string caseId)
        {
            if (string.IsNullOrEmpty(Only))
            {
                return true;
            }

            foreach (var part in Only.Split(','))
            {
                if (string.Equals(part.Trim(), caseId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string DefaultDatabaseName(string dialect)
        {
            switch (dialect)
            {
                case "postgresql":
                    return "dbchronicle_ops_pg";

                case "sqlite":
                    return "dbchronicle_ops";

                default:
                    return "dbchronicle_ops_mysql";
            }
        }

        public static string NormalizeCaseId(string value)
        {
            return (value ?? "").Trim().ToUpper(CultureInfo.InvariantCulture);
        }
    }
}
