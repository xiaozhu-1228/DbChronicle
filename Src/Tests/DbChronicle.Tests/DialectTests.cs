using System.IO;
using System.Linq;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    public class DialectTests
    {
        [Fact]
        public void PostgreSql_ShouldEmitAddColumnWithSeparateComment()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\" comment=\"手机号\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains("ALTER TABLE \"nor_user\" ADD COLUMN \"phone\" VARCHAR(20) NULL", result.Sql);
            Assert.Contains("COMMENT ON COLUMN \"nor_user\".\"phone\" IS '手机号'", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldSplitModifyIntoMultipleStatements()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"email\" action=\"MODIFY\" type=\"String\" length=\"128\" nullable=\"false\" defaultValue=\"none\" comment=\"邮箱\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains("ALTER COLUMN \"email\" TYPE VARCHAR(128)", result.Sql);
            Assert.Contains("SET NOT NULL", result.Sql);
            Assert.Contains("DROP DEFAULT", result.Sql);
            Assert.Contains("COMMENT ON COLUMN", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldWarnOnUnsigned()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" unsigned=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.UnsignedIgnored);
        }

        [Fact]
        public void SqlServer_ShouldEmitNamedDefaultConstraint()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"status\" action=\"ADD\" type=\"Int32\" nullable=\"false\" defaultValue=\"0\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlserver", unit, false, null);
            Assert.Contains("ALTER TABLE [nor_user] ADD [status] INT NOT NULL", result.Sql);
            Assert.Contains("ADD CONSTRAINT [DF_nor_user_status] DEFAULT 0 FOR [status]", result.Sql);
        }

        [Fact]
        public void SqlServer_ShouldWarnAndSkipComment()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"CREATE\" comment=\"用户表\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlserver", unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.TableCommentSkipped);
            Assert.Contains("IDENTITY(1,1)", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldRebuild_WhenModifyColumn()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");

            var path = TestSupport.WriteUnit(directory, "20261022_100000_a1b2c3",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"name\" action=\"MODIFY\" type=\"String\" length=\"64\" nullable=\"false\"/>\n"
                + "  </table>");

            var config = TestSupport.CreateConfig(true);
            var parser = new ChangeUnitParser(config);
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"t__rebuild\"", result.Sql);
            Assert.Contains("INSERT INTO \"t__rebuild\" (\"id\", \"name\") SELECT \"id\", \"name\" FROM \"t\"", result.Sql);
            Assert.Contains("DROP TABLE \"t\"", result.Sql);
            Assert.Contains("RENAME TO \"t\"", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldFail_WhenAutoIncrementIsNotInt64PrimaryKey()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int32\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            Assert.Throws<System.InvalidOperationException>(() => TestSupport.CompileWith("sqlite", unit, false, null));
        }
    }
}
