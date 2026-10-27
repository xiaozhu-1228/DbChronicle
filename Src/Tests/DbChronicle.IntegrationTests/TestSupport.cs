using System.IO;
using System.Text;
using System;

using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Parsing;
using DbChronicle.Models;

namespace DbChronicle.IntegrationTests
{
    internal static class TestSupport
    {
        public static string CreateMigrationsDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dbchronicle-integration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static string WriteUnit(string directory, string changeId, string body)
        {
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"" + changeId + "\" author=\"tester\" release=\"" + changeId.Substring(0, 8) + "\">\n"
                + "  <description>test</description>\n"
                + body
                + "\n</changeUnit>\n";
            var fileName = changeId == "00000000_000000_baseline" ? changeId + ".xml" : changeId + "_test.xml";
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            return path;
        }

        public static CompileResult CompileWith(string dialectName, ChangeUnit unit, bool incrementalModify, StructureModel model)
        {
            var context = new DialectContext { IncrementalModify = incrementalModify };
            var dialect = DialectFactory.Create(dialectName, null, context);
            var compiler = new SchemaCompiler(dialect, new CompilerOptions { IncrementalModify = incrementalModify }, model);
            return compiler.Compile(unit);
        }

        /// <summary>返回一个尚不存在的临时文件路径（SQLite 等会自行创建）。</summary>
        public static string TempFile(string prefix, string extension)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dbchronicle-integration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, prefix + extension);
        }

        /// <summary>把 XML 文本落盘后交给解析器，用于验证扫描产物是否为合法变更单元。</summary>
        public static ChangeUnit ParseXml(string xml)
        {
            var directory = CreateMigrationsDirectory();
            var path = Path.Combine(directory, "00000000_000000_baseline.xml");
            File.WriteAllText(path, xml, new UTF8Encoding(false));

            var config = new DbChronicleConfig { BaseDirectory = directory };
            config.Database.Dialect = "sqlite";
            config.Options.IncrementalModify = true;
            config.Options.AllowDestructive = true;

            return new ChangeUnitParser(config).Parse(path);
        }
    }
}