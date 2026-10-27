using System.Collections.Generic;
using System.IO;
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
    public class ConsistencyTests
    {
        private const string FailedUnitId = "20261023_100000_d4e5f6";

        [Fact]
        public void Apply_ShouldNotRecordApplied_WhenTransactionalUnitFails()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            TestSupport.WriteUnit(directory, FailedUnitId,
                "  <table name=\"t_missing\" action=\"ALTER\">\n"
                + "    <column name=\"c1\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");

            var first = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath);
            Assert.False(first.Success);

            var records = new VersionStore(config).LoadAll();
            Assert.DoesNotContain(records, x => x.ChangeId == FailedUnitId);

            var second = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath);
            Assert.False(second.Success);
            Assert.Equal(0, second.AppliedCount);
        }

        [Fact]
        public void Apply_ShouldReject_WhenAppliedChangeUnitFileMissing()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            var unitPath = TestSupport.WriteUnit(directory, "20261023_110000_c1d2e3",
                "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"c2\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");

            var first = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath);
            Assert.True(first.Success, first.Message);

            File.Delete(unitPath);

            var second = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath);
            Assert.False(second.Success);
            Assert.Contains("文件丢失", second.Message);
        }

        [Fact]
        public void Resume_ShouldKeepStatementProgress_WhenSecondRunFailsAgain()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            TestSupport.WriteUnit(directory, "20261023_120000_e7f8a9",
                "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"c3\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"t1\">INSERT INTO t_missing VALUES (1);</rawSql>");

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");

            var first = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath, noTransaction: true);
            Assert.False(first.Success);

            var store = new VersionStore(config);
            var afterFirst = store.LoadStatements("20261023_120000_e7f8a9");
            Assert.True(afterFirst.ContainsKey(0));

            var second = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath, noTransaction: true);
            Assert.False(second.Success);

            var afterSecond = store.LoadStatements("20261023_120000_e7f8a9");
            Assert.True(afterSecond.ContainsKey(0));
        }

        private static ApplyResult Apply(DbChronicleConfig config, IReadOnlyList<ChangeUnit> units, string dbPath, bool noTransaction = false)
        {
            var factory = new AdoNetExecutorFactory("sqlite");
            IDbExecutorFactory executorFactory = noTransaction
                ? (IDbExecutorFactory)new NoTransactionExecutorFactory(factory)
                : factory;

            var executor = new SchemaExecutor(config, executorFactory, factory);
            return executor.Apply(units, new ApplyOptions
            {
                ConnectionString = "Data Source=" + dbPath,
                AppliedBy = "tester",
                LockTimeoutSeconds = 5
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

        private static void WriteBaseline(string directory)
        {
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t1\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");
        }
    }
}