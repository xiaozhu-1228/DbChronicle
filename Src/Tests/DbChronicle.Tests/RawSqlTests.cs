using System.Linq;

using DbChronicle.Diagnostics;
using DbChronicle.Models.Operations;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// &lt;rawSql&gt; 节点：直接写 SQL 文本，不做任何分析、不区分方言
    /// （方言由启动配置决定，纯 SQL 无需按方言分化）。
    /// </summary>
    public class RawSqlTests
    {
        [Fact]
        public void RawSql_ShouldParseInlineText()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql>UPDATE nor_user SET status = 1 WHERE id = 100;</rawSql>",
                false);

            Assert.DoesNotContain(unit.Diagnostics, x => x.Code.StartsWith("E"));
            var raw = unit.Operations.OfType<RawSqlOperation>().Single();
            Assert.Equal("UPDATE nor_user SET status = 1 WHERE id = 100;", raw.SqlByDialect["any"]);
        }

        [Fact]
        public void RawSql_ShouldEmitVerbatimOnAllDialects()
        {
            var unit = TestSupport.ParseSingle("  <rawSql>UPDATE t SET c = 1;</rawSql>", false);

            foreach (var dialect in new[] { "mysql", "mariadb", "postgresql", "sqlserver", "sqlite" })
            {
                var result = TestSupport.CompileWith(dialect, unit, false, null);

                Assert.DoesNotContain(result.Diagnostics, x => x.Code == DiagnosticCodes.DialectSqlMissing);
                Assert.Contains("UPDATE t SET c = 1;", result.Sql);
            }
        }

        [Fact]
        public void RawSql_ShouldSupportCdata()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql><![CDATA[INSERT INTO t (a, b) VALUES ('<x>', 'a & b');]]></rawSql>",
                false);

            var raw = unit.Operations.OfType<RawSqlOperation>().Single();
            Assert.Equal("INSERT INTO t (a, b) VALUES ('<x>', 'a & b');", raw.SqlByDialect["any"]);
        }

        [Fact]
        public void RawSql_ShouldSupportMultiStatement()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql><![CDATA[\nUPDATE t SET c = 1;\nUPDATE t SET d = 2;\n]]></rawSql>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);
            Assert.Contains("UPDATE t SET c = 1", result.Sql);
            Assert.Contains("UPDATE t SET d = 2", result.Sql);
            Assert.Equal(2, result.Statements.Count);
        }

        [Fact]
        public void RawSql_ShouldRejectEmptyContent()
        {
            var unit = TestSupport.ParseSingle("  <rawSql>   </rawSql>", false);

            Assert.Contains(unit.Diagnostics, x => x.Code == DiagnosticCodes.RawSqlEmpty);
        }

        [Fact]
        public void RawSql_InsideTable_ShouldBeIgnoredAsUnknownNode()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <rawSql><![CDATA[UPDATE nor_user SET c = 1;]]></rawSql>\n"
                + "  </table>",
                false);

            // 表节点内不接受原始 SQL：按未知节点忽略（W020），不生成操作
            Assert.DoesNotContain(unit.Operations, x => x is RawSqlOperation);
            Assert.Contains(unit.Diagnostics, x => x.Code == "W020");
        }

        [Fact]
        public void RawSql_ShouldRunAfterStructuredOperations()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql>UPDATE t SET c = 1;</rawSql>\n"
                + "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            var tableIndex = result.Sql.IndexOf("CREATE TABLE");
            var sqlIndex = result.Sql.IndexOf("UPDATE t SET c = 1");
            Assert.True(tableIndex >= 0, "应先生成建表");
            Assert.True(sqlIndex > tableIndex, "rawSql 应排在结构化操作之后");
        }

        [Fact]
        public void RawSql_ShouldStillAcceptLegacyChildSqlNodes()
        {
            // 旧写法（子 <sql dialect>）保留兼容，但不再推荐
            var unit = TestSupport.ParseSingle(
                "  <rawSql>\n"
                + "    <sql dialect=\"mysql\">A</sql>\n"
                + "    <sql dialect=\"postgresql\">B</sql>\n"
                + "  </rawSql>",
                false);

            var raw = unit.Operations.OfType<RawSqlOperation>().Single();
            Assert.Equal("A", raw.SqlByDialect["mysql"]);
            Assert.Equal("B", raw.SqlByDialect["postgresql"]);
        }
    }
}
