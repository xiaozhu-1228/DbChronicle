using System;
using System.IO;
using System.Text;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Model;

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
    }
}
