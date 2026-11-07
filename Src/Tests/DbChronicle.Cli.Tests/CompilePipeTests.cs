using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Configuration;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Piping;
using Xunit;

namespace DbChronicle.Cli.Tests
{
    public class CompilePipeTests
    {
        [Fact]
        public void Pipe_ShouldEmitOrderedRecords_AndWriteNoFiles()
        {
            var directory = CommandsTests.CreateProject();
            var config = ConfigLoader.Load(Path.Combine(directory, ConfigLoader.DefaultFileName));
            var units = new ChangeUnitLoader(config).LoadAll();
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            using (var stream = new MemoryStream())
            {
                var exit = Commands.WriteCompiledRecords(config, units, units, options, stream);
                Assert.Equal(ExitCodes.Success, exit);

                var records = ReadRecords(stream);
                Assert.Equal(PipeRecordType.Version, records[0].Type);
                Assert.Equal(PipeRecordType.UnitBegin, records[1].Type);
                Assert.Equal("20261023_100000_a1b2c3", records[1].UnitId);
                Assert.Equal("20261023", records[1].Release);
                Assert.Equal(PipeRecordType.UnitEnd, records[records.Count - 2].Type);
                Assert.Equal(PipeRecordType.StreamEnd, records[records.Count - 1].Type);

                var statements = records.Where(x => x.IsStatement).ToList();
                Assert.NotEmpty(statements);
                Assert.Contains(statements, x => x.Sql.Contains("ADD COLUMN"));
                Assert.DoesNotContain(records, x => x.Type == PipeRecordType.Diagnostic);
                Assert.All(statements, x => Assert.False(x.Sql.EndsWith(";", StringComparison.Ordinal)));
                Assert.All(statements, x => Assert.Equal(x.Sql, x.Sql.Trim()));
            }

            Assert.False(Directory.Exists(Path.Combine(directory, "output")), "管道模式不得创建输出目录");
            Assert.False(Directory.Exists(Path.Combine(directory, ".compile_tmp")), "管道模式不得写临时目录");
            Assert.Empty(Directory.GetFiles(directory, "*.sql", SearchOption.AllDirectories));
        }

        [Fact]
        public void Pipe_ShouldEmitDiagnostic_ForInvalidUnit()
        {
            var directory = CommandsTests.CreateProject();
            File.WriteAllText(Path.Combine(directory, "migrations", "20261023_110000_b2c3d4_bad.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261023_110000_b2c3d4\" author=\"tester\" release=\"20261023\">\n"
                + "  <description>bad</description>\n"
                + "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new UTF8Encoding(false));

            var config = ConfigLoader.Load(Path.Combine(directory, ConfigLoader.DefaultFileName));
            var units = new ChangeUnitLoader(config).LoadAll();
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments
            };

            using (var stream = new MemoryStream())
            {
                var exit = Commands.WriteCompiledRecords(config, units, units, options, stream);
                Assert.Equal(ExitCodes.Execution, exit);

                var records = ReadRecords(stream);
                Assert.Equal(PipeRecordType.StreamEnd, records[records.Count - 1].Type);
                var diagnostic = Assert.Single(records, x => x.Type == PipeRecordType.Diagnostic);
                Assert.Equal("Error", diagnostic.Severity);
                Assert.False(string.IsNullOrEmpty(diagnostic.Code));
            }
        }

        [Fact]
        public void Pipe_ShouldNotLeakDiagnosticsIntoStream()
        {
            var directory = CommandsTests.CreateProject();
            File.WriteAllText(Path.Combine(directory, "migrations", "20261024_100000_c3d4e5_warn.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"20261024_100000_c3d4e5\" author=\"tester\" release=\"20261024\">\n"
                + "  <description>warn</description>\n"
                + "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"missing\" action=\"MODIFY\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "</changeUnit>\n",
                new UTF8Encoding(false));

            var config = ConfigLoader.Load(Path.Combine(directory, ConfigLoader.DefaultFileName));
            var units = new ChangeUnitLoader(config).LoadAll();
            var options = new CompilerOptions { IncrementalModify = config.Options.IncrementalModify };

            using (var stream = new MemoryStream())
            {
                Commands.WriteCompiledRecords(config, units, units, options, stream);
                var records = ReadRecords(stream);
                Assert.All(records.Where(x => x.IsStatement),
                    x => Assert.DoesNotContain("warning ", x.Sql, StringComparison.OrdinalIgnoreCase));
                Assert.All(records.Where(x => x.IsStatement),
                    x => Assert.DoesNotContain("error ", x.Sql, StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void Pipe_ShouldEmitVersionAndStreamEnd_WhenNoUnitSelected()
        {
            var directory = CommandsTests.CreateProject();
            var config = ConfigLoader.Load(Path.Combine(directory, ConfigLoader.DefaultFileName));
            var units = new ChangeUnitLoader(config).LoadAll();
            var options = new CompilerOptions { IncrementalModify = config.Options.IncrementalModify };

            using (var stream = new MemoryStream())
            {
                // --since 过滤到 0 个单元时，流也必须完整（V + Z），否则调用方会判定截断
                var exit = Commands.WriteCompiledRecords(config, units, new List<ChangeUnit>(), options, stream);
                Assert.Equal(ExitCodes.Success, exit);

                var records = ReadRecords(stream);
                Assert.Equal(2, records.Count);
                Assert.Equal(PipeRecordType.Version, records[0].Type);
                Assert.Equal(PipeRecordType.StreamEnd, records[1].Type);
            }
        }

        private static List<PipeRecord> ReadRecords(MemoryStream stream)
        {
            stream.Position = 0;
            var reader = new PipeRecordReader(stream);
            var records = new List<PipeRecord>();
            while (true)
            {
                var record = reader.Read();
                if (record == null)
                {
                    return records;
                }
                records.Add(record);
            }
        }
    }
}
