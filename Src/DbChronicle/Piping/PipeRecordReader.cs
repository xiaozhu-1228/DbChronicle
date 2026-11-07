using System.IO;
using System;

namespace DbChronicle.Piping
{
    public sealed class PipeRecordReader
    {
        private readonly Stream _stream;

        private readonly byte[] _buffer = new byte[8192];

        private readonly MemoryStream _pending = new MemoryStream();

        private int _bufferLength;

        private int _bufferOffset;

        private long _read;

        private bool _versionSeen;

        public PipeRecordReader(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public PipeRecord Read()
        {
            for (;;)
            {
                while (_bufferOffset < _bufferLength)
                {
                    var value = _buffer[_bufferOffset];
                    _bufferOffset++;

                    if (value != PipeProtocol.RecordSeparator)
                    {
                        _pending.WriteByte(value);
                        if (_pending.Length > PipeProtocol.MaxRecordBytes)
                        {
                            throw new InvalidDataException("单条记录超过上限 " + PipeProtocol.MaxRecordBytes + " 字节");
                        }
                        continue;
                    }

                    var body = _pending.ToArray();
                    _pending.SetLength(0);
                    _pending.Position = 0;

                    if (body.Length == 0)
                    {
                        continue;
                    }

                    return Parse(body);
                }

                _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
                _bufferOffset = 0;

                if (_bufferLength > 0)
                {
                    _read += _bufferLength;
                    if (_read > PipeProtocol.MaxStreamBytes)
                    {
                        throw new InvalidDataException("流总量超过上限 " + PipeProtocol.MaxStreamBytes + " 字节");
                    }
                    continue;
                }

                if (_pending.Length > 0)
                {
                    throw new EndOfStreamException("记录未以分隔符结束（流被截断）");
                }

                return null;
            }
        }

        private PipeRecord Parse(byte[] body)
        {
            if (body[0] != PipeProtocol.ControlPrefix)
            {
                RequireVersion();
                return new PipeRecord(PipeRecordType.Statement, new[] { PipeProtocol.Utf8.GetString(body) });
            }

            var text = PipeProtocol.Utf8.GetString(body, 1, body.Length - 1);
            var parts = text.Split(new[] { (char)PipeProtocol.FieldSeparator });
            var fields = new string[Math.Max(0, parts.Length - 1)];
            if (fields.Length > 0)
            {
                Array.Copy(parts, 1, fields, 0, fields.Length);
            }

            switch (parts[0])
            {
                case "V":
                    if (fields.Length < 1 || fields[0] != PipeProtocol.VersionText)
                    {
                        throw new InvalidDataException("协议版本不支持");
                    }
                    _versionSeen = true;
                    return new PipeRecord(PipeRecordType.Version, fields);

                case "U":
                    RequireVersion();
                    return new PipeRecord(PipeRecordType.UnitBegin, fields);

                case "E":
                    RequireVersion();
                    return new PipeRecord(PipeRecordType.UnitEnd, fields);

                case "D":
                    RequireVersion();
                    return new PipeRecord(PipeRecordType.Diagnostic, fields);

                case "Z":
                    RequireVersion();
                    return new PipeRecord(PipeRecordType.StreamEnd, fields);

                default:
                    throw new InvalidDataException("未知控制记录：" + parts[0]);
            }
        }

        private void RequireVersion()
        {
            if (!_versionSeen)
            {
                throw new InvalidDataException("缺少版本记录");
            }
        }
    }
}
