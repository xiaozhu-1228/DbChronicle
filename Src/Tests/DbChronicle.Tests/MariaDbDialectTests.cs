using System;

using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Models;
using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// MariaDB 方言：Json → LONGTEXT + CHECK(JSON_VALID) 自动约束、重命名列版本门限、无符号等。
    /// </summary>
    public class MariaDbDialectTests
    {
        private static CompileResult Compile(string body, string version)
        {
            var context = new DialectContext { IncrementalModify = false };
            var dialect = new MariaDbDialect(version, context);
            var unit = TestSupport.ParseSingle(body, false);
            return new SchemaCompiler(dialect,
                new CompilerOptions { IncrementalModify = false }, null).Compile(unit);
        }

        [Fact]
        public void AddColumn_ShouldAppendJsonCheckConstraint()
        {
            var result = Compile(
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"profile\" action=\"ADD\" type=\"Json\" nullable=\"true\" comment=\"画像\"/>\n"
                + "  </table>",
                "10.5");

            Assert.Contains("ALTER TABLE `nor_user` ADD COLUMN `profile` LONGTEXT NULL COMMENT '画像';", result.Sql);
            Assert.Contains("ALTER TABLE `nor_user` ADD CONSTRAINT `ck_nor_user_profile_json`"
                + " CHECK (`profile` IS NULL OR JSON_VALID(`profile`));", result.Sql);
        }

        [Fact]
        public void CreateTable_ShouldAppendJsonCheckConstraint()
        {
            var result = Compile(
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"profile\" action=\"ADD\" type=\"Json\" nullable=\"true\"/>\n"
                + "  </table>",
                "10.5");

            Assert.Contains("`profile` LONGTEXT NULL", result.Sql);
            Assert.Contains("ADD CONSTRAINT `ck_nor_user_profile_json`", result.Sql);
        }

        [Fact]
        public void JsonCheckConstraintName_ShouldTruncate_WhenTooLong()
        {
            var longTable = new string('t', 40);
            var longColumn = new string('c', 40);
            var result = Compile(
                "  <table name=\"" + longTable + "\" action=\"ALTER\">\n"
                + "    <column name=\"" + longColumn + "\" action=\"ADD\" type=\"Json\" nullable=\"true\"/>\n"
                + "  </table>",
                "10.5");

            var line = System.Array.Find(result.Sql.Split('\n'),
                x => x.Contains("ADD CONSTRAINT") && x.Contains("JSON_VALID"));
            Assert.NotNull(line);
            var name = line.Substring(line.IndexOf("ADD CONSTRAINT `", StringComparison.Ordinal) + "ADD CONSTRAINT `".Length);
            name = name.Substring(0, name.IndexOf('`'));
            Assert.True(name.Length <= 64, "约束名超长：" + name);
            Assert.Contains("ck_", name, StringComparison.Ordinal);
        }

        [Fact]
        public void RenameColumn_ShouldUseRenameSyntax_WhenVersionAtLeast105()
        {
            var result = Compile(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"new_name\" action=\"RENAME\" renamedFrom=\"old_name\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>",
                "10.5");

            Assert.Contains("ALTER TABLE `t` RENAME COLUMN `old_name` TO `new_name`;", result.Sql);
        }

        [Fact]
        public void RenameColumn_ShouldUseChangeSyntax_WhenVersionBelow105()
        {
            var result = Compile(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"new_name\" action=\"RENAME\" renamedFrom=\"old_name\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                + "  </table>",
                "10.4");

            Assert.Contains("ALTER TABLE `t` CHANGE `old_name` `new_name` VARCHAR(32) NULL;", result.Sql);
        }

        [Fact]
        public void Unsigned_ShouldBeSupported()
        {
            var result = Compile(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"Int32\" unsigned=\"true\" nullable=\"false\"/>\n"
                + "  </table>",
                "10.5");

            Assert.Contains("`c` INT UNSIGNED NOT NULL", result.Sql);
        }

        [Fact]
        public void DateTimeOffset_ShouldWarn()
        {
            var result = Compile(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"DateTimeOffset\" nullable=\"true\"/>\n"
                + "  </table>",
                "10.5");

            Assert.Contains(result.Diagnostics, x => x.Code == "W003");
            Assert.Contains("`c` TIMESTAMP NULL", result.Sql);
        }

        [Fact]
        public void Seed_ShouldUseMySqlSyntax()
        {
            var result = Compile(
                "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <seed table=\"t\" conflict=\"UPDATE\" columns=\"id\">\n"
                + "      <row values=\"1\"/>\n"
                + "    </seed>\n"
                + "  </table>",
                "10.5");

            Assert.Contains("ON DUPLICATE KEY UPDATE `id` = VALUES(`id`)", result.Sql);
        }

        [Fact]
        public void TypeMapper_ShouldBeIndependentFromMySql()
        {
            var mapper = TypeMapper.Load("mariadb");
            Assert.Equal("mariadb", mapper.DialectName);
            Assert.Equal("LONGTEXT", mapper.Resolve(LogicalType.Json, null, null, null, false));
            Assert.Equal("JSON", TypeMapper.Load("mysql").Resolve(LogicalType.Json, null, null, null, false));
        }
    }
}