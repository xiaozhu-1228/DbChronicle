using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DbChronicle.Compilation.Checking;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Domain.Model;
using DbChronicle.Parsing;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;

using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle
{
    /// <summary>
    /// 组件入口。宿主需提供 IDbExecutor 实例（核心库不含任何 ADO.NET 实现）。
    /// <code>
    /// var db = DbChronicleFactory.Create(myExecutor, o =>
    /// {
    ///     o.ConfigPath = "dbchronicle.config.xml";
    ///     o.Log = Console.WriteLine;
    /// });
    /// var result = db.DbUpgrade();
    /// </code>
    /// </summary>
    public static class DbChronicleFactory
    {
        public static IDbChronicle Create(IDbExecutor executor, Action<DbChronicleOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException("configure");
            }

            var options = new DbChronicleOptions();
            configure(options);
            return Create(executor, options);
        }

        public static IDbChronicle Create(IDbExecutor executor, DbChronicleOptions options)
        {
            if (executor == null)
            {
                throw new ArgumentNullException("executor",
                    "核心库不包含数据库驱动实现，请传入宿主自己的 IDbExecutor 实例");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (string.IsNullOrEmpty(options.ConfigPath))
            {
                throw new ArgumentException("必须提供 ConfigPath（启动配置文件）", "options");
            }

            return new DefaultDbChronicle(executor, options);
        }
    }

    internal sealed class DefaultDbChronicle : IDbChronicle
    {
        private readonly IDbExecutor _executor;
        private readonly DbChronicleOptions _options;
        private readonly DbChronicleConfig _config;

        public DefaultDbChronicle(IDbExecutor executor, DbChronicleOptions options)
        {
            _executor = executor;
            _options = options;
            _config = ConfigLoader.Load(options.ConfigPath);
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

            var executor = new SchemaExecutor(_config, null, null);
            var applied = executor.ApplyWith(_executor, units, new ApplyOptions
            {
                AppliedBy = string.IsNullOrEmpty(upgradeOptions.AppliedBy)
                    ? Environment.UserName
                    : upgradeOptions.AppliedBy,
                DryRun = upgradeOptions.DryRun,
                Log = _options.Log
            });

            return new DbUpgradeResult
            {
                Success = applied.Success,
                AppliedCount = applied.AppliedCount,
                SkippedCount = applied.SkippedCount,
                Message = applied.Message,
                ExitCode = applied.ExitCode,
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
            foreach (var unit in targets)
            {
                builder.AppendLine(DbChronicleMigrator.CompileUnit(_config, units, unit).Sql);
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
