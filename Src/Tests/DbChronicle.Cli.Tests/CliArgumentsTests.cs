using Xunit;

namespace DbChronicle.Cli.Tests
{
    public class CliArgumentsTests
    {
        [Fact]
        public void Parse_ShouldReadKeyValue()
        {
            var args = CliArguments.Parse(new[] { "--config", "DbchronicleConfig.xml", "--to-release", "20261022" });

            Assert.True(args.Has("config"));
            Assert.Equal("DbchronicleConfig.xml", args.Get("config", null));
            Assert.Equal("20261022", args.Get("to-release", null));
        }

        [Fact]
        public void Parse_ShouldTreatFlagWithoutValue_AsTrue()
        {
            var args = CliArguments.Parse(new[] { "--dry-run", "--restart" });

            Assert.True(args.Has("dry-run"));
            // 无值标记取不到字符串，Get 回退到 fallback
            Assert.Null(args.Get("dry-run", null));
            Assert.True(args.Has("restart"));
        }

        [Fact]
        public void Parse_ShouldNotSwallowNextFlag_AsValue()
        {
            var args = CliArguments.Parse(new[] { "--mark-applied", "--restart" });

            Assert.True(args.Has("mark-applied"));
            Assert.True(args.Has("restart"));
            Assert.Null(args.Get("mark-applied", null));
        }

        [Fact]
        public void Parse_ShouldReadShortFlag_AndPositionals()
        {
            var args = CliArguments.Parse(new[] { "-v", "apply", "extra" });

            Assert.True(args.Has("v"));
            Assert.Equal(new[] { "apply", "extra" }, args.Positionals);
        }

        [Fact]
        public void GetInt_ShouldParseOrFallback()
        {
            var args = CliArguments.Parse(new[] { "--n", "7", "--bad", "abc" });

            Assert.Equal(7, args.GetInt("n", 0));
            Assert.Equal(9, args.GetInt("bad", 9));
            Assert.Equal(9, args.GetInt("missing", 9));
        }
    }
}
