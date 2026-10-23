using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Parsing;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Executor.AdoNet;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    public class PersistenceTests
    {
        [Fact]
        public void Apply_ShouldCreateTableAndRecordVersion()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            WritePhoneUnit(directory);

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");
            var units = new ChangeUnitLoader(config).LoadAll();

            var result = Apply(config, units, dbPath, false);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.AppliedCount);
            Assert.True(TableExists(dbPath, "nor_user"));
            Assert.True(ColumnExists(dbPath, "nor_user", "phone"));
            Assert.Equal(2, CountVersionRows(dbPath));
        }

        [Fact]
        public void Apply_ShouldSkipApplied_AndRejectModifiedFile()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            var phonePath = WritePhoneUnit(directory);

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");

            var first = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath, false);
            Assert.True(first.Success, first.Message);

            var second = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath, false);
            Assert.True(second.Success, second.Message);
            Assert.Equal(0, second.AppliedCount);
            Assert.Equal(2, second.SkippedCount);

            File.AppendAllText(phonePath, "\n");
            var third = Apply(config, new ChangeUnitLoader(config).LoadAll(), dbPath, false);
            Assert.False(third.Success);
            Assert.Contains("checksum", third.Message);
        }

        [Fact]
        public void Apply_ShouldSkipApplied_WhenRestartRequested()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            WriteTwoColumnUnit(directory);

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");
            var units = new ChangeUnitLoader(config).LoadAll();

            var first = Apply(config, units, dbPath, false);
            Assert.True(first.Success, first.Message);

            var second = Apply(config, units, dbPath, true);
            Assert.True(second.Success, second.Message);
            Assert.Equal(0, second.AppliedCount);
            Assert.Equal(2, second.SkippedCount);
        }

        [Fact]
        public void Apply_ShouldSkipRecordedStatements_WhenResumingHalfDoneUnit()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            WriteTwoColumnUnit(directory);

            var config = CreateConfig(directory);
            var dbPath = Path.Combine(directory, "test.db");
            var connectionString = "Data Source=" + dbPath;

            var units = new ChangeUnitLoader(config).LoadAll();
            var baseline = FindUnit(units, "00000000_000000_baseline");
            var unit = FindUnit(units, "20261022_100000_c3d4e5");

            using (var executor = new SqliteExecutor(connectionString))
            {
                var store = new VersionStore(config);
                store.EnsureTable();
                store.EnsureStatementTable();
                store.Delete(baseline.Id);
                store.Begin(baseline.Id, baseline.Release, baseline.Checksum, "sqlite", "tester", baseline.Description);

                var baselineCompiled = TestSupport.CompileWith("sqlite", baseline, false, null);
                foreach (var statement in baselineCompiled.Statements)
                {
                    executor.Execute(statement.Text);
                }

                store.MarkApplied(baseline.Id);
            }

            var compiled = TestSupport.CompileWith("sqlite",
                unit, true, new DbChronicle.Domain.Structures.StructureModelBuilder().Build(units, unit.Id));

            using (var executor = new SqliteExecutor(connectionString))
            {
                var store = new VersionStore(config);
                store.Delete(unit.Id);
                store.Begin(unit.Id, unit.Release, unit.Checksum, "sqlite", "tester", unit.Description);
                executor.Execute(compiled.Statements[0].Text);
                store.RecordStatement(unit.Id, compiled.Statements[0].Index, compiled.Statements[0].Hash);
                store.MarkFailure(unit.Id, 1);
            }

            var result = Apply(config, new[] { baseline, unit }, dbPath, false, noTransaction: true);

            Assert.True(result.Success, result.Message);
            Assert.True(ColumnExists(dbPath, "nor_user", "phone"));
            Assert.True(ColumnExists(dbPath, "nor_user", "email"));
        }

        private static ApplyResult Apply(DbChronicleConfig config, IReadOnlyList<DbChronicle.Domain.Model.ChangeUnit> units,
            string dbPath, bool restart, bool noTransaction = false)
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
                Restart = restart,
                LockTimeoutSeconds = 5,
                Log = null
            });
        }

        private static DbChronicle.Domain.Model.ChangeUnit FindUnit(IReadOnlyList<DbChronicle.Domain.Model.ChangeUnit> units, string id)
        {
            foreach (var unit in units)
            {
                if (unit.Id == id)
                {
                    return unit;
                }
            }
            throw new InvalidOperationException("未找到单元 " + id);
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
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                + "  </table>");
        }

        private static string WritePhoneUnit(string directory)
        {
            return TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                + "  </table>");
        }

        private static void WriteTwoColumnUnit(string directory)
        {
            TestSupport.WriteUnit(directory, "20261022_100000_c3d4e5",
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                + "    <column name=\"email\" action=\"ADD\" type=\"String\" length=\"128\" nullable=\"true\"/>\n"
                + "  </table>");
        }

        private static bool TableExists(string dbPath, string table)
        {
            using (var connection = new SqliteConnection("Data Source=" + dbPath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "'";
                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            }
        }

        private static bool ColumnExists(string dbPath, string table, string column)
        {
            using (var connection = new SqliteConnection("Data Source=" + dbPath))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('" + table + "')";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static int CountVersionRows(string dbPath)
        {
            // 版本状态现在存本地文件（versionLog 目录），不再写数据库表
            var statePath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".",
                "version_log", "test", VersionStore.StateFileName);

            if (!File.Exists(statePath))
            {
                return 0;
            }

            var document = System.Xml.Linq.XDocument.Load(statePath);
            return document.Root == null ? 0 : document.Root.Elements("applied").Count();
        }
    }

    internal sealed class NoTransactionExecutorFactory : IDbExecutorFactory
    {
        private readonly IDbExecutorFactory _inner;

        public NoTransactionExecutorFactory(IDbExecutorFactory inner)
        {
            _inner = inner;
        }

        public string DialectName
        {
            get { return _inner.DialectName; }
        }

        public IDbExecutor Create(string connectionString)
        {
            return new NoTransactionExecutor(_inner.Create(connectionString));
        }
    }

    internal sealed class NoTransactionExecutor : IDbExecutor
    {
        private readonly IDbExecutor _inner;

        public NoTransactionExecutor(IDbExecutor inner)
        {
            _inner = inner;
        }

        public string DialectName
        {
            get { return _inner.DialectName; }
        }

        public bool SupportsTransactionalDdl
        {
            get { return false; }
        }

        public void Execute(string sql)
        {
            _inner.Execute(sql);
        }

        public object Scalar(string sql)
        {
            return _inner.Scalar(sql);
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            return _inner.Query(sql);
        }

        public void BeginTransaction()
        {
            _inner.BeginTransaction();
        }

        public void Commit()
        {
            _inner.Commit();
        }

        public void Rollback()
        {
            _inner.Rollback();
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
