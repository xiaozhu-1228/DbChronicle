using System;
using System.Collections.Generic;
using System.IO;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Domain.Model;
using DbChronicle.Parsing;
using DbChronicle.Executor.AdoNet;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    public class WildcardIntegrationTests
    {
        [Fact]
        public void Apply_Wildcard_ShouldAddColumnToAllTrackedTables()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t2\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");
            TestSupport.WriteUnit(directory, "20261022_100000_ab12cd",
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = CreateConfig(directory);
            var databasePath = Path.Combine(directory, "wild.db");
            var units = new ChangeUnitLoader(config).LoadAll();

            var result = Apply(config, units, databasePath);

            Assert.True(result.Success, result.Message);
            Assert.True(ColumnExists(databasePath, "t1", "created_by"));
            Assert.True(ColumnExists(databasePath, "t2", "created_by"));
        }

        [Fact]
        public void Apply_Wildcard_ShouldIncludeTablesOutsideModel()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"tracked\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");
            TestSupport.WriteUnit(directory, "20261022_100000_ab12cd",
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");

            var databasePath = Path.Combine(directory, "wild-warn.db");
            CreateExtraTable(databasePath, "legacy_log");

            var config = CreateConfig(directory);
            var logs = new List<string>();
            var result = Apply(config, new ChangeUnitLoader(config).LoadAll(), databasePath, logs);

            Assert.True(result.Success, result.Message);
            Assert.Contains(logs, x => x.Contains("legacy_log"));
            Assert.True(ColumnExists(databasePath, "legacy_log", "created_by"));
        }

        [Fact]
        public void Apply_Wildcard_ShouldNotTouchVersionTable()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"base_table\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");
            TestSupport.WriteUnit(directory, "20261022_100000_ab12cd",
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = CreateConfig(directory);
            var databasePath = Path.Combine(directory, "wild-scope.db");
            var result = Apply(config, new ChangeUnitLoader(config).LoadAll(), databasePath);

            Assert.True(result.Success, result.Message);
            Assert.True(ColumnExists(databasePath, "base_table", "created_by"));
            Assert.False(ColumnExists(databasePath, "dbchronicle_version_log", "created_by"));
        }

        private static ApplyResult Apply(DbChronicleConfig config, IReadOnlyList<ChangeUnit> units,
            string databasePath, List<string> logs = null)
        {
            var factory = new AdoNetExecutorFactory("sqlite");
            var executor = new SchemaExecutor(config, factory, factory);

            return executor.Apply(units, new ApplyOptions
            {
                ConnectionString = "Data Source=" + databasePath,
                AppliedBy = "tester",
                LockTimeoutSeconds = 5,
                Log = logs == null ? (Action<string>)null : logs.Add
            });
        }

        private static DbChronicleConfig CreateConfig(string directory)
        {
            var config = new DbChronicleConfig { BaseDirectory = directory };
            config.Database.Dialect = "sqlite";
            config.Database.Version = "3.35";
            config.Environment = "test";
            config.Paths.Migrations = ".";
            config.Paths.VersionLog = "./version_log/${environment}";
            config.Paths.Output = "./output";
            config.Options.IncrementalModify = true;
            config.Options.AllowDestructive = true;
            return config;
        }

        private static void CreateExtraTable(string databasePath, string table)
        {
            using (var connection = new SqliteConnection("Data Source=" + databasePath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE IF NOT EXISTS \"" + table + "\" (\"id\" INTEGER PRIMARY KEY AUTOINCREMENT)";
                    command.ExecuteNonQuery();
                }
            }
        }

        private static bool ColumnExists(string databasePath, string table, string column)
        {
            using (var connection = new SqliteConnection("Data Source=" + databasePath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('" + table + "')";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (string.Equals(Convert.ToString(reader["name"]), column, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
