using System.IO;

using DbChronicle.Compilation.Checks;
using DbChronicle.Migrations.Templates;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    public class CheckerAndNamingTests
    {
        [Fact]
        public void Checker_ShouldReportDuplicateAdd()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var first = TestSupport.WriteUnit(directory, "20261022_100000_aaaaaa",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");
            var second = TestSupport.WriteUnit(directory, "20261022_110000_bbbbbb",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = TestSupport.CreateConfig(true);
            var parser = new ChangeUnitParser(config);
            var units = new[]
            {
                parser.Parse(first),
                parser.Parse(second)
            };

            var diagnostics = new ChangeChecker().Check(units);
            Assert.Contains(diagnostics, x => x.Code == "E101");
        }

        [Fact]
        public void Checker_ShouldReportDropOfUnknownColumn()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"nope\" action=\"DROP\"/>\n"
                + "  </table>");

            var diagnostics = new ChangeChecker().Check(new[] { unit });

            Assert.Contains(diagnostics, x => x.Message.Contains("DROP 不存在的列"));
        }

        [Fact]
        public void Checker_ShouldReportMissingDependency()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");
            unit.Depends.Add("20990101_000000_zzzzzz");

            var diagnostics = new ChangeChecker().Check(new[] { unit });
            Assert.Contains(diagnostics, x => x.Message.Contains("depends"));
        }

        [Fact]
        public void Checker_ShouldReportForeignKeyToLaterCreatedTable()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var first = TestSupport.WriteUnit(directory, "20261022_100000_aaaaaa",
                "  <table name=\"child\" action=\"ALTER\">\n"
                + "    <foreignKey name=\"fk_child_parent\" action=\"ADD\" columns=\"pid\" refTable=\"parent\" refColumns=\"id\"/>\n"
                + "  </table>");
            var second = TestSupport.WriteUnit(directory, "20261022_110000_bbbbbb",
                "  <table name=\"parent\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var units = new[]
            {
                parser.Parse(first),
                parser.Parse(second)
            };

            var diagnostics = new ChangeChecker().Check(units);
            Assert.Contains(diagnostics, x => x.Code == "E109");
        }

        [Fact]
        public void Checker_ShouldReportReferenceAfterDropTable()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var first = TestSupport.WriteUnit(directory, "20261022_100000_cccccc",
                "  <table name=\"t\" action=\"DROP\"/>\n");
            var second = TestSupport.WriteUnit(directory, "20261022_110000_dddddd",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var units = new[]
            {
                parser.Parse(first),
                parser.Parse(second)
            };

            var diagnostics = new ChangeChecker().Check(units);
            Assert.Contains(diagnostics, x => x.Code == "E109");
        }

        [Fact]
        public void Checker_ShouldWarnCrossDayRelease()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "20261022_100000_eeeeee",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var path = Path.Combine(directory, "20261023_100000_ffffff_test.xml");
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261023_100000_ffffff\" author=\"tester\" release=\"20261022\">\n"
                + "  <description>test</description>\n"
                + "  <table name=\"t2\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new System.Text.UTF8Encoding(false));

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var units = new[]
            {
                parser.Parse(Path.Combine(directory, "20261022_100000_eeeeee_test.xml")),
                parser.Parse(path)
            };

            var diagnostics = new ChangeChecker().Check(units);
            Assert.Contains(diagnostics, x => x.Code == "W013");
        }

        [Fact]
        public void Factory_ShouldCreateFileWithStandardName()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var path = ChangeUnitFactory.Create(directory, "add phone", "20261022", "tester");
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(path);

            Assert.Matches(@"^\d{8}_\d{6}\.xml$", name);
            Assert.Contains("add phone", text);
            Assert.Contains("release=\"20261022\"", text);
            Assert.Contains("id=\"" + name.Substring(0, name.Length - 4) + "\"", text);
        }

        [Fact]
        public void Factory_ShouldNeverOverwriteWhenCalledTwice()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var first = ChangeUnitFactory.Create(directory, "add phone", "20261022", "tester");
            var second = ChangeUnitFactory.Create(directory, "add phone", "20261022", "tester");

            Assert.NotEqual(first, second);
            Assert.Equal(2, Directory.GetFiles(directory, "*.xml").Length);
        }
    }
}