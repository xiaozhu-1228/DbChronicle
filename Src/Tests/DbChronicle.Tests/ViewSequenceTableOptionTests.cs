using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// 第 8 类视图、第 9 类序列、第 1 类表选项：结构化生成与方言不支持时的报错。
    /// </summary>
    public class ViewSequenceTableOptionTests
    {
        private const string ViewBody =
            "  <view name=\"v_t1\" action=\"REPLACE\" columns=\"id, name\">\n"
            + "    <select>SELECT id, name FROM t1</select>\n"
            + "  </view>";

        private const string SequenceBody =
            "  <sequence name=\"seq_order\" action=\"CREATE\" start=\"1000\" increment=\"2\" minValue=\"1\" maxValue=\"9999\" cycle=\"false\" cache=\"20\"/>";

        private const string TableOptionBody =
            "  <table name=\"t1\" action=\"ENGINE\" engine=\"InnoDB\" charset=\"utf8mb4\" collate=\"utf8mb4_general_ci\"/>";

        [Fact]
        public void MySql_ShouldCreateOrReplaceView()
        {
            var result = TestSupport.CompileWith("mysql", TestSupport.ParseSingle(ViewBody, false), false, null);

            Assert.Contains("CREATE OR REPLACE VIEW `v_t1` (id, name) AS", result.Sql);
            Assert.Contains("SELECT id, name FROM t1;", result.Sql);
        }

        [Fact]
        public void SqlServer_ShouldUseCreateOrAlterView()
        {
            var result = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(ViewBody, false), false, null);

            Assert.Contains("CREATE OR ALTER VIEW [v_t1] (id, name) AS", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldDropThenCreate_WhenReplaceView()
        {
            var result = TestSupport.CompileWith("sqlite", TestSupport.ParseSingle(ViewBody, false), false, null);

            Assert.Contains("DROP VIEW IF EXISTS \"v_t1\";", result.Sql);
            Assert.Contains("CREATE VIEW \"v_t1\" (id, name) AS", result.Sql);
            Assert.DoesNotContain("OR REPLACE", result.Sql);
        }

        [Fact]
        public void DropView_ShouldWorkOnAllDialects()
        {
            foreach (var dialect in new[] { "mysql", "mariadb", "postgresql", "sqlserver", "sqlite" })
            {
                var result = TestSupport.CompileWith(dialect, TestSupport.ParseSingle(
                    "  <view name=\"v_t1\" action=\"DROP\"/>\n", false), false, null);
                Assert.Contains("DROP VIEW IF EXISTS", result.Sql);
                Assert.False(result.HasErrors, dialect + " 删除视图不应报错");
            }
        }

        [Fact]
        public void PostgreSql_ShouldCreateSequence()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(SequenceBody, false), false, null);

            Assert.Contains("CREATE SEQUENCE \"seq_order\" START WITH 1000 INCREMENT BY 2 MINVALUE 1 MAXVALUE 9999 NO CYCLE CACHE 20;", result.Sql);
        }

        [Fact]
        public void SqlServer_ShouldCreateSequence()
        {
            var result = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(SequenceBody, false), false, null);

            Assert.Contains("CREATE SEQUENCE [seq_order] START WITH 1000 INCREMENT BY 2", result.Sql);
        }

        [Fact]
        public void Sequence_ShouldFail_OnDialectsWithoutNativeSupport()
        {
            foreach (var dialect in new[] { "mysql", "mariadb", "sqlite" })
            {
                var result = TestSupport.CompileWith(dialect, TestSupport.ParseSingle(SequenceBody, false), false, null);
                Assert.True(result.HasErrors, dialect + " 应报错");
                Assert.Contains(result.Diagnostics, x => x.Code == "E046");
            }
        }

        [Fact]
        public void MySql_ShouldAlterTableOptions()
        {
            var result = TestSupport.CompileWith("mysql", TestSupport.ParseSingle(TableOptionBody, false), false, null);

            Assert.Contains("ALTER TABLE `t1` ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldSetTablespace()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <table name=\"t1\" action=\"TABLESPACE\" tablespace=\"fast\"/>\n", false), false, null);

            Assert.Contains("ALTER TABLE \"t1\" SET TABLESPACE \"fast\";", result.Sql);
        }

        [Fact]
        public void RawEnvelope_ShouldWarn_WhenDialectSqlMissing()
        {
            // 只写了 MySQL 的 SQL，PG 编译时应明确告警而不是静默丢弃
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <view name=\"v_t1\" action=\"CREATE\">\n"
                + "    <sql dialect=\"mysql\">CREATE OR REPLACE VIEW v_t1 AS SELECT 1;</sql>\n"
                + "  </view>",
                false), false, null);

            Assert.Contains(result.Diagnostics, x => x.Code == "W032");
            Assert.DoesNotContain("CREATE OR REPLACE VIEW", result.Sql);
        }

        [Fact]
        public void RawEnvelope_ShouldUseDialectSql_WhenProvided()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(
                "  <trigger name=\"trg\" action=\"CREATE\">\n"
                + "    <sql dialect=\"postgresql\">CREATE TRIGGER trg BEFORE INSERT ON t FOR EACH ROW EXECUTE FUNCTION f();</sql>\n"
                + "  </trigger>",
                false), false, null);

            Assert.Contains("CREATE TRIGGER trg BEFORE INSERT ON t", result.Sql);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W032");
        }

        [Fact]
        public void TableOption_ShouldFail_OnUnsupportedDialects()
        {
            var mysqlOnly = TestSupport.ParseSingle(TableOptionBody, false);
            foreach (var dialect in new[] { "postgresql", "sqlserver", "sqlite" })
            {
                var result = TestSupport.CompileWith(dialect, mysqlOnly, false, null);
                Assert.True(result.HasErrors, dialect + " 应报错");
                Assert.Contains(result.Diagnostics, x => x.Code == "E047");
            }
        }
    }
}
