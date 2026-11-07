using System.Globalization;
using System.Text;

namespace DbChronicle.Piping
{
    public static class PipeProtocol
    {
        public const byte RecordSeparator = 0x1E;

        public const byte FieldSeparator = 0x1F;

        public const byte ControlPrefix = 0x01;

        public const byte Version = 2;

        public const int MaxRecordBytes = 16 * 1024 * 1024;

        public const long MaxStreamBytes = 256L * 1024 * 1024;

        public static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static string VersionText
        {
            get { return Version.ToString(CultureInfo.InvariantCulture); }
        }

        public static bool ContainsSeparator(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (ch == (char)RecordSeparator || ch == (char)FieldSeparator || ch == (char)ControlPrefix)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
