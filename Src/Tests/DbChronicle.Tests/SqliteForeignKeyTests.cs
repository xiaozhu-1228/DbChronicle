using System.IO;
using System.Linq;

using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// SQLite 外键：建表级 &lt;foreignKey&gt; 内联进 CREATE TABLE；ALTER 增删外键走 rebuild 并重放外键。
    /// </summary>
    public class SqliteForeignKeyTests
    {
        [Fact]
        public void CreateTable_ShouldInlineForeignKey_AndNotRebuild()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\" onDelete=\"RESTRICT\" onUpdate=\"CASCADE\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlite", unit, false, null);

            Assert.False(result.HasErrors);
            Assert.Contains("CONSTRAINT \"fk_order_user\" FOREIGN KEY (\"user_id\") REFERENCES \"nor_user\" (\"id\") ON DELETE RESTRICT ON UPDATE CASCADE", result.Sql);
            Assert.DoesNotContain("__rebuild", result.Sql);
        }

        [Fact]
        public void AlterAddForeignKey_ShouldRebuild_WithForeignKeyInlined()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_100000_a1b2c3",
                "  <table name=\"nor_order\" action=\"ALTER\">\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"nor_order__rebuild\"", result.Sql);
            Assert.Contains("CONSTRAINT \"fk_order_user\" FOREIGN KEY (\"user_id\") REFERENCES \"nor_user\" (\"id\")", result.Sql);
            Assert.DoesNotContain("ADD CONSTRAINT", result.Sql);
        }

        [Fact]
        public void AlterModifyColumn_ShouldRebuild_WithHistoricalForeignKeyReplayed()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_100000_a1b2c3",
                "  <table name=\"nor_order\" action=\"ALTER\">\n"
                + "    <column name=\"user_id\" action=\"MODIFY\" type=\"Int32\" nullable=\"false\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"nor_order__rebuild\"", result.Sql);
            Assert.Contains("CONSTRAINT \"fk_order_user\" FOREIGN KEY (\"user_id\") REFERENCES \"nor_user\" (\"id\")", result.Sql);
        }

        [Fact]
        public void AlterDropForeignKey_ShouldRebuild_WithoutDroppedForeignKey()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\"/>\n"
                + "  </table>");
            var path = TestSupport.WriteUnit(directory, "20261023_100000_a1b2c3",
                "  <table name=\"nor_order\" action=\"ALTER\">\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"DROP\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            var unit = parser.Parse(path);
            var model = new StructureModelBuilder().Build(new[] { baseline, unit }, unit.Id);

            var result = TestSupport.CompileWith("sqlite", unit, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("CREATE TABLE \"nor_order__rebuild\"", result.Sql);
            Assert.DoesNotContain("fk_order_user", result.Sql);
        }

        [Fact]
        public void MySql_ShouldStillEmitForeignKeyAsSeparateStatement()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            Assert.Contains("ALTER TABLE `nor_order` ADD CONSTRAINT `fk_order_user` FOREIGN KEY (`user_id`) REFERENCES `nor_user` (`id`)", result.Sql);
        }
    }
}