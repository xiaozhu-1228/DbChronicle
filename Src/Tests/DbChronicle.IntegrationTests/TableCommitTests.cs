using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Executor.AdoNet;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.State;
using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    public sealed class TableCommitTests
    {
        [Fact]
        public void TableScope_ShouldCommitDoneTablesAndResumeFromFailedOne()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);
            config.Options.CommitScope = "table";

            TestSupport.WriteUnit(directory, "20261023_100000_aa0001",
                "  <table name=\"t1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"t2\">THIS IS NOT VALID SQL</rawSql>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "table-commit.db");
            var logs = new List<string>();

            var first = Apply(config, units, dbPath, logs);
            Assert.False(first.Success);
            Assert.Contains("t2", first.Message);
            Assert.Contains("已完成 1", first.Message);
            Assert.Contains(logs, x => x.Contains("  SQL：") && x.Contains("\"*\""));
            Assert.Contains(logs, x => x.Contains("表名以 * 表示"));

            using (var executor = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.True(TableExists(executor, "t1"));
                Assert.False(TableExists(executor, "t2"));
            }

            var store = new VersionStore(config);
            var appliedTables = store.LoadAppliedTables(units[0].Id);
            Assert.Contains("t1", appliedTables, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("t2", appliedTables, StringComparer.OrdinalIgnoreCase);
            Assert.True(store.LoadAll().Single(x => x.ChangeId == units[0].Id).HasFailure);

            logs.Clear();
            var second = Apply(config, units, dbPath, logs);
            Assert.False(second.Success);
            Assert.Contains("t2", second.Message);
        }

        [Fact]
        public void BatchScope_ShouldCommitPreviousBatchOnly()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);
            config.Options.CommitScope = "batch";
            config.Options.CommitBatchSize = 2;

            TestSupport.WriteUnit(directory, "20261023_110000_bb0002",
                "  <table name=\"b1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"b2\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"b3\">NOT VALID SQL</rawSql>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "batch-commit.db");

            var result = Apply(config, units, dbPath, new List<string>());
            Assert.False(result.Success);

            using (var executor = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.True(TableExists(executor, "b1"));
                Assert.True(TableExists(executor, "b2"));
                Assert.False(TableExists(executor, "b3"));
            }

            var tables = new VersionStore(config).LoadAppliedTables(units[0].Id);
            Assert.Contains("b1", tables, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("b2", tables, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("b3", tables, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void UnitScope_ShouldKeepWholeUnitAtomic()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);
            config.Options.CommitScope = "unit";

            TestSupport.WriteUnit(directory, "20261023_120000_cc0003",
                "  <table name=\"u1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"u2\">NOT VALID SQL</rawSql>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "unit-commit.db");

            var result = Apply(config, units, dbPath, new List<string>());
            Assert.False(result.Success);

            using (var executor = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.False(TableExists(executor, "u1"));
            }
        }

        [Fact]
        public void NoTransactionDialect_ShouldContinueByDefault()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);

            TestSupport.WriteUnit(directory, "20261023_150000_ab0007",
                "  <table name=\"e1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"e2\">NOT VALID SQL</rawSql>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "no-transaction.db");

            var factory = new AdoNetExecutorFactory("sqlite");
            var executor = new SchemaExecutor(config, new NoTransactionExecutorFactory(factory), factory);
            var result = executor.Apply(units, new ApplyOptions
            {
                ConnectionString = "Data Source=" + dbPath,
                AppliedBy = "tester",
                LockTimeoutSeconds = 5
            });

            Assert.False(result.Success);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Failures, x => x.Contains("e2"));
            using (var db = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.True(TableExists(db, "e1"));
            }
        }

        [Fact]
        public void ContinueOnError_ShouldKeepGoingAfterFailedTable()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);
            config.Options.CommitScope = "table";

            TestSupport.WriteUnit(directory, "20261023_130000_dd0004",
                "  <table name=\"c1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"c2\">NOT VALID SQL</rawSql>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "continue-table.db");
            var logs = new List<string>();

            var result = Apply(config, units, dbPath, logs, continueOnError: true);

            Assert.False(result.Success);
            Assert.Equal(1, result.FailedCount);
            Assert.Contains(result.Failures, x => x.Contains("c2"));
            Assert.Contains(logs, x => x.Contains("完成：共 2 张表，执行 1，跳过 0，失败 1"));
            Assert.Contains(logs, x => x.Contains("表 c2："));
            Assert.Equal(1, logs.Count(x => x.Contains("表 c2：")));
            using (var executor = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.True(TableExists(executor, "c1"));
            }
        }

        [Fact]
        public void ContinueOnError_ShouldContinueNextUnit()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var config = CreateConfig(directory);
            config.Options.CommitScope = "table";

            TestSupport.WriteUnit(directory, "20261023_140000_ee0005",
                "  <rawSql name=\"d1\">NOT VALID SQL</rawSql>");

            TestSupport.WriteUnit(directory, "20261023_140100_ff0006",
                "  <table name=\"d2\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");

            var units = new ChangeUnitLoader(config).LoadAll();
            var dbPath = Path.Combine(directory, "continue-unit.db");

            var result = Apply(config, units, dbPath, new List<string>(), continueOnError: true);

            Assert.False(result.Success);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(1, result.AppliedCount);
            using (var executor = new SqliteExecutor("Data Source=" + dbPath))
            {
                Assert.True(TableExists(executor, "d2"));
            }
        }

        private static ApplyResult Apply(DbChronicleConfig config, IReadOnlyList<ChangeUnit> units,
            string databasePath, List<string> logs, bool continueOnError = false)
        {
            var factory = new AdoNetExecutorFactory("sqlite");
            var executor = new SchemaExecutor(config, factory, factory);
            return executor.Apply(units, new ApplyOptions
            {
                ConnectionString = "Data Source=" + databasePath,
                AppliedBy = "tester",
                LockTimeoutSeconds = 5,
                ContinueOnError = continueOnError,
                Log = logs == null ? (Action<string>)null : logs.Add
            });
        }

        private static bool TableExists(IDbExecutor executor, string table)
        {
            return executor.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = '" + table + "'").Count > 0;
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
            return config;
        }
    }
}