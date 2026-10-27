using System.Collections.Generic;
using System;

using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Models;
using DbChronicle.Parsing;

namespace DbChronicle.Migrations.Executor
{
    public static class DbChronicleMigrator
    {
        public static ApplyResult Run(MigratorOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            var config = ResolveConfig(options);
            var factory = options.ExecutorFactory;
            if (factory == null)
            {
                throw new InvalidOperationException(
                    "必须提供 ExecutorFactory（如 new AdoNetExecutorFactory(config.Database.Dialect)）");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            var executor = new SchemaExecutor(config, factory, options.LockFactory);

            return executor.Apply(units, new ApplyOptions
            {
                ConnectionString = options.ConnectionString,
                AppliedBy = string.IsNullOrEmpty(options.AppliedBy) ? Environment.UserName : options.AppliedBy,
                DryRun = options.DryRun,
                Restart = options.Restart,
                MarkAppliedChangeId = options.MarkAppliedChangeId,
                LockTimeoutSeconds = options.LockTimeoutSeconds > 0
                    ? options.LockTimeoutSeconds
                    : config.Options.LockTimeoutSeconds,
                Log = options.Log
            });
        }

        public static IReadOnlyList<ChangeUnit> LoadUnits(MigratorOptions options)
        {
            return new ChangeUnitLoader(ResolveConfig(options)).LoadAll();
        }

        public static CompileResult CompileUnit(DbChronicleConfig config, IReadOnlyList<ChangeUnit> units, ChangeUnit unit)
        {
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            var dialect = DialectFactory.Create(config.Database.Dialect, config.Database.Version,
                new DialectContext { IncrementalModify = options.IncrementalModify });
            var model = options.IncrementalModify ? new StructureModelBuilder().Build(units, unit.Id) : null;

            return new SchemaCompiler(dialect, options, model).Compile(unit);
        }

        public static DriftReport CheckDrift(MigratorOptions options)
        {
            var config = ResolveConfig(options);
            var factory = options.ExecutorFactory;
            if (factory == null)
            {
                throw new InvalidOperationException("必须提供 ExecutorFactory");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            return new SchemaExecutor(config, factory, options.LockFactory)
                .CheckDrift(units, options.ConnectionString);
        }

        public static BackfillResult BackfillLogs(MigratorOptions options)
        {
            var config = ResolveConfig(options);
            var factory = options.ExecutorFactory;
            if (factory == null)
            {
                throw new InvalidOperationException("必须提供 ExecutorFactory");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            return new SchemaExecutor(config, factory, options.LockFactory)
                .BackfillLogs(units, new ApplyOptions
                {
                    ConnectionString = options.ConnectionString,
                    AppliedBy = string.IsNullOrEmpty(options.AppliedBy) ? Environment.UserName : options.AppliedBy,
                    Log = options.Log
                });
        }

        private static DbChronicleConfig ResolveConfig(MigratorOptions options)
        {
            if (options.Config != null)
            {
                return options.Config;
            }

            if (!string.IsNullOrEmpty(options.ConfigPath))
            {
                return ConfigLoader.Load(options.ConfigPath);
            }

            throw new InvalidOperationException("必须提供 Config（代码构造）或 ConfigPath（指向 DbchronicleConfig.xml）");
        }
    }
}