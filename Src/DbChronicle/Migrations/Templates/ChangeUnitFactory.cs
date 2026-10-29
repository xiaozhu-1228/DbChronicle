using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
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

            var authorValue = string.IsNullOrEmpty(author) ? ResolveAuthor() : author;

            Directory.CreateDirectory(outputDirectory);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var fileName = BuildFileName(DateTime.Now);
                var path = Path.Combine(outputDirectory, fileName);
                var id = fileName.Substring(0, fileName.Length - ".xml".Length);
                var releaseValue = string.IsNullOrEmpty(release) ? id.Substring(0, 8) : release;
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
                    Thread.Sleep(1000 - DateTime.Now.Millisecond + 1);
                }
            }

            throw new InvalidOperationException("连续 10 次生成文件名均失败，请检查 migrations 目录");
        }

        private static string BuildFileName(DateTime now)
        {
            return now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xml";
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