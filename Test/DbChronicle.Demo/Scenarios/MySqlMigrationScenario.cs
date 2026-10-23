using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Parsing;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Demo.Cli;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.MySql;
using DbChronicle.Demo.Xml;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Demo.Scenarios
{
    /// <summary>
    /// MySQL 对现网表做迭代：introspect → 生成基线 → 增量 → 编译 → apply → 状态 → 结构复核。
    /// </summary>
    internal sealed class MySqlMigrationScenario
    {
        private const string TargetTable = "abnor_msg";

        private readonly DemoOptions _options;
        private readonly ConsoleReporter _console;
        private readonly ChangeUnitXmlWriter _xmlWriter = new ChangeUnitXmlWriter();

        public MySqlMigrationScenario(DemoOptions options, ConsoleReporter console)
        {
            _options = options;
            _console = console;
        }

        public void Run()
        {
            var connectionString = MySqlConnectionStrings.Resolve();
            var workspace = DemoWorkspace.Create("mysql", false);

            _console.Section("1/8 初始化工程（MySQL / " + TargetTable + "）");
            var config = ConfigSource.Resolve(_options, workspace, _console, "mysql", "8.0", "demo", "mysql");
            _console.Line("模式：" + (_options.Apply ? "执行（--apply）" : "dry-run（只生成与打印，不落库）"));

            var factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);

            _console.Section("2/8 读取现有表结构（information_schema）");
            IReadOnlyList<ColumnSnapshot> snapshot;
            using (var db = factory.Create(connectionString))
            {
                snapshot = new MySqlTableIntrospector(db).Inspect(TargetTable);
            }

            if (snapshot.Count == 0)
            {
                _console.Line("表 " + TargetTable + " 不存在：基线将按示例结构创建");
            }
            else
            {
                foreach (var column in snapshot)
                {
                    _console.Line("  " + column);
                }
            }

            _console.Section("3/8 基线逻辑模型");
            if (_options.HasExternalConfig)
            {
                _console.Line("  使用 --config 指定目录中的基线（不重新生成）");
            }
            else
            {
                var baselinePath = Path.Combine(config.ResolveMigrationsPath(), BaselineXmlWriter.BaselineFileName);
                if (File.Exists(baselinePath))
                {
                    _console.Line("  复用已有基线（基线已冻结，不重新生成）：" + Path.GetFileName(baselinePath));
                }
                else
                {
                    baselinePath = new BaselineXmlWriter().Write(config, TargetTable, snapshot);
                    _console.Line("  " + Path.GetFileName(baselinePath));
                }
            }

            _console.Section("4/8 增量变更单元（ADD test1 / MODIFY msg → Text）");
            if (_options.HasExternalConfig)
            {
                _console.Line("  使用 --config 指定目录中的变更单元（不自动生成）");
            }
            else
            {
                var changePath = _xmlWriter.WriteMySqlAbnorMsgUnit(config);
                _console.Line("  " + Path.GetFileName(changePath));
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            new CompilationPresenter(_console).Print(config, units, "5/8 编译（增量模式：MODIFY 只写变化项）");

            if (snapshot.Any(x => string.Equals(x.Name, "test1", StringComparison.OrdinalIgnoreCase)))
            {
                _console.Section("6/8 test1 已存在：跳过 apply（演示已执行过）");
                _console.Line("如需重跑：先手工回退该表，或删除状态文件 " + new VersionStore(config).StatePath + "。");
                ShowState(factory, connectionString, config);
                _console.Line();
                _console.Line("演示目录：" + workspace.Root);
                return;
            }

            _console.Section("6/8 应用");
            var executor = new SchemaExecutor(config, factory, factory);
            var applyResult = executor.Apply(units, new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = "demo-mysql",
                DryRun = !_options.Apply,
                LockTimeoutSeconds = 10,
                Log = _console.Log
            });

            _console.Line("apply 结果：success=" + applyResult.Success
                + "，应用 " + applyResult.AppliedCount + "，跳过 " + applyResult.SkippedCount
                + (string.IsNullOrEmpty(applyResult.Message) ? "" : "，message=" + applyResult.Message));

            if (!applyResult.Success)
            {
                throw new InvalidOperationException(applyResult.Message);
            }

            if (!_options.Apply)
            {
                _console.Section("7/8 已跳过（dry-run）");
                _console.Line("确认 SQL 无误后，加 --apply 真正执行。");
                return;
            }

            ShowState(factory, connectionString, config);
            _console.Line();
            _console.Line("演示目录：" + workspace.Root);
        }

        private void ShowState(IDbExecutorFactory factory, string connectionString, DbChronicleConfig config)
        {
            _console.Section("7/8 版本托管状态");
            using (var db = factory.Create(connectionString))
            {
                foreach (var record in new VersionStore(config).LoadAll())
                {
                    _console.Line(record.ChangeId + "  release=" + record.Release
                        + "  appliedAt=" + record.AppliedAt + "  checksum=" + ConsoleReporter.Short(record.Checksum));
                }
            }

            _console.Section("8/8 最终结构（information_schema）");
            using (var db = factory.Create(connectionString))
            {
                foreach (var column in new MySqlTableIntrospector(db).Inspect(TargetTable))
                {
                    _console.Line("  " + column);
                }
            }
        }
    }
}
