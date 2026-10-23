using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DbChronicle.Compilation.Checking;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Configuration;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Domain.Model;
using DbChronicle.Runtime.Naming;
using DbChronicle.Parsing;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Cli
{
    public static class Commands
    {
        public static int Init(CliArguments args)
        {
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (File.Exists(path))
            {
                Console.WriteLine("配置已存在：" + path);
                return ExitCodes.Success;
            }

            var config = ConfigLoader.CreateDefault(path);

            var dialect = args.Get("dialect", null);
            if (!string.IsNullOrEmpty(dialect))
            {
                if (!DialectFactory.IsSupported(dialect))
                {
                    throw new CliFailureException(ExitCodes.Usage,
                        "不支持的方言：" + dialect + "（可选：" + string.Join(" / ", DialectFactory.SupportedDialects) + "）");
                }
                config.Database.Dialect = dialect.Trim().ToLowerInvariant();
                config.Database.Version = DefaultVersionOf(config.Database.Dialect);
            }

            var version = args.Get("version", null);
            if (!string.IsNullOrEmpty(version))
            {
                config.Database.Version = version;
            }

            ConfigLoader.Save(config, path);
            Directory.CreateDirectory(config.ResolveMigrationsPath());
            Directory.CreateDirectory(config.ResolveVersionLogPath());
            Directory.CreateDirectory(config.ResolveOutputPath());

            Console.WriteLine("已生成配置：" + path);
            Console.WriteLine("  方言：" + config.Database.Dialect + " " + config.Database.Version);
            Console.WriteLine("  migrations：" + config.ResolveMigrationsPath());
            Console.WriteLine("  version_log：" + config.ResolveVersionLogPath());
            Console.WriteLine("  output：" + config.ResolveOutputPath());
            return ExitCodes.Success;
        }

        public static int New(CliArguments args)
        {
            var config = LoadConfig(args);
            var description = args.Get("desc", null);
            if (string.IsNullOrEmpty(description))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --desc 参数");
            }

            var path = ChangeUnitFactory.Create(config.ResolveMigrationsPath(), description,
                args.Get("release", null), args.Get("author", null));
            Console.WriteLine(path);
            return ExitCodes.Success;
        }

        public static int Compile(CliArguments args)
        {
            var config = LoadConfig(args);
            var loader = new ChangeUnitLoader(config);
            var units = loader.LoadAll();
            if (units.Count == 0)
            {
                throw new CliFailureException(ExitCodes.Usage, "未找到变更单元：" + loader.MigrationsDirectory);
            }

            var allDialects = args.Has("all-dialects");
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            if (config.Options.IncludeRollback)
            {
                Console.WriteLine("warning W019: options.includeRollback 当前不生效，回滚请使用 db-chronicle rollback --to-release");
            }

            IReadOnlyList<string> dialects = allDialects
                ? DialectFactory.SupportedDialects
                : new[] { config.Database.Dialect };

            var outputDirectory = config.ResolveOutputPath();
            Directory.CreateDirectory(outputDirectory);
            var hasError = false;

            foreach (var dialectName in dialects)
            {
                var version = allDialects ? null : config.Database.Version;
                var dialect = DialectFactory.Create(dialectName, version,
                    new DialectContext { IncrementalModify = options.IncrementalModify });

                var builder = new StringBuilder();
                var dialectFailed = false;

                foreach (var unit in units)
                {
                    var model = options.IncrementalModify ? new StructureModelBuilder().Build(units, unit.Id) : null;
                    var result = new SchemaCompiler(dialect, options, model).Compile(unit);

                    PrintDiagnostics(result.Diagnostics);
                    if (result.HasErrors)
                    {
                        dialectFailed = true;
                        hasError = true;
                        continue;
                    }

                    builder.AppendLine(result.Sql);
                }

                var outputPath = Path.Combine(outputDirectory, dialectName + ".sql");
                File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(false));
                Console.WriteLine("已生成：" + outputPath + (dialectFailed ? "（含编译错误）" : ""));
            }

            return hasError ? ExitCodes.Execution : ExitCodes.Success;
        }

        public static int Check(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var diagnostics = new List<CompileDiagnostic>();
            foreach (var unit in units)
            {
                diagnostics.AddRange(unit.Diagnostics);
            }
            diagnostics.AddRange(new ChangeChecker().Check(units));

            PrintDiagnostics(diagnostics);

            var strict = args.Has("strict-warnings");
            var failed = diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error)
                || (strict && diagnostics.Any(x => x.Severity == DiagnosticSeverity.Warning));

            if (failed)
            {
                Console.Error.WriteLine("校验未通过（" + units.Count + " 个变更单元）");
                return ExitCodes.Usage;
            }

            Console.WriteLine("校验通过（" + units.Count + " 个变更单元）");
            return ExitCodes.Success;
        }

        public static int Apply(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var connectionString = ConnectionResolver.Resolve(config);
            var options = new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = ChangeUnitFactory.ResolveAuthor(),
                DryRun = args.Has("dry-run"),
                MarkAppliedChangeId = args.Get("mark-applied", null),
                Restart = args.Has("restart"),
                LockTimeoutSeconds = config.Options.LockTimeoutSeconds,
                Log = Console.WriteLine
            };

            SchemaExecutor executor;
            try
            {
                var factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
                executor = new SchemaExecutor(config, factory, factory);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            if (args.Has("check-drift"))
            {
                DriftReport drift;
                try
                {
                    drift = executor.CheckDrift(units, connectionString);
                }
                catch (Exception ex)
                {
                    throw new CliFailureException(ExitCodes.Environment, "结构比对失败：" + ex.Message);
                }

                foreach (var missing in drift.MissingColumns)
                {
                    Console.Error.WriteLine("drift: 模型中有、库中没有的列：" + missing);
                }
                foreach (var extra in drift.ExtraColumns)
                {
                    Console.WriteLine("info: 库中多出的列（允许）：" + extra);
                }
                foreach (var table in drift.UnreadableTables)
                {
                    Console.WriteLine("提示: 无法读取表结构：" + table);
                }

                if (drift.HasDrift)
                {
                    Console.Error.WriteLine("发现模型漂移：请核对后再执行 apply（手工改过库时需补基线或手工对齐）");
                    return ExitCodes.Usage;
                }

                Console.WriteLine("未发现模型漂移。");
                return ExitCodes.Success;
            }

            if (args.Has("backfill-logs"))
            {
                BackfillResult backfill;
                try
                {
                    backfill = executor.BackfillLogs(units, options);
                }
                catch (Exception ex)
                {
                    throw new CliFailureException(ExitCodes.Environment, "补生成日志失败：" + ex.Message);
                }

                if (!backfill.Success)
                {
                    Console.Error.WriteLine("error: " + backfill.Message);
                    return backfill.ExitCode ?? ExitCodes.Execution;
                }

                Console.WriteLine("补生成完成：新增 " + backfill.BackfilledCount + " 个，已存在 " + backfill.SkippedCount + " 个");
                return ExitCodes.Success;
            }

            ApplyResult result;
            try
            {
                result = executor.Apply(units, options);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "连接或执行环境异常：" + ex.Message);
            }

            PrintDiagnostics(result.Diagnostics);

            if (!result.Success)
            {
                Console.Error.WriteLine("error: " + result.Message);
                return result.ExitCode ?? ExitCodes.Execution;
            }

            Console.WriteLine("完成：应用 " + result.AppliedCount + " 个，跳过 " + result.SkippedCount + " 个");
            return ExitCodes.Success;
        }

        public static int Status(CliArguments args)
        {
            var config = LoadConfig(args);
            var connectionString = ConnectionResolver.Resolve(config);
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config, connectionString);

            var releaseFilter = args.Get("release", null);
            var current = applied
                .Where(x => !x.HasFailure)
                .Select(x => x.Release)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();

            var appliedIds = new HashSet<string>(applied.Where(x => !x.HasFailure).Select(x => x.ChangeId), StringComparer.OrdinalIgnoreCase);
            var pending = units.Where(x => !appliedIds.Contains(x.Id)
                && (releaseFilter == null || x.Release == releaseFilter)).ToList();
            var pendingRelease = pending
                .Select(x => x.Release)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();

            Console.WriteLine(releaseFilter == null
                ? "数据库当前版本：" + (current ?? "无（未初始化）")
                : "数据库当前版本（过滤 release=" + releaseFilter + "）：" + (current ?? "无（未初始化）"));

            if (pending.Count > 0)
            {
                Console.WriteLine("待应用版本：" + (pendingRelease ?? "无"));
            }

            Console.WriteLine();

            Console.WriteLine("已应用：");
            foreach (var record in applied.Where(x => releaseFilter == null || x.Release == releaseFilter))
            {
                Console.WriteLine((record.HasFailure ? "[!] " : "[x] ") + record.ChangeId + "  " + record.Release
                    + "  " + (record.HasFailure ? "failed at stmt " + record.FailureStatement : "applied " + record.AppliedAt)
                    + " by " + record.AppliedBy);
            }

            Console.WriteLine();
            Console.WriteLine("待应用：");
            foreach (var unit in pending)
            {
                Console.WriteLine("[ ] " + unit.Id + "  " + unit.Release + "  pending  " + (unit.Description ?? ""));
            }

            if (pending.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("提示：执行 db-chronicle apply 应用 " + pending.Count + " 个待应用的变更单元。");
                return ExitCodes.Pending;
            }

            Console.WriteLine();
            Console.WriteLine("数据库已是最新版本。");
            return ExitCodes.Success;
        }

        public static int Verify(CliArguments args)
        {
            var config = LoadConfig(args);
            var connectionString = ConnectionResolver.Resolve(config);
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config, connectionString);

            var map = new Dictionary<string, ChangeUnit>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in units)
            {
                map[unit.Id] = unit;
            }

            var errors = 0;

            foreach (var record in applied)
            {
                ChangeUnit unit;
                if (!map.TryGetValue(record.ChangeId, out unit))
                {
                    Console.Error.WriteLine("error: 已应用的变更单元文件丢失：" + record.ChangeId);
                    errors++;
                    continue;
                }

                if (string.Equals(record.Checksum, unit.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[x] " + record.ChangeId);
                    continue;
                }

                var normalized = ChecksumUtil.ComputeNormalizedFile(unit.SourcePath);
                if (string.Equals(record.Checksum, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[?] " + record.ChangeId + "  疑似仅换行或编码变动（规范化后一致）");
                    continue;
                }

                Console.Error.WriteLine("error: 已应用的变更单元内容被修改：" + record.ChangeId
                    + "（已应用=" + record.Checksum + "，当前=" + unit.Checksum + "）");
                errors++;
            }

            if (errors > 0)
            {
                return ExitCodes.Execution;
            }

            Console.WriteLine("checksum 校验通过（" + applied.Count + " 条记录）。");
            return ExitCodes.Success;
        }

        public static int Scan(CliArguments args)
        {
            var assemblyPath = args.Get("assembly", null);
            if (string.IsNullOrEmpty(assemblyPath))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --assembly 参数");
            }

            if (!File.Exists(assemblyPath))
            {
                throw new CliFailureException(ExitCodes.Usage, "程序集不存在：" + assemblyPath);
            }

            var output = args.Get("out", Path.Combine("migrations", "00000000_000000_baseline.xml"));
            var scanner = new DbChronicle.Scanning.CSharpScanner();

            DbChronicle.Scanning.ScanResult result;
            try
            {
                result = scanner.ScanAssembly(Path.GetFullPath(assemblyPath));
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "扫描失败：" + ex.Message);
            }

            if (result.Tables.Count == 0)
            {
                Console.Error.WriteLine("未识别到实体：需要 [Table] / [Entity] 特性，或继承名字含 Entity 的基类");
                return ExitCodes.Usage;
            }

            var xml = scanner.GenerateXml(result, ChangeUnitFactory.ResolveAuthor(), DateTime.Now.ToString("yyyy-MM-dd"));
            var outputPath = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(outputPath, xml, new UTF8Encoding(false));

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine("warning: " + warning);
            }

            Console.WriteLine("已生成基线草案：" + outputPath + "（" + result.Tables.Count + " 张表）");
            Console.WriteLine("提示：草案需人工确认后冻结，之后只写增量变更单元。");
            return ExitCodes.Success;
        }

        public static int Rollback(CliArguments args)
        {
            var config = LoadConfig(args);
            var target = args.Get("to-release", null);
            if (string.IsNullOrEmpty(target))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --to-release 参数");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            AdoNetExecutorFactory factory;
            try
            {
                factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            var service = new RollbackService(config, factory, factory);
            RollbackPlan plan;
            try
            {
                plan = service.Plan(units, target);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "生成回滚计划失败：" + ex.Message);
            }

            if (plan.Steps.Count == 0)
            {
                Console.WriteLine("没有需要回滚的变更单元（目标版本：" + target + "）");
                return ExitCodes.Success;
            }

            Console.Write(plan.ToSql());

            if (plan.HasBlockers)
            {
                foreach (var step in plan.Steps)
                {
                    foreach (var blocker in step.Blockers)
                    {
                        Console.Error.WriteLine("blocked: " + step.Unit.Id + "：" + blocker);
                    }
                }
                Console.Error.WriteLine("存在无法自动回滚的操作，已中止（请手工处理）");
                return ExitCodes.Usage;
            }

            if (!args.Has("apply"))
            {
                Console.WriteLine("（dry-run：确认无误后加 --apply 执行）");
                return ExitCodes.Success;
            }

            var connectionString = ConnectionResolver.Resolve(config);
            RollbackResult result;
            try
            {
                result = service.Execute(plan, connectionString);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "回滚执行异常：" + ex.Message);
            }

            if (!result.Success)
            {
                foreach (var blocker in result.Blockers)
                {
                    Console.Error.WriteLine("blocked: " + blocker);
                }
                Console.Error.WriteLine("error: " + result.Message);
                return result.ExitCode ?? ExitCodes.Execution;
            }

            Console.WriteLine("回滚完成：撤销 " + result.RolledBackCount + " 个变更单元");
            return ExitCodes.Success;
        }

        private static string DefaultVersionOf(string dialect)
        {
            switch (dialect)
            {
                case "mariadb":
                    return "10.5";
                case "postgresql":
                    return "14";
                case "sqlserver":
                    return "2019";
                case "sqlite":
                    return "3.35";
                default:
                    return "8.0";
            }
        }

        private static List<AppliedChange> LoadApplied(DbChronicleConfig config, string connectionString)
        {
            IDbExecutorFactory factory;
            try
            {
                factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            try
            {
                using (var executor = factory.Create(connectionString))
                {
                    var store = new VersionStore(config);
                    return store.LoadAll();
                }
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "读取版本表失败：" + ex.Message);
            }
        }

        private static DbChronicleConfig LoadConfig(CliArguments args)
        {
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (!File.Exists(path))
            {
                throw new CliFailureException(ExitCodes.Usage,
                    "配置文件不存在：" + Path.GetFullPath(path) + "（先执行 db-chronicle init）");
            }
            return ConfigLoader.Load(path);
        }

        private static void PrintDiagnostics(IEnumerable<CompileDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Console.Error.WriteLine(diagnostic.ToString());
                }
                else
                {
                    Console.WriteLine(diagnostic.ToString());
                }
            }
        }
    }
}
