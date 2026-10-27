using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.State;
using DbChronicle.Models;
using DbChronicle.Parsing;

namespace DbChronicle.Embedded
{
    internal sealed class DefaultDbChronicle : IDbChronicle
    {
        private readonly IDbExecutor _executor;
        private readonly DbChronicleOptions _options;
        private readonly DbChronicleConfig _config;

        public DefaultDbChronicle(IDbExecutor executor, DbChronicleOptions options)
        {
            _executor = executor;
            _options = options;
            _config = options.Config ?? ConfigLoader.Load(options.ConfigPath);
        }

        public DbChronicleConfig Config
        {
            get { return _config; }
        }

        public IReadOnlyList<ChangeUnit> LoadUnits()
        {
            return new ChangeUnitLoader(_config).LoadAll();
        }

        public DbUpgradeResult DbUpgrade(DbUpgradeOptions options)
        {
            var units = LoadUnits();
            var upgradeOptions = options ?? new DbUpgradeOptions();

            var executor = new SchemaExecutor(_config, null, upgradeOptions.LockFactory);
            var applied = executor.ApplyWith(_executor, units, new ApplyOptions
            {
                ConnectionString = upgradeOptions.ConnectionString,
                AppliedBy = string.IsNullOrEmpty(upgradeOptions.AppliedBy)
                    ? Environment.UserName
                    : upgradeOptions.AppliedBy,
                DryRun = upgradeOptions.DryRun,
                Restart = upgradeOptions.Restart,
                MarkAppliedChangeId = upgradeOptions.MarkAppliedChangeId,
                ContinueOnError = upgradeOptions.ContinueOnError,
                LockTimeoutSeconds = upgradeOptions.LockTimeoutSeconds > 0
                    ? upgradeOptions.LockTimeoutSeconds
                    : _config.Options.LockTimeoutSeconds,
                Log = _options.Log
            });

            return new DbUpgradeResult
            {
                Success = applied.Success,
                AppliedCount = applied.AppliedCount,
                SkippedCount = applied.SkippedCount,
                Sql = applied.Sql,
                Message = applied.Message,
                ExitCode = applied.ExitCode,
                Failures = applied.Failures.ToList(),
                FailedCount = applied.FailedCount,
                Diagnostics = applied.Diagnostics.ToList()
            };
        }

        public string Compile(string changeId = null)
        {
            var units = LoadUnits();
            var targets = string.IsNullOrEmpty(changeId)
                ? units
                : units.Where(x => string.Equals(x.Id, changeId, StringComparison.OrdinalIgnoreCase)).ToList();

            var builder = new StringBuilder();
            var errors = new List<CompileDiagnostic>();
            foreach (var unit in targets)
            {
                var result = DbChronicleMigrator.CompileUnit(_config, units, unit);
                builder.AppendLine(result.Sql);
                foreach (var diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        errors.Add(diagnostic);
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "编译失败：" + string.Join("；", errors.Select(x => x.ToString())));
            }

            return builder.ToString();
        }

        public IReadOnlyList<CompileDiagnostic> Check()
        {
            return new ChangeChecker().Check(LoadUnits());
        }

        public IReadOnlyList<AppliedChange> Status()
        {
            return new VersionStore(_config).LoadAll();
        }

        public void Dispose()
        {
            // 执行器由宿主创建与释放，这里不接管其生命周期
}
}
}