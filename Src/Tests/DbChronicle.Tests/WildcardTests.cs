using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using Xunit;

namespace DbChronicle.Tests
{
    public class WildcardTests
    {
        [Fact]
        public void Wildcard_ShouldKeepPlaceholder_WhenOffline()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions { IncrementalModify = false });

            Assert.False(result.HasErrors);
            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.WildcardPlaceholderKept);
            Assert.Contains("ALTER TABLE `*` ADD COLUMN `created_by`", result.Sql);
        }

        [Fact]
        public void Wildcard_ShouldExpandFromDatabaseTables()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions
            {
                IncrementalModify = false,
                WildcardTables = new List<string> { "t1", "t2" }
            });

            Assert.False(result.HasErrors);
            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.WildcardExpanded);
            Assert.Contains("ALTER TABLE `t1` ADD COLUMN `created_by`", result.Sql);
            Assert.Contains("ALTER TABLE `t2` ADD COLUMN `created_by`", result.Sql);
        }

        [Fact]
        public void Wildcard_ShouldRunBeforeOtherOperations()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"remark\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"created_by\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions
            {
                IncrementalModify = false,
                WildcardTables = new List<string> { "t1", "t2" }
            });

            var wildcardIndex = result.Sql.IndexOf("ADD COLUMN `created_by`");
            var explicitIndex = result.Sql.IndexOf("ADD COLUMN `remark`");
            Assert.True(wildcardIndex >= 0 && explicitIndex >= 0);
            Assert.True(wildcardIndex < explicitIndex, "通配符语句应排在其他语句之前");
        }

        [Fact]
        public void Wildcard_ShouldSkipSystemTables()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"*\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions
            {
                IncrementalModify = false,
                WildcardTables = new List<string> { "t1", "dbchronicle_version_log" }
            });

            Assert.Contains("ALTER TABLE `t1` ADD COLUMN `c`", result.Sql);
            Assert.DoesNotContain("dbchronicle_version_log", result.Sql);
        }

        [Fact]
        public void Wildcard_ExplicitTables_ShouldOverrideDatabaseList()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"*\" action=\"ALTER\" tables=\"only_this\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions
            {
                IncrementalModify = false,
                WildcardTables = new List<string> { "t1", "t2" }
            });

            Assert.Contains("ALTER TABLE `only_this` ADD COLUMN `c`", result.Sql);
            Assert.DoesNotContain("ALTER TABLE `t1`", result.Sql);
            Assert.DoesNotContain("ALTER TABLE `t2`", result.Sql);
        }

        [Fact]
        public void Wildcard_ShouldFail_ForCreateTable()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"*\" action=\"CREATE\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWithOptions(unit, new CompilerOptions
            {
                IncrementalModify = false,
                WildcardTables = new List<string> { "t1" }
            });

            Assert.Contains(result.Diagnostics, x => x.Code == DiagnosticCodes.WildcardUnsupported);
            Assert.True(result.HasErrors);
        }
    }
}
