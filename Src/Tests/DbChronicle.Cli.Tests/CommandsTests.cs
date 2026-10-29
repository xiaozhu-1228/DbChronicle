using System.IO;
using System.Text;
using System;

using DbChronicle.Configuration;
using DbChronicle.Migrations.State;
using Xunit;

namespace DbChronicle.Cli.Tests
{
    /// <summary>
    /// CLI 命令层冒烟：退出码映射与本地命令（status / verify / check）不连库的行为。
    /// </summary>
    public class CommandsTests
    {
        [Fact]
        public void Status_ShouldReturnPending_WhenUnitsNotApplied()
        {
            var directory = CreateProject();
            var output = Capture(() => Commands.Status(ArgsFor(directory)));

            Assert.Equal(ExitCodes.Pending, LastExit);
            Assert.Contains("待应用", output);
            Assert.Contains("20261023_100000_a1b2c3", output);
        }

        [Fact]
        public void Verify_ShouldPass_WhenNoAppliedRecords()
        {
            var directory = CreateProject();
            var output = Capture(() => Commands.Verify(ArgsFor(directory)));

            Assert.Equal(ExitCodes.Success, LastExit);
            Assert.Contains("checksum 校验通过", output);
        }

        [Fact]
        public void Check_ShouldPass_ForValidUnits()
        {
            var directory = CreateProject();
            var output = Capture(() => Commands.Check(ArgsFor(directory)));

            Assert.Equal(ExitCodes.Success, LastExit);
            Assert.Contains("校验通过", output);
        }

        [Fact]
        public void Check_ShouldFail_ForInvalidUnit()
        {
            var directory = CreateProject();
            var path = Path.Combine(directory, "migrations", "20261023_110000_b2c3d4_bad.xml");
            File.WriteAllText(path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261023_110000_b2c3d4\" author=\"tester\" release=\"20261023\">\n"
                + "  <description>bad</description>\n"
                + "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new UTF8Encoding(false));

            Capture(() => Commands.Check(ArgsFor(directory)));

            Assert.Equal(ExitCodes.Usage, LastExit);
        }

        [Fact]
        public void CompileSplit_ShouldEmitOneStatementPerLine()
        {
            var directory = CreateProject();
            var outputDirectory = Path.Combine(directory, "split-sql");

            var args = CliArguments.Parse(new[]
            {
                "--config", Path.Combine(directory, ConfigLoader.DefaultFileName),
                "--split", "--output-dir", outputDirectory
            });
            Capture(() => Commands.Compile(args));

            Assert.Equal(ExitCodes.Success, LastExit);
            var file = Path.Combine(outputDirectory, "20261023_100000_a1b2c3.mysql.sql");
            Assert.True(File.Exists(file));

            var lines = File.ReadAllLines(file);
            Assert.Single(lines);
            Assert.Contains("ADD COLUMN", lines[0]);
            Assert.False(lines[0].TrimEnd().EndsWith(";", StringComparison.Ordinal), "行尾不应保留分号");
            Assert.False(lines[0].Contains("\n"), "一条语句必须压成一行");
        }

        [Fact]
        public void Compile_ShouldApplyDialectOverride()
        {
            var directory = CreateProject();
            var outputDirectory = Path.Combine(directory, "pg-split");

            var args = CliArguments.Parse(new[]
            {
                "--config", Path.Combine(directory, ConfigLoader.DefaultFileName),
                "--split", "--output-dir", outputDirectory,
                "--dialect", "postgresql"
            });
            Capture(() => Commands.Compile(args));

            Assert.Equal(ExitCodes.Success, LastExit);

            var file = Path.Combine(outputDirectory, "20261023_100000_a1b2c3.postgresql.sql");
            Assert.True(File.Exists(file), "应按覆盖后的方言生成文件");
            Assert.Contains("\"", File.ReadAllText(file));
        }

        [Fact]
        public void CompilePending_ShouldSkipAppliedUnits()
        {
            var directory = CreateProject();
            var configPath = Path.Combine(directory, ConfigLoader.DefaultFileName);
            var config = ConfigLoader.Load(configPath);
            new VersionStore(config).MarkAppliedManually(
                "20261023_100000_a1b2c3", "20261023", "checksum", config.Database.Dialect, "tester", "add phone");

            var outputDirectory = Path.Combine(directory, "pending-sql");
            var args = CliArguments.Parse(new[]
            {
                "--config", configPath, "--pending", "--split", "--output-dir", outputDirectory
            });
            var output = Capture(() => Commands.Compile(args));

            Assert.Equal(ExitCodes.Success, LastExit);
            Assert.Contains("没有待应用", output);
            Assert.False(Directory.Exists(outputDirectory) && Directory.GetFiles(outputDirectory).Length > 0);
        }

        [Fact]
        public void CompilePending_ShouldEmitOnlyPendingUnits()
        {
            var directory = CreateProject();
            var configPath = Path.Combine(directory, ConfigLoader.DefaultFileName);
            var config = ConfigLoader.Load(configPath);

            // 再加一个单元，并把第一个标记为已应用
            File.WriteAllText(Path.Combine(directory, "migrations", "20261024_090000_b2c3d4_add_status.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261024_090000_b2c3d4\" author=\"tester\" release=\"20261024\">\n"
                + "  <description>add status</description>\n"
                + "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"status\" action=\"ADD\" type=\"Int32\" nullable=\"false\" defaultValue=\"0\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new UTF8Encoding(false));

            new VersionStore(config).MarkAppliedManually(
                "20261023_100000_a1b2c3", "20261023", "checksum", config.Database.Dialect, "tester", "add phone");

            var outputDirectory = Path.Combine(directory, "pending-only");
            var args = CliArguments.Parse(new[]
            {
                "--config", configPath, "--pending", "--split", "--output-dir", outputDirectory
            });
            var output = Capture(() => Commands.Compile(args));

            Assert.Equal(ExitCodes.Success, LastExit);
            Assert.Contains("待应用 1 个变更单元", output);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "20261024_090000_b2c3d4.mysql.sql")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "20261023_100000_a1b2c3.mysql.sql")));
        }

