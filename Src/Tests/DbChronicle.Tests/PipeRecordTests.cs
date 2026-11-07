using System.Collections.Generic;
using System.IO;
using System.Text;
using System;

using DbChronicle.Piping;
using Xunit;

namespace DbChronicle.Tests
{
    public class PipeRecordTests
    {
        [Fact]
        public void RoundTrip_ShouldPreserveStatements()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteVersion();
                writer.WriteUnitBegin("20261029_100000_abc123", "20261029");
                writer.WriteStatement("ALTER TABLE `用户` ADD COLUMN `备注` VARCHAR(50) COMMENT '中文描述'");
                writer.WriteStatement("INSERT INTO `t` (`a`) VALUES ('第一行\n第二行')");
                writer.WriteDiagnostic("Error", "E001", "XML 解析失败：标签不匹配");
                writer.WriteUnitEnd();
                writer.WriteStreamEnd();
                writer.Flush();

                var records = ReadAll(stream);
                Assert.Equal(7, records.Count);

                Assert.Equal(PipeRecordType.Version, records[0].Type);
                Assert.Equal(PipeProtocol.VersionText, records[0].Fields[0]);

                Assert.Equal(PipeRecordType.UnitBegin, records[1].Type);
                Assert.Equal("20261029_100000_abc123", records[1].UnitId);
                Assert.Equal("20261029", records[1].Release);

                Assert.True(records[2].IsStatement);
                Assert.Contains("中文描述", records[2].Sql);

                Assert.True(records[3].IsStatement);
                Assert.Contains("第一行\n第二行", records[3].Sql);

                Assert.Equal(PipeRecordType.Diagnostic, records[4].Type);
                Assert.Equal("Error", records[4].Severity);
                Assert.Equal("E001", records[4].Code);
                Assert.Contains("标签不匹配", records[4].Message);

                Assert.Equal(PipeRecordType.UnitEnd, records[5].Type);
                Assert.Equal(PipeRecordType.StreamEnd, records[6].Type);
            }
        }

        [Fact]
        public void RawSql_ShouldBePlainTextWithoutWrapper()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteVersion();
                writer.WriteStatement("UPDATE t SET c = 'a;b' WHERE id = 1 -- 注释");
                writer.WriteStreamEnd();

                stream.Position = 0;
                var bytes = stream.ToArray();
                var text = Encoding.UTF8.GetString(bytes);
                Assert.Contains("UPDATE t SET c = 'a;b' WHERE id = 1 -- 注释", text);
                Assert.Equal(PipeProtocol.RecordSeparator, bytes[bytes.Length - 1]);

                var records = ReadAll(stream);
                Assert.Equal("UPDATE t SET c = 'a;b' WHERE id = 1 -- 注释", records[1].Sql);
            }
        }

        [Fact]
        public void MissingSeparator_ShouldThrow()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteVersion();
                writer.WriteStatement("ALTER TABLE t ADD COLUMN c INT");
                writer.Flush();

                var bytes = stream.ToArray();
                using (var truncated = new MemoryStream(bytes, 0, bytes.Length - 1))
                {
                    var reader = new PipeRecordReader(truncated);
                    reader.Read();
                    Assert.Throws<EndOfStreamException>(() => reader.Read());
                }
            }
        }

        [Fact]
        public void StatementBeforeVersion_ShouldThrow()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteStatement("SELECT 1");
                writer.Flush();

                stream.Position = 0;
                var reader = new PipeRecordReader(stream);
                Assert.Throws<InvalidDataException>(() => reader.Read());
            }
        }

        [Fact]
        public void UnsupportedVersion_ShouldThrow()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteVersion();
                writer.Flush();

                var bytes = stream.ToArray();
                var replaced = new byte[bytes.Length];
                Array.Copy(bytes, replaced, bytes.Length);
                replaced[3] = (byte)'9';

                using (var stream2 = new MemoryStream(replaced))
                {
                    var reader = new PipeRecordReader(stream2);
                    Assert.Throws<InvalidDataException>(() => reader.Read());
                }
            }
        }

        [Fact]
        public void SeparatorInContent_ShouldThrow()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new PipeRecordWriter(stream);
                writer.WriteVersion();
                var sql = "SELECT '" + (char)PipeProtocol.RecordSeparator + "'";
                Assert.Throws<InvalidOperationException>(() => writer.WriteStatement(sql));
            }
        }

        [Fact]
        public void StandardSqlShouldNotContainSeparators()
        {
            Assert.False(PipeProtocol.ContainsSeparator("ALTER TABLE `t` ADD COLUMN `c` VARCHAR(20) NULL"));
            Assert.False(PipeProtocol.ContainsSeparator("INSERT INTO t VALUES ('a\tb')"));
            Assert.False(PipeProtocol.ContainsSeparator("SELECT ';' , '中文'"));
            Assert.True(PipeProtocol.ContainsSeparator("SELECT '" + (char)PipeProtocol.FieldSeparator + "'"));
        }

        private static List<PipeRecord> ReadAll(MemoryStream stream)
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
