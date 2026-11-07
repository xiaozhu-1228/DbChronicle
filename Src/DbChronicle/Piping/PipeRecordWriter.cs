using System.IO;
using System.Text;
using System;

namespace DbChronicle.Piping
{
    public sealed class PipeRecordWriter
    {
        private readonly Stream _stream;

        private long _written;

        public PipeRecordWriter(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public long BytesWritten
        {
            get { return _written; }
        }

        public void WriteVersion()
        {
            WriteRecord(Control("V", PipeProtocol.VersionText), false);
        }

        public void WriteUnitBegin(string unitId, string release)
        {
            WriteRecord(Control("U", unitId ?? "", release ?? ""), false);
        }

        public void WriteStatement(string sql)
        {
            WriteRecord(sql ?? "", true);
        }

        public void WriteUnitEnd()
        {
            WriteRecord(Control("E"), false);
        }

        public void WriteDiagnostic(string severity, string code, string message)
        {
            WriteRecord(Control("D", severity ?? "", code ?? "", message ?? ""), false);
        }

        public void WriteStreamEnd()
        {
            WriteRecord(Control("Z"), false);
        }

        public void Flush()
        {
            _stream.Flush();
        }

        private static string Control(string type, params string[] fields)
        {
            var builder = new StringBuilder();
            builder.Append((char)PipeProtocol.ControlPrefix).Append(type);
            foreach (var field in fields)
            {
                builder.Append((char)PipeProtocol.FieldSeparator).Append(field ?? "");
            }

            return builder.ToString();
        }

        private void WriteRecord(string text, bool checkSeparators)
        {
            if (checkSeparators && PipeProtocol.ContainsSeparator(text))
            {
                throw new InvalidOperationException("内容包含协议分隔符（0x1E / 0x1F / 0x01），无法写入记录");
            }

            var body = PipeProtocol.Utf8.GetBytes(text);
            if (body.Length > PipeProtocol.MaxRecordBytes)
            {
                throw new InvalidOperationException("单条记录超过上限 " + PipeProtocol.MaxRecordBytes + " 字节");
            }

            if (_written + body.Length + 1 > PipeProtocol.MaxStreamBytes)
            {
                throw new InvalidOperationException("流总量超过上限 " + PipeProtocol.MaxStreamBytes + " 字节");
            }

            if (body.Length > 0)
            {
                _stream.Write(body, 0, body.Length);
            }

            _stream.WriteByte(PipeProtocol.RecordSeparator);
            _written += body.Length + 1;
        }
    }
}
