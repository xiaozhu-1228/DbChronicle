using System;
using System.IO;
using System.Linq;
using System.Text;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Model;
using DbChronicle.Parsing;

namespace DbChronicle.Tests
{
    internal static class TestSupport
    {
        public static DbChronicleConfig CreateConfig(bool incrementalModify)
        {
            var config = new DbChronicleConfig { BaseDirectory = Path.GetTempPath() };
            config.Database.Dialect = "mysql";
            config.Database.Version = "8.0";
            config.Options.IncrementalModify = incrementalModify;
            config.Options.AllowDestructive = true;
            return config;
        }

        public static string CreateMigrationsDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dbchronicle-tests", Guid.NewGuid().ToString("N"));
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

        public static ChangeUnit ParseSingle(string body, bool incrementalModify = true)
        {
            var directory = CreateMigrationsDirectory();
            var path = WriteUnit(directory, "20261022_100000_a1b2c3", body);
            return new ChangeUnitParser(CreateConfig(incrementalModify)).Parse(path);
        }

        public static CompileResult Compile(ChangeUnit unit, bool incrementalModify, StructureModel model)
        {
            return CompileWith("mysql", unit, incrementalModify, model);
        }

        public static CompileResult CompileWithOptions(ChangeUnit unit, CompilerOptions options)
        {
            var context = new DialectContext { IncrementalModify = options.IncrementalModify };
            var dialect = DialectFactory.Create("mysql", null, context);
            return new SchemaCompiler(dialect, options, null).Compile(unit);
        }

        public static CompileResult CompileWith(string dialectName, ChangeUnit unit, bool incrementalModify, StructureModel model)
        {
            var context = new DialectContext { IncrementalModify = incrementalModify };
            var dialect = DialectFactory.Create(dialectName, null, context);
            var compiler = new SchemaCompiler(dialect, new CompilerOptions { IncrementalModify = incrementalModify }, model);
            return compiler.Compile(unit);
        }

        public static string CompileSql(string body, bool incrementalModify = false)
        {
            var unit = ParseSingle(body, incrementalModify);
            return Compile(unit, incrementalModify, null).Sql;
        }

        public static bool HasError(ChangeUnit unit)
        {
            return unit.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error);
        }
    }
}
