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
using DbChronicle.Compilation.Dialects;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Parsing;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Demo.Cli;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.Xml;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Demo.Scenarios
{
    /// <summary>
    /// SQLite 全流程演示（无需任何数据库）：init → 变更 → 编译 → apply → 状态 → 校验 → 回滚计划。
    /// </summary>
    internal sealed class SqliteScenario
    {
        private readonly DemoOptions _options;
        private readonly ConsoleReporter _console;
        private readonly ChangeUnitXmlWriter _xmlWriter = new ChangeUnitXmlWriter();

        public SqliteScenario(DemoOptions options, ConsoleReporter console)
        {
            _options = options;
            _console = console;
        }

        public void Run()
        {
            var workspace = DemoWorkspace.Create("sqlite", true);

            _console.Section("1/7 初始化工程");
            var config = ConfigSource.Resolve(_options, workspace, _console, "sqlite", "3.35", "demo", "sqlite");

            // SQLite 演示每次新建临时库，状态文件必须跟库一起放在临时工作区
            config.Paths.VersionLog = Path.Combine(workspace.Root, "version_log", "${environment}");

            _console.Section("2/7 变更单元");
            if (_options.HasExternalConfig)
            {
                _console.Line("  使用 --config 指定目录中的变更单元（不自动生成）");
            }
            else
            {
                foreach (var path in _xmlWriter.WriteSqliteScenarioUnits(config))
                {
                    _console.Line("  " + Path.GetFileName(path));
                }
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            _console.Line("已解析 " + units.Count + " 个变更单元");

            new CompilationPresenter(_console).Print(config, units, "3/7 编译（增量模式：只写 length，其余属性由累积模型补全）");

            _console.Section("4/7 应用到 SQLite（真实执行）");
            var databasePath = workspace.DatabaseFile("demo.db");
            var connectionString = "Data Source=" + databasePath;
            var factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
            var executor = new SchemaExecutor(config, factory, factory);

            var applyResult = executor.Apply(units, new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = "demo",
                LockTimeoutSeconds = 5,
                Log = _console.Log
            });
            _console.Line("apply 结果：success=" + applyResult.Success
                + "，应用 " + applyResult.AppliedCount + "，跳过 " + applyResult.SkippedCount
                + (string.IsNullOrEmpty(applyResult.Message) ? "" : "，message=" + applyResult.Message));

            _console.Section("5/7 查看版本托管状态");
            using (var db = factory.Create(connectionString))
            {
                foreach (var record in new VersionStore(config).LoadAll())
                {
                    _console.Line(record.ChangeId + "  release=" + record.Release
                        + "  appliedAt=" + record.AppliedAt
                        + "  checksum=" + ConsoleReporter.Short(record.Checksum)
                        + (record.HasFailure ? "  [!] 上次失败" : ""));
                }

                _console.Line();
                _console.Line("表结构：");
                foreach (var column in db.Query("PRAGMA table_info('nor_user')"))
                {
                    _console.Line("  " + column["name"] + "  " + column["type"]
                        + "  notnull=" + column["notnull"] + "  default=" + (column["dflt_value"] ?? "NULL"));
                }
            }

            _console.Section("6/7 校验 checksum 与模型漂移");
            var applied = new List<AppliedChange>();
            using (var db = factory.Create(connectionString))
            {
                applied.AddRange(new VersionStore(config).LoadAll());
            }

            foreach (var record in applied)
            {
                var unit = units.FirstOrDefault(x => x.Id == record.ChangeId);
                if (unit == null)
                {
                    _console.Line("[!] " + record.ChangeId + " 源文件丢失");
                    continue;
                }

                _console.Line((string.Equals(unit.Checksum, record.Checksum, StringComparison.OrdinalIgnoreCase) ? "[x] " : "[!] ")
                    + record.ChangeId);
            }

            var drift = executor.CheckDrift(units, connectionString);
            _console.Line("模型漂移：缺失列 " + drift.MissingColumns.Count + "，库中多出列 " + drift.ExtraColumns.Count);

            _console.Section("7/7 回滚计划（以 MySQL 方言演示 SQL 生成，不执行）");
            var mysqlDialect = DialectFactory.Create("mysql", "8.0", new DialectContext { IncrementalModify = true });
            var plan = new RollbackPlanner().Build(units, "00000000", mysqlDialect);
            _console.Line(plan.HasBlockers
                ? "存在不可逆操作，回滚计划被阻断"
                : "可回滚单元：" + plan.Steps.Count + " 个");
            _console.Line(plan.ToSql());

            var sqlitePlan = new RollbackPlanner().Build(units, "00000000",
                DialectFactory.Create("sqlite", "3.35", new DialectContext { IncrementalModify = true }));
            if (sqlitePlan.HasBlockers)
            {
                _console.Line("SQLite 回滚边界：");
                foreach (var blocker in sqlitePlan.Steps.SelectMany(x => x.Blockers))
                {
                    _console.Line("  blocked: " + blocker);
                }
            }

            _console.Line();
            _console.Line("演示目录：" + workspace.Root);
            _console.Line("审计日志：" + config.ResolveVersionLogPath());
        }
    }
}
