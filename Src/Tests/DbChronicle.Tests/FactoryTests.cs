using System;
using System.Collections.Generic;
using System.IO;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using Xunit;

namespace DbChronicle.Tests
{
    public class FactoryTests
    {
        [Fact]
        public void Factory_ShouldCompileOffline_WithInjectedExecutor()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");

            var configPath = Path.Combine(directory, ConfigLoader.DefaultFileName);
            var config = ConfigLoader.CreateDefault(configPath);
            config.Database.Dialect = "mysql";
            config.Paths.Migrations = ".";
            ConfigLoader.Save(config, configPath);

            using (var chronicle = DbChronicleFactory.Create(new OfflineExecutor(), o => o.ConfigPath = configPath))
            {
                var sql = chronicle.Compile();
                Assert.Contains("CREATE TABLE IF NOT EXISTS `t`", sql);
                Assert.Single(chronicle.LoadUnits());
                Assert.DoesNotContain(chronicle.Check(), x => x.Severity == DiagnosticSeverity.Error);
            }
        }

        [Fact]
        public void Config_ShouldResolveAbsoluteMigrationsPath()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>");

            var configPath = Path.Combine(directory, ConfigLoader.DefaultFileName);
            var config = ConfigLoader.CreateDefault(configPath);
            config.Database.Dialect = "mysql";
            config.Paths.Migrations = directory;
            ConfigLoader.Save(config, configPath);

            using (var chronicle = DbChronicleFactory.Create(new OfflineExecutor(), o => o.ConfigPath = configPath))
            {
                Assert.Equal(Path.GetFullPath(directory), Path.GetFullPath(chronicle.Config.ResolveMigrationsPath()));
                Assert.Single(chronicle.LoadUnits());
            }
        }

        [Fact]
        public void Factory_ShouldRejectNullExecutor()
        {
            Assert.Throws<ArgumentNullException>(() => DbChronicleFactory.Create(null, o => { }));
        }

        private sealed class OfflineExecutor : IDbExecutor
        {
            public string DialectName
            {
                get { return "mysql"; }
            }

            public bool SupportsTransactionalDdl
            {
                get { return false; }
            }

            public void Execute(string sql)
            {
            }

            public object Scalar(string sql)
            {
                return null;
            }

            public List<Dictionary<string, object>> Query(string sql)
            {
                return new List<Dictionary<string, object>>();
            }

            public void BeginTransaction()
            {
            }

            public void Commit()
            {
            }

            public void Rollback()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
