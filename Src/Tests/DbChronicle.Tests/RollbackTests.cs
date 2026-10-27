using System.IO;
using System.Linq;

using DbChronicle.Compilation.Dialects;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    public class RollbackTests
    {
        [Fact]
        public void Plan_ShouldReverseAddColumn()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                + "  </table>");

            var plan = BuildPlan(directory, "00000000");

            Assert.False(plan.HasBlockers);
            Assert.Single(plan.Steps);
            Assert.Contains(plan.Steps[0].Statements, x => x.Text.Contains("DROP COLUMN `phone`"));
        }

        [Fact]
        public void Plan_ShouldRestorePreviousDefinition_WhenModifyColumn()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"name\" action=\"MODIFY\" length=\"128\"/>\n"
                + "  </table>");

            var plan = BuildPlan(directory, "00000000");

            Assert.False(plan.HasBlockers);
            var sql = plan.Steps[0].Statements[0].Text;
            Assert.Contains("VARCHAR(64)", sql);
            Assert.Contains("NOT NULL", sql);
        }

        [Fact]
        public void Plan_ShouldBlock_WhenColumnWasDropped()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            WriteBaseline(directory);
            TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"name\" action=\"DROP\"/>\n"
                + "  </table>");

            var plan = BuildPlan(directory, "00000000");

            Assert.True(plan.HasBlockers);
            Assert.Contains(plan.Steps[0].Blockers, x => x.Contains("无法自动回滚"));
        }

        [Fact]
        public void Plan_ShouldReverseCreateTable()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>");

            var plan = BuildPlan(directory, "00000000");

            Assert.False(plan.HasBlockers);
            Assert.Contains(plan.Steps[0].Statements, x => x.Text.Contains("DROP TABLE"));
        }

        private static RollbackPlan BuildPlan(string directory, string targetRelease)
        {
            var config = TestSupport.CreateConfig(true);
            config.Paths.Migrations = ".";
            config.BaseDirectory = directory;

            var parser = new ChangeUnitParser(config);
            var units = Directory.GetFiles(directory, "*.xml")
                .Select(path => parser.Parse(path))
                .OrderBy(x => x.Release, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dialect = DialectFactory.Create("mysql", "8.0", new DialectContext());
            return new RollbackPlanner().Build(units, targetRelease, dialect);
        }

        private static void WriteBaseline(string directory)
        {
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"false\"/>\n"
                + "  </table>");
        }
    }
}