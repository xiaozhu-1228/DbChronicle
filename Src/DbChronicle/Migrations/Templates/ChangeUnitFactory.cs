using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System;

namespace DbChronicle.Migrations.Templates
{
    public static class ChangeUnitFactory
    {
        public static string Create(string outputDirectory, string description, string release, string author)
        {
            if (string.IsNullOrEmpty(description))
            {
                throw new ArgumentException("变更描述不能为空", "description");
            }

            var releaseValue = string.IsNullOrEmpty(release)
                ? DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : release;
            var authorValue = string.IsNullOrEmpty(author) ? ResolveAuthor() : author;

            Directory.CreateDirectory(outputDirectory);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var fileName = BuildFileName(description);
                var path = Path.Combine(outputDirectory, fileName);
                var id = fileName.Substring(0, fileName.Length - ".xml".Length);
                var content = BuildTemplate(id, authorValue, releaseValue, description);

                try
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(content);
                    }

                    return path;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }

            throw new InvalidOperationException("连续 10 次生成文件名均失败，请检查 migrations 目录");
        }

        public static string GenerateUniqueFileName(string migrationsDirectory, string description)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var fileName = BuildFileName(description);
                if (!File.Exists(Path.Combine(migrationsDirectory, fileName)))
                {
                    return fileName;
                }
            }

            throw new InvalidOperationException("连续 10 次生成文件名均失败，请检查 migrations 目录");
        }

        private static string BuildFileName(string description)
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
                + "_" + GenerateRandomCode(6) + "_" + Slugify(description) + ".xml";
        }

        public static string Slugify(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "change";
            }

            var sb = new StringBuilder();
            foreach (var ch in text.Trim().ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    sb.Append(ch);
                }
                else if (ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }
                }
            }

            var result = sb.ToString().Trim('_');
            if (result.Length == 0)
            {
                result = "change";
            }
            if (result.Length > 40)
            {
                result = result.Substring(0, 40).Trim('_');
            }
            return result;
        }

        public static string GenerateRandomCode(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(length);
            foreach (var b in bytes)
            {
                sb.Append((b % 16).ToString("x"));
            }
            return sb.ToString();
        }

        public static string ResolveAuthor()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("DBCHRONICLE_AUTHOR");
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return fromEnvironment;
            }
            return Environment.UserName;
        }

        private static string BuildTemplate(string id, string author, string release, string description)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<!--\n");
            sb.Append("  本变更单元一旦应用（写入 version_log），文件即不可修改。\n");
            sb.Append("  修正错误请新增变更单元文件，并用 <depends on=\"本文件 id\"/> 声明依赖。\n");
            sb.Append("-->\n");
            sb.Append("<changeUnit id=\"").Append(id).Append("\" author=\"").Append(Escape(author))
              .Append("\" release=\"").Append(release).Append("\">\n");
            sb.Append("  <description>").Append(Escape(description)).Append("</description>\n");
            sb.Append('\n');
            sb.Append("  <table name=\"TODO\" action=\"ALTER\">\n");
            sb.Append("    <column name=\"TODO\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n");
            sb.Append("  </table>\n");
            sb.Append("</changeUnit>\n");
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}