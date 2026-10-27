using System.IO;

using DbChronicle.Models;
using DbChronicle.Parsing;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// SEED 冲突策略按方言生成：MySQL/MariaDB ON DUPLICATE KEY / INSERT IGNORE，
    /// PostgreSQL ON CONFLICT（主键目标），SQLite INSERT OR IGNORE / ON CONFLICT（3.24+），
    /// SQL Server 降级并警告。
    /// </summary>
    public class SeedTests
    {
        private const string SeedBody =
            "  <table name=\"nor_errorcode\" action=\"ALTER\">\n"
            + "    <seed table=\"nor_errorcode\" conflict=\"UPDATE\" columns=\"id,message\">\n"
            + "      <row values=\"8,'余量不足:{0}'\"/>\n"
            + "    </seed>\n"
            + "  </table>";

        [Fact]
        public void MySql_ShouldEmitOnDuplicateKeyUpdate()
        {
            var result = TestSupport.CompileWith("mysql", TestSupport.ParseSingle(SeedBody, false), false, null);

            Assert.Contains("INSERT INTO `nor_errorcode` (`id`, `message`) VALUES", result.Sql);
            Assert.Contains("ON DUPLICATE KEY UPDATE `id` = VALUES(`id`), `message` = VALUES(`message`)", result.Sql);
        }

        [Fact]
        public void MariaDb_ShouldEmitOnDuplicateKeyUpdate()
        {
            var result = TestSupport.CompileWith("mariadb", TestSupport.ParseSingle(SeedBody, false), false, null);

            Assert.Contains("ON DUPLICATE KEY UPDATE `id` = VALUES(`id`), `message` = VALUES(`message`)", result.Sql);
        }

        [Fact]
        public void MySql_ShouldEmitInsertIgnore_WhenConflictIgnore()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_errorcode\" action=\"ALTER\">\n"
                + "    <seed table=\"nor_errorcode\" conflict=\"IGNORE\" columns=\"id,message\">\n"
                + "      <row values=\"8,'x'\"/>\n"
                + "    </seed>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            Assert.Contains("INSERT IGNORE INTO `nor_errorcode`", result.Sql);
            Assert.DoesNotContain("ON DUPLICATE", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldEmitOnConflictDoUpdate_WithPrimaryKeyTarget()
        {
            var model = BuildModelWithPrimaryKey();

            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(SeedBody, true), true, model);

            Assert.Contains("INSERT INTO \"nor_errorcode\" (\"id\", \"message\") VALUES", result.Sql);
            Assert.Contains("ON CONFLICT (\"id\") DO UPDATE SET \"id\" = EXCLUDED.\"id\", \"message\" = EXCLUDED.\"message\"", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldEmitOnConflictDoNothing_WhenConflictIgnore()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_errorcode\" action=\"ALTER\">\n"
                + "    <seed table=\"nor_errorcode\" conflict=\"IGNORE\" columns=\"id,message\">\n"
                + "      <row values=\"8,'x'\"/>\n"
                + "    </seed>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);

            Assert.Contains("ON CONFLICT DO NOTHING", result.Sql);
        }

        [Fact]
        public void PostgreSql_ShouldDegrade_WhenPrimaryKeyUnknown()
        {
            var result = TestSupport.CompileWith("postgresql", TestSupport.ParseSingle(SeedBody, false), false, null);

            Assert.Contains(result.Diagnostics, x => x.Code == "W029");
            Assert.DoesNotContain("ON CONFLICT", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldEmitInsertOrIgnore_WhenConflictIgnore()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_errorcode\" action=\"ALTER\">\n"
                + "    <seed table=\"nor_errorcode\" conflict=\"IGNORE\" columns=\"id,message\">\n"
                + "      <row values=\"8,'x'\"/>\n"
                + "    </seed>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("sqlite", unit, false, null);

            Assert.Contains("INSERT OR IGNORE INTO \"nor_errorcode\"", result.Sql);
        }

        [Fact]
        public void Sqlite_ShouldEmitOnConflictDoUpdate_WithPrimaryKeyTarget()
        {
            var model = BuildModelWithPrimaryKey();

            var result = TestSupport.CompileWith("sqlite", TestSupport.ParseSingle(SeedBody, true), true, model);

            Assert.Contains("ON CONFLICT (\"id\") DO UPDATE SET \"id\" = EXCLUDED.\"id\", \"message\" = EXCLUDED.\"message\"", result.Sql);
        }

        [Fact]
        public void SqlServer_ShouldDegrade_WithWarning()
        {
            var result = TestSupport.CompileWith("sqlserver", TestSupport.ParseSingle(SeedBody, false), false, null);

            Assert.Contains(result.Diagnostics, x => x.Code == "W029");
            Assert.DoesNotContain("ON CONFLICT", result.Sql);
            Assert.DoesNotContain("ON DUPLICATE", result.Sql);
        }

        [Fact]
        public void NoConflict_ShouldEmitPlainInsert()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_errorcode\" action=\"ALTER\">\n"
                + "    <seed table=\"nor_errorcode\" columns=\"id,message\">\n"
                + "      <row values=\"8,'x'\"/>\n"
                + "    </seed>\n"
                + "  </table>",
                false);

            foreach (var dialect in new[] { "mysql", "postgresql", "sqlite", "sqlserver" })
            {
                var result = TestSupport.CompileWith(dialect, unit, false, null);
                Assert.Contains("INSERT INTO", result.Sql);
                Assert.DoesNotContain("ON CONFLICT", result.Sql);
                Assert.DoesNotContain("ON DUPLICATE", result.Sql);
                Assert.DoesNotContain(result.Diagnostics, x => x.Code == "W029");
            }
        }

        /// <summary>构造含 nor_errorcode(id 主键) 的累积模型，供需要冲突目标的方言使用。</summary>
        private static StructureModel BuildModelWithPrimaryKey()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            TestSupport.WriteUnit(directory, "00000000_000000_baseline",
                "  <table name=\"nor_errorcode\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int32\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"message\" action=\"ADD\" type=\"String\" length=\"200\" nullable=\"true\"/>\n"
                + "  </table>");

            var parser = new ChangeUnitParser(TestSupport.CreateConfig(true));
            var baseline = parser.Parse(Path.Combine(directory, "00000000_000000_baseline.xml"));
            return new StructureModelBuilder().Build(new[] { baseline }, null);
        }
    }
}