using System.IO;
using System.Linq;

using DbChronicle.Diagnostics;
using DbChronicle.Models;
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
        public void PostgreSql_BooleanDefault_ShouldUseTrueFalse()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"isdeleted\" action=\"ADD\" type=\"Boolean\" nullable=\"false\" defaultValue=\"false\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains("DEFAULT false", result.Sql);
            Assert.DoesNotContain("DEFAULT 0", result.Sql);
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
            Assert.Contains("ALTER TABLE [nor_user] ADD [status] INT NOT NULL CONSTRAINT [DF_nor_user_status] DEFAULT 0", result.Sql);
        }

        [Fact]
        public void SqlServer_ShouldCommentTableViaExtendedProperty()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"CREATE\" comment=\"用户表\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlserver", unit, false, null);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == DiagnosticCodes.TableCommentSkipped);
            Assert.Contains("IDENTITY(1,1)", result.Sql);
            Assert.Contains("sp_addextendedproperty N'MS_Description', N'用户表'", result.Sql);
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

        [Fact]
        public void SqlServer_Modify_ShouldDropDefaultConstraintWithObjectIdGuard()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"status\" action=\"MODIFY\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlserver", unit, false, null);

            Assert.Contains("IF OBJECT_ID(N'DF_nor_user_status', N'D') IS NOT NULL ALTER TABLE [nor_user] DROP CONSTRAINT [DF_nor_user_status]", result.Sql);
            Assert.DoesNotContain("DROP CONSTRAINT IF EXISTS", result.Sql);
        }

        [Fact]
        public void SqlServer_AddColumn_ShouldInlineDefaultConstraint()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"flag\" action=\"ADD\" type=\"Int32\" nullable=\"false\" defaultValue=\"1\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlserver", unit, false, null);

            Assert.Contains("ADD [flag] INT NOT NULL CONSTRAINT [DF_nor_user_flag] DEFAULT 1", result.Sql);
        }

        [Fact]
        public void PostgreSql_Modify_ShouldOnlyEmitChangedAttributes()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_100000_d4e5f6",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"name\" action=\"MODIFY\" nullable=\"false\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("postgresql", unit, true, model);

            Assert.Contains("SET NOT NULL", result.Sql);
            Assert.DoesNotContain("TYPE", result.Sql);
        }

        [Fact]
        public void Sqlite_Rebuild_ShouldCreateIndexAfterRebuild()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_100000_e5f6a7",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"name\" action=\"MODIFY\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                + "    <index name=\"idx_t_name\" action=\"ADD\">\n"
                + "      <columnRef name=\"name\"/>\n"
                + "    </index>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.False(result.HasErrors);
            Assert.True(result.Sql.IndexOf("CREATE INDEX", System.StringComparison.Ordinal)
                > result.Sql.IndexOf("DROP TABLE \"t\"", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Sqlite_Rebuild_ShouldCastWhenStorageClassChanges()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"amount\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_120000_f6a7b8",
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"amount\" action=\"MODIFY\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.Contains("CAST(\"amount\" AS TEXT)", result.Sql);
        }
    }
}