using System.Linq;

using DbChronicle.Common.Enums;
using DbChronicle.Diagnostics;
using DbChronicle.Models.Operations;
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

            var create = (CreateTableOperation)unit.Operations[0];
            Assert.Equal(2, create.Columns.Count);
            Assert.Single(create.PrimaryKeyColumns);
            Assert.Equal("id", create.PrimaryKeyColumns[0]);
            Assert.Equal(LogicalType.Int64, create.Columns[0].Type);
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

        [Fact]
        public void Parse_ShouldRejectInvalidStage()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "    <update set=\"c = 1\" stage=\"whenever\"/>\n"
                + "  </table>");

            Assert.Contains(unit.Diagnostics, x => x.Code == "E026");
        }

        [Fact]
        public void Parse_ShouldRequireTypeAndNullable_WhenFullModify()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"MODIFY\" type=\"Int32\"/>\n"
                + "  </table>",
                false);

            Assert.Contains(unit.Diagnostics, x => x.Code == "E027");
        }

        [Fact]
        public void Parse_ShouldRequireAtLeastOneAttribute_WhenIncrementalModify()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"MODIFY\"/>\n"
                + "  </table>");

            Assert.Contains(unit.Diagnostics, x => x.Code == "E028");
        }

        [Fact]
        public void Parse_ShouldRejectDuplicateColumnInSameUnit()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "    <column name=\"c\" action=\"MODIFY\" length=\"10\"/>\n"
                + "  </table>");

            Assert.Contains(unit.Diagnostics, x => x.Code == "E029");
        }

        [Fact]
        public void Parse_ShouldRejectForeignKey_WhenFormatInvalid()
        {
            var noDot = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int64\" nullable=\"true\" foreignKey=\"id\"/>\n"
                + "  </table>");
            Assert.Contains(noDot.Diagnostics, x => x.Code == "E031");

            var tooManyDots = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int64\" nullable=\"true\" foreignKey=\"a.b.c\"/>\n"
                + "  </table>");
            Assert.Contains(tooManyDots.Diagnostics, x => x.Code == "E031");

            var emptyPart = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int64\" nullable=\"true\" foreignKey=\"nor_user.\"/>\n"
                + "  </table>");
            Assert.Contains(emptyPart.Diagnostics, x => x.Code == "E031");
        }

        [Fact]
        public void Parse_ShouldAcceptForeignKey_WhenFormatValid()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int64\" nullable=\"true\" foreignKey=\"nor_user.id\"/>\n"
                + "  </table>");

            Assert.DoesNotContain(unit.Diagnostics, x => x.Code == "E031");
        }
    }
}