using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
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
    }
}
