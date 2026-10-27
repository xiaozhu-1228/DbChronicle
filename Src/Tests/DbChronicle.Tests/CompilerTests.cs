using System.IO;
using System.Linq;

using DbChronicle.Diagnostics;
using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    public class CompilerTests
    {
        [Fact]
        public void Compile_ShouldEmitAddColumn()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\" comment=\"手机号\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("ALTER TABLE `nor_user` ADD COLUMN `phone` VARCHAR(20) NULL COMMENT '手机号'", sql);
        }

        [Fact]
        public void Compile_ShouldEmitCreateTable()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("CREATE TABLE IF NOT EXISTS `nor_user`", sql);
            Assert.Contains("`id` BIGINT NOT NULL AUTO_INCREMENT", sql);
            Assert.Contains("PRIMARY KEY (`id`)", sql);
        }

        [Fact]
        public void Compile_ShouldWarn_WhenDateTimeOffset()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"updated_at\" action=\"ADD\" type=\"DateTimeOffset\" nullable=\"true\"/>\n"
                + "  </table>");

            var result = TestSupport.Compile(unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.DateTimeOffsetDowngraded);
            Assert.Contains("TIMESTAMP", result.Sql);
        }

        [Fact]
        public void Compile_ShouldFullRewrite_WhenIncrementalDisabled()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"email\" action=\"MODIFY\" type=\"String\" length=\"128\" nullable=\"false\" defaultValue=\"''\" comment=\"邮箱\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.Compile(unit, false, null);
            Assert.Contains("MODIFY COLUMN `email` VARCHAR(128) NOT NULL DEFAULT '' COMMENT '邮箱'", result.Sql);
        }

        [Fact]
        public void Compile_ShouldMergeHistory_WhenIncrementalEnabled()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"email\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"false\" defaultValue=\"''\" comment=\"邮箱\"/>\n"
                + "  </table>");

            var path = TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"email\" action=\"MODIFY\" length=\"128\"/>\n"
                + "  </table>");

            var config = TestSupport.CreateConfig(true);
            var parser = new ChangeUnitParser(config);
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var units = new[] { baseline, unit };

            var model = new StructureModelBuilder().Build(units, unit.Id);
            var result = TestSupport.Compile(unit, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("MODIFY COLUMN `email` VARCHAR(128) NOT NULL DEFAULT '' COMMENT '邮箱'", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == DiagnosticCodes.IncrementalBaseMissing);
        }

        [Fact]
        public void Compile_ShouldWarnAndDegrade_WhenIncrementalModelMissing()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"email\" action=\"MODIFY\" length=\"128\"/>\n"
                + "  </table>",
                true);

            var result = TestSupport.Compile(unit, true, null);

            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.IncrementalBaseMissing);
            Assert.DoesNotContain("MODIFY COLUMN", result.Sql);
        }
    }
}