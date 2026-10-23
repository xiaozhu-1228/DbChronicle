using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Parsing;
using DbChronicle.Executor.AdoNet;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    public class RealDatabaseTests
    {
        [Fact]
        public void MySql_ShouldApplyAndReportChecksumChange()
        {
            Run("mysql", Environment.GetEnvironmentVariable("DBCHRONICLE_TEST_MYSQL"), "DBCHRONICLE_TEST_MYSQL");
        }

        [Fact]
        public void PostgreSql_ShouldApplyAndReportChecksumChange()
        {
            Run("postgresql", Environment.GetEnvironmentVariable("DBCHRONICLE_TEST_PG"), "DBCHRONICLE_TEST_PG");
        }

        [Fact]
        public void SqlServer_ShouldApplyAndReportChecksumChange()
        {
            Run("sqlserver", Environment.GetEnvironmentVariable("DBCHRONICLE_TEST_SQLSERVER"), "DBCHRONICLE_TEST_SQLSERVER");
        }

        [Fact]
        public void Sqlite_ShouldApplyAndReportChecksumChange()
        {
            Run("sqlite", null, "（SQLite 使用临时文件，无需配置）");
        }

        private static void Run(string dialect, string connectionString, string environmentHint)
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            if (dialect == "sqlite")
            {
                connectionString = "Data Source=" + Path.Combine(directory, "real.db");
            }
            else if (string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("跳过 " + dialect + "：未配置 " + environmentHint);
                return;
            }

            var table = "dc_test_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var config = CreateConfig(directory, dialect);
            var baselinePath = TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"" + table + "\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                + "  </table>");

            var changePath = TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"" + table + "\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                + "  </table>");

            var factory = AdoNetExecutorFactory.Resolve(dialect);
            var schemaExecutor = new SchemaExecutor(config, factory, factory);
            var options = new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = "integration-test",
                LockTimeoutSeconds = 10
            };

            try
            {
                var first = schemaExecutor.Apply(new ChangeUnitLoader(config).LoadAll(), options);
                Assert.True(first.Success, dialect + " apply 失败：" + first.Message);
                Assert.Equal(2, first.AppliedCount);

                using (var executor = factory.Create(connectionString))
                {
                    Assert.True(ColumnExists(executor, dialect, table, "id"));
                    Assert.True(ColumnExists(executor, dialect, table, "phone"));
                }

                var second = schemaExecutor.Apply(new ChangeUnitLoader(config).LoadAll(), options);
                Assert.True(second.Success, dialect + " 二次 apply 失败：" + second.Message);
                Assert.Equal(0, second.AppliedCount);
                Assert.Equal(2, second.SkippedCount);

                File.AppendAllText(changePath, "\n");
                var third = schemaExecutor.Apply(new ChangeUnitLoader(config).LoadAll(), options);
                Assert.False(third.Success);
                Assert.Contains("checksum", third.Message);

                var drift = schemaExecutor.CheckDrift(new ChangeUnitLoader(config).LoadAll(), connectionString);
                Assert.False(drift.HasDrift);
            }
            finally
            {
                try
                {
                    using (var executor = factory.Create(connectionString))
                    {
                        executor.Execute("DROP TABLE IF EXISTS " + Quote(dialect, table));
                    }
                }
                catch
                {
                }
            }
        }

        private static DbChronicleConfig CreateConfig(string directory, string dialect)
        {
            var config = new DbChronicleConfig { BaseDirectory = directory };
            config.Database.Dialect = dialect;
            config.Database.Version = null;
            config.Environment = "test";
            config.Paths.Migrations = ".";
            config.Paths.VersionLog = "./version_log/${environment}";
            config.Paths.Output = "./output";
            config.Options.IncrementalModify = true;
            config.Options.AllowDestructive = true;
            return config;
        }

        private static bool ColumnExists(IDbExecutor executor, string dialect, string table, string column)
        {
            string sql;
            switch (dialect)
            {
                case "mysql":
                case "mariadb":
                    sql = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()"
                        + " AND TABLE_NAME = '" + table + "' AND COLUMN_NAME = '" + column + "'";
                    break;

                case "postgresql":
                    sql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public'"
                        + " AND table_name = '" + table + "' AND column_name = '" + column + "'";
                    break;

                case "sqlserver":
                    sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS"
                        + " WHERE TABLE_NAME = '" + table + "' AND COLUMN_NAME = '" + column + "'";
                    break;

                default:
                    sql = "SELECT COUNT(*) FROM pragma_table_info('" + table + "') WHERE name = '" + column + "'";
                    break;
            }

            var rows = executor.Query(sql);
            var value = rows.Count == 0 ? null : rows[0].Values.FirstOrDefault();
            return value != null && Convert.ToInt32(value) > 0;
        }

        private static string Quote(string dialect, string identifier)
        {
            switch (dialect)
            {
                case "mysql":
                case "mariadb":
                    return "`" + identifier + "`";
                case "sqlserver":
                    return "[" + identifier + "]";
                default:
                    return "\"" + identifier + "\"";
            }
        }
    }
}
