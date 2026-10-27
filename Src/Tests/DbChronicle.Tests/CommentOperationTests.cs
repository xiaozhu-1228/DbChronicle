using System;

using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// 第 7 类注释操作：表 / 列 / 索引 / 约束四种对象的方言实现与不支持降级。
    /// </summary>
    public class CommentOperationTests
    {
        [Fact]
        public void PostgreSql_ShouldCommentIndex()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <index name=\"idx_a\" action=\"COMMENT\" comment=\"索引说明\"/>\n"
                + "  </table>",
                false), false, null);

            Assert.Contains("COMMENT ON INDEX \"idx_a\" IS '索引说明';", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W031");
        }

        [Fact]
        public void PostgreSql_ShouldCommentConstraint()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <constraintComment name=\"ck_a\" kind=\"check\" comment=\"约束说明\"/>\n"
                + "  </table>",
                false), false, null);

            Assert.Contains("COMMENT ON CONSTRAINT \"ck_a\" ON \"t\" IS '约束说明';", result.Sql);
        }

        [Fact]
        public void MySql_ShouldWarnOnIndexComment()
        {
            var result = TestSupport.CompileWith("mysql", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <index name=\"idx_a\" action=\"COMMENT\" comment=\"索引说明\"/>\n"
                + "  </table>",
                false), false, null);

            Assert.Contains(result.Diagnostics, x => x.Code == "W031");
        }

        [Fact]
        public void Sqlite_ShouldWarnOnConstraintComment()
        {
            var result = TestSupport.CompileWith("sqlite", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <constraintComment name=\"uk_a\" kind=\"unique\" comment=\"约束说明\"/>\n"
                + "  </table>",
                false), false, null);

            Assert.Contains(result.Diagnostics, x => x.Code == "W031");
        }

        [Fact]
        public void SqlServer_ShouldCommentTableViaExtendedProperty()
        {
            var result = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"COMMENT\" comment=\"表说明\"/>\n",
                false), false, null);

            Assert.Contains("sys.extended_properties", result.Sql);
            Assert.Contains("sp_addextendedproperty N'MS_Description', N'表说明'", result.Sql);
            Assert.Contains("sp_updateextendedproperty", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W006");
        }

        [Fact]
        public void SqlServer_ShouldCommentColumnViaExtendedProperty()
        {
            var result = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\" comment=\"列说明\"/>\n"
                + "  </table>",
                false), false, null);

            Assert.Contains("N'COLUMN', N'c'", result.Sql);
            Assert.Contains("COLUMNPROPERTY(", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W007");
        }

        [Fact]
        public void SqlServer_ShouldCommentIndexAndConstraint()
        {
            var indexResult = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <index name=\"idx_a\" action=\"COMMENT\" comment=\"索引说明\"/>\n"
                + "  </table>",
                false), false, null);
            Assert.Contains("N'INDEX', N'idx_a'", indexResult.Sql);

            var constraintResult = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <constraintComment name=\"ck_a\" kind=\"check\" comment=\"约束说明\"/>\n"
                + "  </table>",
                false), false, null);
            Assert.Contains("N'CONSTRAINT', N'ck_a'", constraintResult.Sql);
        }

        [Fact]
        public void ColumnReorder_ShouldFail_OnDialectsWithoutSupport()
        {
            const string body =
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"REORDER\" after=\"id\"/>\n"
                + "  </table>";

            foreach (var dialect in new[] { "postgresql", "sqlserver", "sqlite" })
            {
                var result = TestSupport.CompileWith(dialect, TestSupport.ParseSingle(body, false), false, null);
                Assert.True(result.HasErrors, dialect + " 应报错而不是静默忽略");
                Assert.Contains(result.Diagnostics, x => x.Code == "E044");
            }
        }

        [Fact]
        public void ColumnReorder_ShouldPass_OnMySql()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" length=\"8\" nullable=\"true\"/>\n"
                + "  </table>");
            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(System.IO.Path.Combine(directory, "00000000_000000_baseline.xml"));
            var model = new StructureModelBuilder().Build(new[] { baseline }, null);
            var reorder = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"REORDER\" after=\"id\"/>\n"
                + "  </table>",
                true);

            var result = TestSupport.CompileWith("mysql", reorder, true, model);

            Assert.False(result.HasErrors);
            Assert.Contains("AFTER `id`", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldRejectCompositePrimaryKeyWithAutoIncrement()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"no\" action=\"ADD\" type=\"String\" length=\"16\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                false);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                TestSupport.CompileWith("sqlite", unit, false, null));

            Assert.Contains("复合主键", exception.Message);
        }
    }
}
