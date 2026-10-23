using System.Linq;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using Xunit;

namespace DbChronicle.Tests
{
    public class ParserTests
    {
        [Fact]
        public void Parse_ShouldReadColumns_AndPrimaryKey()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"CREATE\" comment=\"用户表\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"false\"/>\n"
                + "  </table>");

            Assert.False(TestSupport.HasError(unit), string.Join(" | ", unit.Diagnostics.Select(x => x.ToString())));
            Assert.Single(unit.Operations);

            var create = (DbChronicle.Domain.Operations.CreateTableOperation)unit.Operations[0];
            Assert.Equal(2, create.Columns.Count);
            Assert.Single(create.PrimaryKeyColumns);
            Assert.Equal("id", create.PrimaryKeyColumns[0]);
            Assert.Equal(DbChronicle.Domain.Operations.LogicalType.Int64, create.Columns[0].Type);
        }

        [Fact]
        public void Parse_ShouldFail_WhenStringLengthMissing()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" nullable=\"true\"/>\n"
                + "  </table>");

            Assert.True(TestSupport.HasError(unit));
            Assert.Contains(unit.Diagnostics, x => x.Code == "E018");
        }

        [Fact]
        public void Parse_ShouldFail_WhenRenameWithoutRenamedFrom()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"b\" action=\"RENAME\"/>\n"
                + "  </table>");

            Assert.True(TestSupport.HasError(unit));
            Assert.Contains(unit.Diagnostics, x => x.Code == "E014");
        }

        [Fact]
        public void Parse_ShouldWarn_WhenBlobHasLength()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Blob\" length=\"100\" nullable=\"true\"/>\n"
                + "  </table>");

            Assert.False(TestSupport.HasError(unit));
            Assert.Contains(unit.Diagnostics, x => x.Code == DiagnosticCodes.BlobLengthIgnored);
        }

        [Fact]
        public void Parse_ShouldRejectNonDefinition_WhenBaselineFrozen()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var path = TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"MODIFY\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            var config = TestSupport.CreateConfig(true);
            config.Baseline.Frozen = true;

            var unit = new DbChronicle.Parsing.ChangeUnitParser(config).Parse(path);

            Assert.Contains(unit.Diagnostics, x => x.Code == "E040");
        }

        [Fact]
        public void Parse_ShouldAcceptStandardFileName()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");

            Assert.DoesNotContain(unit.Diagnostics, x => x.Code == "E003");
        }
    }
}
