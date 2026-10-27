using System;

using DbChronicle.Compilation.Compiler;
using Xunit;

namespace DbChronicle.Tests
{
    public class SplitterTests
    {
        [Fact]
        public void Split_ShouldKeepSemicolonInsideString()
        {
            var parts = SqlStatementSplitter.Split("INSERT INTO t VALUES ('a;b');\nSELECT 1;", "mysql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("'a;b'", parts[0]);
        }

        [Fact]
        public void Split_ShouldIgnoreSemicolonInLineComment()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1; -- comment ; here\nSELECT 2;", "mysql");
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Split_ShouldKeepBeginEndBlock()
        {
            var parts = SqlStatementSplitter.Split(
                "CREATE TRIGGER trg BEFORE INSERT ON t FOR EACH ROW\nBEGIN\n  SET @a = 1;\nEND;\nSELECT 1;",
                "mysql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("END", parts[0]);
        }

        [Fact]
        public void Split_ShouldHonorDelimiterDirective()
        {
            var parts = SqlStatementSplitter.Split(
                "DELIMITER $$\nCREATE PROCEDURE p()\nBEGIN\n  SELECT 1;\nEND$$\nDELIMITER ;",
                "mysql");
            Assert.Single(parts);
            Assert.Contains("CREATE PROCEDURE p()", parts[0]);
        }

        [Fact]
        public void Split_ShouldKeepIfEndIfBlock()
        {
            var parts = SqlStatementSplitter.Split(
                "CREATE PROCEDURE p() BEGIN IF 1=1 THEN SELECT 1; END IF; SELECT 2; END;\nSELECT 9;",
                "mysql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("SELECT 2", parts[0]);
        }

        [Fact]
        public void Split_ShouldKeepCaseInsideBeginEnd()
        {
            var parts = SqlStatementSplitter.Split(
                "CREATE PROCEDURE p() BEGIN CASE WHEN 1=1 THEN SELECT 2; END; SELECT 3; END;\nSELECT 9;",
                "mysql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("SELECT 3", parts[0]);
        }

        [Fact]
        public void Split_ShouldNotTreatIfFunctionAsBlock()
        {
            var parts = SqlStatementSplitter.Split("SELECT IF(a > 1, 1, 0) FROM t;\nSELECT 2;", "mysql");
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Split_ShouldNotTreatIfExistsAsBlock()
        {
            var parts = SqlStatementSplitter.Split("DROP TABLE IF EXISTS t;\nCREATE TABLE IF NOT EXISTS t2 (a INT);", "mysql");
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Split_ShouldKeepPostgresDollarQuotedFunction()
        {
            var parts = SqlStatementSplitter.Split(
                "CREATE FUNCTION f() RETURNS int AS $$ SELECT 1; $$ LANGUAGE sql;\nSELECT 1;",
                "postgresql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("$$ SELECT 1; $$", parts[0]);
        }

        [Fact]
        public void Split_ShouldKeepTaggedDollarQuote()
        {
            var parts = SqlStatementSplitter.Split(
                "DO $body$ BEGIN PERFORM 1; END $body$;\nSELECT 1;",
                "postgresql");
            Assert.Equal(2, parts.Count);
            Assert.Contains("$body$", parts[0]);
        }

        [Fact]
        public void Split_ShouldSupportGoCount()
        {
            var parts = SqlStatementSplitter.Split("SELECT 1\nGO 3\nSELECT 2\nGO", "sqlserver");
            Assert.Equal(4, parts.Count);
            Assert.Equal("SELECT 1", parts[0]);
            Assert.Equal("SELECT 2", parts[3]);
        }

        [Fact]
        public void Split_ShouldIgnoreGoInsideString()
        {
            var parts = SqlStatementSplitter.Split("INSERT INTO t VALUES ('\nGO\n');\nGO\nSELECT 1\nGO", "sqlserver");
            Assert.Equal(2, parts.Count);
            Assert.Contains("SELECT 1", parts[1]);
        }

        [Fact]
        public void Split_ShouldNestBlockComments()
        {
            var parts = SqlStatementSplitter.Split("/* a /* b */ c ; */\nSELECT 1;", "postgresql");
            Assert.Single(parts);
            Assert.Contains("SELECT 1", parts[0]);
        }

        [Fact]
        public void Split_ShouldSupportBracketIdentifiers()
        {
            var parts = SqlStatementSplitter.Split("CREATE TABLE [a;b] (x INT);\nSELECT 1;", "sqlite");
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Split_ShouldThrowOnUnclosedString()
        {
            Assert.Throws<InvalidOperationException>(() => SqlStatementSplitter.Split("SELECT 'abc;", "mysql"));
        }

        [Fact]
        public void Split_ShouldThrowOnUnbalancedBlock()
        {
            Assert.Throws<InvalidOperationException>(() => SqlStatementSplitter.Split("BEGIN SELECT 1;", "mysql"));
        }

        [Fact]
        public void Split_ShouldReportSplitErrorAsDiagnostic()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql>SELECT 'abc</rawSql>");

            var result = TestSupport.CompileWith("mysql", unit, false, null);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, x => x.Code == "E042");
        }
    }
}