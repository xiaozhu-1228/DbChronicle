using System.IO;

using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// 第 6 类约束在 SQLite 上的实现：检查约束与默认值约束只能靠重建表落地。
    /// </summary>
    public class SqliteConstraintTests
    {
        private const string BaselineBody =
            "  <table name=\"t\" action=\"CREATE\">\n"
            + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
            + "    <column name=\"amount\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
            + "  </table>";

        [Fact]
        public void AddCheckConstraint_ShouldRebuildWithCheck()
        {
            var result = CompileSqlite(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <checkConstraint name=\"ck_amount\" action=\"ADD\" expression=\"amount &gt;= 0\"/>\n"
                + "  </table>");

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"t__rebuild\"", result.Sql);
            Assert.Contains("CONSTRAINT \"ck_amount\" CHECK (amount >= 0)", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W025");
        }

        [Fact]
        public void AddDefaultConstraint_ShouldRebuildWithColumnDefault()
        {
            var result = CompileSqlite(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <defaultConstraint name=\"df_amount\" action=\"ADD\" column=\"amount\" value=\"0\"/>\n"
                + "  </table>");

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"t__rebuild\"", result.Sql);
            Assert.Contains("\"amount\" INTEGER DEFAULT 0", result.Sql);
        }

        [Fact]
        public void DropDefaultConstraint_ShouldRebuildWithoutDefault()
        {
            var result = CompileSqlite(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <defaultConstraint name=\"df_amount\" action=\"DROP\" column=\"amount\"/>\n"
                + "  </table>");

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"t__rebuild\"", result.Sql);
            Assert.DoesNotContain("\"amount\" INTEGER DEFAULT", result.Sql);
        }

        private static CompileResult CompileSqlite(string unitBody)
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline", BaselineBody);
            var path = TestSupport.WriteUnit(directory, "20261026_160000_a1b2c3", unitBody);

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline }, null);

            return TestSupport.CompileWith("sqlite", unit, true, model);
        }
    }
}
