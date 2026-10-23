using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Parsing
{
    public sealed class ChangeUnitLoader
    {
        private readonly DbChronicleConfig _config;
        private readonly ChangeUnitParser _parser;

        public ChangeUnitLoader(DbChronicleConfig config)
        {
            _config = config;
            _parser = new ChangeUnitParser(config);
        }

        public ChangeUnitParser Parser
        {
            get { return _parser; }
        }

        public string MigrationsDirectory
        {
            get { return _config.ResolveMigrationsPath(); }
        }

        public IReadOnlyList<ChangeUnit> LoadAll()
        {
            var directory = MigrationsDirectory;
            var units = new List<ChangeUnit>();
            if (!Directory.Exists(directory))
            {
                return units;
            }

            foreach (var path in Directory.GetFiles(directory, "*.xml")
                .Where(x => !string.Equals(Path.GetFileName(x), ConfigLoader.DefaultFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                units.Add(_parser.Parse(path));
            }

            return Sort(units);
        }

        public ChangeUnit LoadOne(string path)
        {
            return _parser.Parse(path);
        }

        public IReadOnlyList<ChangeUnit> LoadPending(string currentChangeId)
        {
            var units = LoadAll();
            if (string.IsNullOrEmpty(currentChangeId))
            {
                return units;
            }

            var result = new List<ChangeUnit>();
            var found = false;
            foreach (var unit in units)
            {
                if (found)
                {
                    result.Add(unit);
                }
                if (string.Equals(unit.Id, currentChangeId, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                }
            }
            return result;
        }

        public StructureModel BuildModel(IReadOnlyList<ChangeUnit> units, string beforeChangeId)
        {
            return new StructureModelBuilder().Build(units, beforeChangeId);
        }

        public static IReadOnlyList<ChangeUnit> Sort(IEnumerable<ChangeUnit> units)
        {
            return units
                .OrderBy(x => x.Release ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
