using System.Linq;

using DbChronicle.Compilation.Compiler;
using Xunit;

namespace DbChronicle.Tests
{
    public class FixVerificationTests
    {
        [Fact]
        public void MySql_ShouldEmitColumnCharsetAndCollate()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\""
                + " charset=\"utf8mb4\" collate=\"utf8mb4_bin\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("CHARACTER SET utf8mb4 COLLATE utf8mb4_bin", sql);
        }

        [Fact]
        public void PostgreSql_ShouldWarnOnColumnCharset()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\" charset=\"utf8mb4\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == "W015");
        }

        [Fact]
        public void MySql_ShouldEmitInlineForeignKeyOnCreateTable()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\" foreignKey=\"nor_user.id\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("REFERENCES `nor_user` (`id`)", sql);
        }

        [Fact]
        public void MySql_ShouldWarnOnInlineForeignKeyInAddColumn()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"uid\" action=\"ADD\" type=\"Int64\" nullable=\"true\" foreignKey=\"nor_user.id\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == "W016");
        }

        [Fact]
        public void MySql_ShouldEmitFulltextIndex()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <index name=\"ft_bio\" action=\"ADD\" type=\"FULLTEXT\">\n"
                + "      <columnRef name=\"bio\"/>\n"
                + "    </index>\n"
                + "  </table>",
                false);

            Assert.Contains("CREATE FULLTEXT INDEX `ft_bio`", sql);
        }

        [Fact]
        public void PostgreSql_ShouldWarnOnFulltextIndex()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <index name=\"ft_bio\" action=\"ADD\" type=\"FULLTEXT\">\n"
                + "      <columnRef name=\"bio\"/>\n"
                + "    </index>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);
            Assert.Contains(result.Diagnostics, x => x.Code == "W017");
        }

        [Fact]
        public void ReplacePrimaryKey_ShouldDropThenAdd()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <primaryKey action=\"REPLACE\" columns=\"tenant_id, id\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("DROP PRIMARY KEY", sql);
            Assert.Contains("ADD PRIMARY KEY (`tenant_id`, `id`)", sql);
            Assert.True(sql.IndexOf("DROP PRIMARY KEY") < sql.IndexOf("ADD PRIMARY KEY"));
        }

        [Fact]
        public void ModifyForeignKey_ShouldDropThenAdd()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <foreignKey name=\"fk_t_user\" action=\"MODIFY\" columns=\"user_id\""
                + " refTable=\"nor_user\" refColumns=\"id\" onDelete=\"CASCADE\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("DROP FOREIGN KEY `fk_t_user`", sql);
            Assert.Contains("ADD CONSTRAINT `fk_t_user`", sql);
            Assert.Contains("ON DELETE CASCADE", sql);
        }

        [Fact]
        public void DataOperations_ShouldRenderUpdateDeleteCleanBackfill()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <update set=\"status = 1\" where=\"id &gt; 0\"/>\n"
                + "    <delete where=\"id &lt; 0\"/>\n"
                + "    <clean rules=\"email = NULL; phone = NULL\" where=\"email = ''\"/>\n"
                + "    <backfill column=\"email\" value=\"''\" where=\"email IS NULL\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("UPDATE `t` SET status = 1 WHERE id > 0", sql);
            Assert.Contains("DELETE FROM `t` WHERE id < 0", sql);
            Assert.Contains("UPDATE `t` SET email = NULL, phone = NULL WHERE email = ''", sql);
            Assert.Contains("UPDATE `t` SET `email` = '' WHERE email IS NULL", sql);
        }

        [Fact]
        public void StageBefore_ShouldRunBeforeStructureChange()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "    <update set=\"c = 1\" where=\"c IS NULL\" stage=\"before\"/>\n"
                + "  </table>",
                false);

            var updateIndex = sql.IndexOf("UPDATE `t`");
            var addIndex = sql.IndexOf("ADD COLUMN `c`");
            Assert.True(updateIndex >= 0 && addIndex >= 0);
            Assert.True(updateIndex < addIndex);
        }

        [Fact]
        public void Splitter_ShouldHonorCustomDelimiter()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1$$SELECT 2$$", "mysql", "$$");
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Rebuild_ShouldWarn()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"REBUILD\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            Assert.Contains(unit.Diagnostics, x => x.Code == "W018");
        }

        [Fact]
        public void UnknownTableAction_ShouldHintRawSql()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"PARTITION\"/>\n",
                false);

            Assert.Contains(unit.Diagnostics, x => x.Code == "E012" && x.Message.Contains("rawSql"));
        }

        [Fact]
        public void TableOptionWithoutAnyValue_ShouldFail()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ENGINE\"/>\n",
                false);

            Assert.Contains(unit.Diagnostics, x => x.Code == "E050");
        }

        [Fact]
        public void FunctionDefaults_ShouldNotBeQuoted()
        {
            var sql = TestSupport.CompileSql(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"DateTime\" nullable=\"false\" defaultValue=\"UTC_TIMESTAMP()\"/>\n"
                + "    <column name=\"b\" action=\"ADD\" type=\"DateTime\" nullable=\"false\" defaultValue=\"NOW()\"/>\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\" defaultValue=\"'UTC_TIMESTAMP()'\"/>\n"
                + "  </table>",
                false);

            Assert.Contains("`a` DATETIME NOT NULL DEFAULT UTC_TIMESTAMP()", sql);
            Assert.Contains("`b` DATETIME NOT NULL DEFAULT NOW()", sql);
            // 显式带引号的仍是字符串字面量
            Assert.Contains("`c` VARCHAR(32) NULL DEFAULT 'UTC_TIMESTAMP()'", sql);
        }

        [Fact]
        public void Sqlite_ShouldPassThroughDatetimeNow()
        {
            var sql = TestSupport.CompileWith("sqlite", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"DateTime\" nullable=\"false\" defaultValue=\"datetime('now')\"/>\n"
                + "  </table>",
                false), false, null).Sql;

            Assert.Contains("\"a\" TEXT NOT NULL DEFAULT datetime('now')", sql);
        }

        [Fact]
        public void PostgreSql_ShouldPassThroughCurrentTimestampAtTimeZone()
        {
            var sql = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"DateTimeOffset\" nullable=\"false\""
                + " defaultValue=\"CURRENT_TIMESTAMP AT TIME ZONE 'UTC'\"/>\n"
                + "  </table>",
                false), false, null).Sql;

            Assert.Contains("\"a\" TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP AT TIME ZONE 'UTC'", sql);
        }

        [Fact]
        public void BooleanDefaults_ShouldFollowDialect()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"Boolean\" nullable=\"false\" defaultValue=\"true\"/>\n"
                + "  </table>",
                false);

            var mySql = TestSupport.Compile(unit, false, null).Sql;
            Assert.Contains("`a` TINYINT(1) NOT NULL DEFAULT 1", mySql);

            var pg = TestSupport.CompileWith("postgresql", unit, false, null).Sql;
            Assert.Contains("\"a\" BOOLEAN NOT NULL DEFAULT true", pg);
        }
    }
}