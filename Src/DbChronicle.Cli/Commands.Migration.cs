using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Migrations.State;
using DbChronicle.Migrations.Templates;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Scanner;

namespace DbChronicle.Cli
{
    public static partial class Commands
    {
        public static int Apply(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var connectionString = ResolveConnection(config);
            var options = new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = ChangeUnitFactory.ResolveAuthor(),
                DryRun = args.Has("dry-run"),
                MarkAppliedChangeId = args.Get("mark-applied", null),
                Restart = args.Has("restart"),
                ContinueOnError = args.Has("continue-on-error") ? true : (bool?)null,
                LockTimeoutSeconds = config.Options.LockTimeoutSeconds,
                Log = Console.WriteLine
            };

            SchemaExecutor executor;
            try
            {
                var factory = Host.ResolveExecutor(config.Database.Dialect);
                executor = new SchemaExecutor(config, factory, Host.ResolveLocks(config.Database.Dialect));
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
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config);

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
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config);

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

        public static int Rollback(CliArguments args)
        {
            var config = LoadConfig(args);
            var target = args.Get("target-release", null) ?? args.Get("to-release", null);
            if (string.IsNullOrEmpty(target))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --target-release 参数");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            IDbExecutorFactory factory;
            try
            {
                factory = Host.ResolveExecutor(config.Database.Dialect);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            var service = new RollbackService(config, factory, Host.ResolveLocks(config.Database.Dialect));
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

            var connectionString = ResolveConnection(config);
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

    }
}