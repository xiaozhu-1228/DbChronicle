using System.Security.Cryptography;
using System.Text;

namespace DbChronicle.Domain.Sql
{
    public sealed class SqlStatement
    {
        public int Index { get; set; }
        public string Text { get; set; } = "";
        public string Hash { get; set; } = "";

        public static string ComputeHash(string text)
        {
            var normalized = Normalize(text);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }
    }
}