        [Fact]
        public void CompileWithChangeId_ShouldEmitSingleUnit()
        {
            var directory = CreateProject();
            var outputDirectory = Path.Combine(directory, "one-sql");
            var args = CliArguments.Parse(new[]
            {
                "--config", Path.Combine(directory, ConfigLoader.DefaultFileName),
                "--change-id", "20261023_100000_a1b2c3", "--split", "--out", outputDirectory
            });
            Capture(() => Commands.Compile(args));

            Assert.Equal(ExitCodes.Success, LastExit);
            Assert.Single(Directory.GetFiles(outputDirectory, "*.sql"));
            Assert.Single(File.ReadAllLines(Path.Combine(outputDirectory, "_units.txt")));
        }

        [Fact]
        public void CompileWithUnknownChangeId_ShouldFail()
        {
            var directory = CreateProject();
            var args = CliArguments.Parse(new[]
            {
                "--config", Path.Combine(directory, ConfigLoader.DefaultFileName),
                "--change-id", "20990101_000000_zzzzzz"
            });

            Assert.Throws<CliFailureException>(() => Commands.Compile(args));
        }

        private static int LastExit;

        private static string Capture(Func<int> action)
        {
            var original = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                LastExit = action();
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        private static CliArguments ArgsFor(string directory)
        {
            return CliArguments.Parse(new[] { "--config", Path.Combine(directory, ConfigLoader.DefaultFileName) });
        }

        /// <summary>构造临时工程：配置 + 一个合法变更单元。</summary>
        private static string CreateProject()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dbchronicle-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "migrations"));

            var config = new DbChronicleConfig { BaseDirectory = directory };
            ConfigLoader.Save(config, Path.Combine(directory, ConfigLoader.DefaultFileName));

            File.WriteAllText(Path.Combine(directory, "migrations", "20261023_100000_a1b2c3_add_phone.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261023_100000_a1b2c3\" author=\"tester\" release=\"20261023\">\n"
                + "  <description>add phone</description>\n"
                + "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new UTF8Encoding(false));

            return directory;
        }
    }
}