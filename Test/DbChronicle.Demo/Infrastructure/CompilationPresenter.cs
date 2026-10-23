using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Model;

namespace DbChronicle.Demo.Infrastructure
{
    internal sealed class CompilationPresenter
    {
        private readonly ConsoleReporter _console;

        public CompilationPresenter(ConsoleReporter console)
        {
            _console = console;
        }

        public void Print(DbChronicleConfig config, IReadOnlyList<ChangeUnit> units, string title)
        {
            _console.Section(title);

            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments
            };

            foreach (var unit in units)
            {
                var model = options.IncrementalModify ? new StructureModelBuilder().Build(units, unit.Id) : null;
                var dialect = DialectFactory.Create(config.Database.Dialect, config.Database.Version,
                    new DialectContext { IncrementalModify = options.IncrementalModify });
                var result = new SchemaCompiler(dialect, options, model).Compile(unit);

                _console.Line("-- " + unit.Id);
                foreach (var statement in result.Statements)
                {
                    _console.Line("   " + statement.Text.Replace("\n", " ").Replace("\r", ""));
                }
                foreach (var diagnostic in result.Diagnostics)
                {
                    _console.Line("   " + diagnostic);
                }
            }
        }
    }
}
