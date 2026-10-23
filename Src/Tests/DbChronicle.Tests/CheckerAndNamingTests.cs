using System.IO;
using DbChronicle.Compilation.Checking;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Naming;
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
            Assert.Contains(diagnostics, x => x.Code == "E100");
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
        public void Slugify_ShouldProduceSnakeCase()
        {
            Assert.Equal("add_phone_to_nor_user", ChangeUnitFactory.Slugify("add phone to nor_user"));
            Assert.Equal("change", ChangeUnitFactory.Slugify("   "));
        }

        [Fact]
        public void Factory_ShouldCreateFileWithStandardName()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var path = ChangeUnitFactory.Create(directory, "add phone", "20261022", "tester");
            var name = Path.GetFileName(path);

            Assert.Matches(@"^\d{8}_\d{6}_[0-9a-f]{6}_add_phone\.xml$", name);
            Assert.Contains("add phone", File.ReadAllText(path));
        }
    }
}
