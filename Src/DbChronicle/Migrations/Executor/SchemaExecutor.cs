using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.State;
using DbChronicle.Models;
using DbChronicle.Parsing;

namespace DbChronicle.Migrations.Executor
{
    public sealed class SchemaExecutor
    {
        private readonly DbChronicleConfig _config;
        private readonly IDbExecutorFactory _executorFactory;
        private readonly IDbLockFactory _lockFactory;

        public SchemaExecutor(DbChronicleConfig config, IDbExecutorFactory executorFactory, IDbLockFactory lockFactory)
        {
            _config = config;
            _executorFactory = executorFactory;
            _lockFactory = lockFactory;
        }

        public ApplyResult Apply(IReadOnlyList<ChangeUnit> units, ApplyOptions options)
        {
            using (var executor = _executorFactory.Create(options.ConnectionString))
            {
                return ApplyWith(executor, units, options);
            }
        }

        /// <summary>
        /// 使用宿主提供的执行器实例执行（不创建、不释放连接）。
        /// 核心库不包含任何数据库驱动实现，执行器由调用方注入。
        /// </summary>
        public ApplyResult ApplyWith(IDbExecutor executor, IReadOnlyList<ChangeUnit> units, ApplyOptions options)
        {
            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            var result = new ApplyResult();
            var ordered = ChangeUnitLoader.Sort(units);
            var dialectContext = new DialectContext { IncrementalModify = _config.Options.IncrementalModify };
            var dialect = DialectFactory.Create(_config.Database.Dialect, _config.Database.Version, dialectContext);
            var compilerOptions = new CompilerOptions
            {
                IncrementalModify = _config.Options.IncrementalModify,
                IncludeComments = _config.Options.IncludeComments
            };

            {
                if (!IsDialectCompatible(executor.DialectName, _config.Database.Dialect))
                {
                    result.Message = "执行器方言（" + executor.DialectName + "）与配置方言（"
                        + _config.Database.Dialect + "）不一致，拒绝执行";
                    result.ExitCode = 5;
                    return result;
                }

                IDbLock dbLock = null;
                try
                {
                    if (_lockFactory != null && !options.DryRun && !string.IsNullOrEmpty(options.ConnectionString))
                    {
                        dbLock = _lockFactory.Create(options.ConnectionString);
                        var timeout = TimeSpan.FromSeconds(Math.Max(0, options.LockTimeoutSeconds));
                        if (!dbLock.TryAcquire(timeout))
                        {
                            result.Message = "另一进程正在执行 apply（无法获取库级锁）";
                            result.ExitCode = 4;
                            return result;
                        }
                    }

                    var store = new VersionStore(_config);
                    List<AppliedChange> applied;
                    if (options.DryRun)
                    {
                        try
                        {
                            applied = store.LoadAll();
                        }
                        catch
                        {
                            applied = new List<AppliedChange>();
                        }
                    }
                    else
                    {
                        store.EnsureTable();
                        applied = store.LoadAll();
                    }

                    if (!string.IsNullOrEmpty(options.MarkAppliedChangeId))
                    {
                        return MarkApplied(store, ordered, options);
                    }

                    var knownIds = new HashSet<string>(ordered.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                    var missingFiles = applied
                        .Where(x => !x.HasFailure && !knownIds.Contains(x.ChangeId))
                        .Select(x => x.ChangeId)
                        .ToList();
                    if (missingFiles.Count > 0)
                    {
                        result.Message = "已应用的变更单元文件丢失：" + string.Join(", ", missingFiles)
                            + "（请恢复文件，或按补丁规范新增变更单元）";
                        result.ExitCode = 3;
                        return result;
                    }

                    foreach (var unit in ordered)
                    {
                        var record = applied.FirstOrDefault(x => string.Equals(x.ChangeId, unit.Id, StringComparison.OrdinalIgnoreCase));
                        var skip = false;
                        if (record != null)
                        {
                            if (!string.Equals(record.Checksum, unit.Checksum, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Message = "已应用的变更单元文件被修改（checksum 不一致）：" + unit.Id
                                    + "；已应用=" + record.Checksum + "，当前=" + unit.Checksum;
                                result.ExitCode = 3;
                                return result;
                            }

                            skip = !record.HasFailure;
                        }

                        if (!skip && !PrepareWildcardTables(executor, unit, compilerOptions, options, out var wildcardMessage))
                        {
                            result.Message = wildcardMessage;
                            result.ExitCode = 5;
                            return result;
                        }

                        var model = compilerOptions.IncrementalModify
                            ? new StructureModelBuilder().Build(ordered, unit.Id)
                            : null;
                        var compiler = new SchemaCompiler(dialect, compilerOptions, model);
                        var compiled = compiler.Compile(unit);
                        result.Sql += compiled.Sql;
                        result.Diagnostics.AddRange(compiled.Diagnostics);

                        if (skip)
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        if (compiled.HasErrors)
                        {
                            result.Message = "变更单元编译失败：" + unit.Id;
                            result.ExitCode = 1;
                            return result;
                        }

                        options.WriteLog("  SQL：" + PreviewSql(compiled, dialect));

                        if (options.DryRun)
                        {
                            options.WriteLog(compiled.Sql);
                            continue;
                        }

                        List<string> unitFailures;
                        try
                        {
                            unitFailures = ExecuteUnit(executor, store, unit, compiled, options);
                        }
                        catch (StatementExecutionException ex)
                        {
                            result.Message = "执行失败：" + unit.Id + "；" + ex.Describe();
                            result.ExitCode = 3;
                            return result;
                        }
                        catch (Exception ex)
                        {
                            result.Message = "执行失败：" + unit.Id + "：" + ex.Message;
                            result.ExitCode = 3;
                            return result;
                        }

                        if (unitFailures != null && unitFailures.Count > 0)
                        {
                            result.FailedCount++;
                            foreach (var failure in unitFailures)
                            {
                                result.Failures.Add(unit.Id + " / " + failure);
                            }

                            continue;
                        }

                        result.AppliedCount++;
                        WriteAuditLog(unit, compiled, options);
                    }

                    result.Success = result.FailedCount == 0;
                    if (!result.Success)
                    {
                        result.ExitCode = 3;
                        result.Message = "部分失败：" + result.FailedCount + " 个单元（failures 含明细）";
                    }
                    return result;
                }
                finally
                {
                    if (dbLock != null)
                    {
                        dbLock.Dispose();
                    }
                }
            }
        }

        public DriftReport CheckDrift(IReadOnlyList<ChangeUnit> units, string connectionString)
        {
            var ordered = ChangeUnitLoader.Sort(units);
            var model = new StructureModelBuilder().Build(ordered, null);

            using (var executor = _executorFactory.Create(connectionString))
            {
                return SchemaIntrospector.Compare(executor, model);
            }
        }

        public BackfillResult BackfillLogs(IReadOnlyList<ChangeUnit> units, ApplyOptions options)
        {
            var result = new BackfillResult();
            var ordered = ChangeUnitLoader.Sort(units);
            var directory = _config.ResolveVersionLogPath();
            if (string.IsNullOrEmpty(directory))
            {
                result.Message = "未配置 version_log 目录";
                result.ExitCode = 1;
                return result;
            }

            var dialect = DialectFactory.Create(_config.Database.Dialect, _config.Database.Version,
                new DialectContext { IncrementalModify = _config.Options.IncrementalModify });
            var compilerOptions = new CompilerOptions
            {
                IncrementalModify = _config.Options.IncrementalModify,
                IncludeComments = _config.Options.IncludeComments
            };

            using (var executor = _executorFactory.Create(options.ConnectionString))
            {
                var store = new VersionStore(_config);
                List<AppliedChange> applied;
                try
                {
                    applied = store.LoadAll();
                }
                catch (Exception ex)
                {
                    result.Message = "读取版本表失败：" + ex.Message;
                    result.ExitCode = 5;
                    return result;
                }

                var writer = new VersionLogWriter(directory);
                foreach (var record in applied)
                {
                    if (record.HasFailure)
                    {
                        continue;
                    }

                    var unit = ordered.FirstOrDefault(x => string.Equals(x.Id, record.ChangeId, StringComparison.OrdinalIgnoreCase));
                    if (unit == null)
                    {
                        options.WriteLog("警告：源文件缺失，无法补生成：" + record.ChangeId);
                        result.SkippedCount++;
                        continue;
                    }

                    var release = unit.Release ?? "unknown";
                    var mdPath = Path.Combine(directory, release + "_dbupdate.md");
                    var snapshotPath = Path.Combine(directory, "sql", release, unit.Id + "." + _config.Database.Dialect + ".sql");
                    var mdHasEntry = File.Exists(mdPath) && File.ReadAllText(mdPath).Contains(unit.Id);
                    if (mdHasEntry && File.Exists(snapshotPath))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var model = compilerOptions.IncrementalModify
                        ? new StructureModelBuilder().Build(ordered, unit.Id)
                        : null;
                    var compiled = new SchemaCompiler(dialect, compilerOptions, model).Compile(unit);
                    if (compiled.HasErrors)
                    {
                        options.WriteLog("警告：补生成失败（编译有错误）：" + unit.Id);
                        result.SkippedCount++;
                        continue;
                    }

                    var snapshot = writer.WriteSqlSnapshot(unit, compiled, _config.Database.Dialect);
                    writer.AppendEntry(unit, compiled, record.AppliedBy,
                        _config.Database.Dialect + " " + _config.Database.Version, snapshot, _config.Database.Dialect, true);

                    result.BackfilledCount++;
                    options.WriteLog("已补生成：" + unit.Id);
                }
            }

            result.Success = true;
            return result;
        }

        private static bool PrepareWildcardTables(IDbExecutor executor, ChangeUnit unit,
            CompilerOptions compilerOptions, ApplyOptions options, out string message)
        {
            message = null;
            var hasWildcard = unit.Operations.Any(o =>
                string.Equals((o.Table ?? "").Trim(), SchemaCompiler.WildcardTable, StringComparison.Ordinal));
            if (!hasWildcard)
            {
                return true;
            }

            List<string> tables;
            try
            {
                tables = SchemaIntrospector.ListTables(executor).ToList();
            }
            catch (Exception ex)
            {
                message = "读取表清单失败（table=\"*\" 需要数据库连接）：" + ex.Message;
                return false;
            }

            compilerOptions.WildcardTables = tables;
            return true;
        }

        private static bool IsDialectCompatible(string executorDialect, string configuredDialect)
        {
            var a = (executorDialect ?? "").Trim().ToLowerInvariant();
            var b = (configuredDialect ?? "").Trim().ToLowerInvariant();
            if (a == b)
            {
                return true;
            }

            return (a == "mysql" && b == "mariadb") || (a == "mariadb" && b == "mysql");
        }

        private ApplyResult MarkApplied(VersionStore store, IReadOnlyList<ChangeUnit> units, ApplyOptions options)
        {
            var result = new ApplyResult();
            var unit = units.FirstOrDefault(x => string.Equals(x.Id, options.MarkAppliedChangeId, StringComparison.OrdinalIgnoreCase));
            if (unit == null)
            {
                result.Message = "未找到变更单元：" + options.MarkAppliedChangeId;
                return result;
            }

            store.MarkAppliedManually(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect,
                options.AppliedBy, unit.Description);
            result.Success = true;
            result.AppliedCount = 1;
            result.Message = "已标记为已应用：" + unit.Id;
            return result;
        }

        private List<string> ExecuteUnit(IDbExecutor executor, VersionStore store, ChangeUnit unit, CompileResult compiled, ApplyOptions options)
        {
            var continueOnError = options.ContinueOnError ?? !executor.SupportsTransactionalDdl;

            if (executor.SupportsTransactionalDdl && IsTableScopedCommit())
            {
                return ExecuteByTable(executor, store, unit, compiled, options, continueOnError);
            }

            if (executor.SupportsTransactionalDdl)
            {
                executor.BeginTransaction();
                try
                {
                    foreach (var group in compiled.Groups)
                    {
                        var inGroup = 0;
                        foreach (var statement in group.Statements)
                        {
                            try
                            {
                                executor.Execute(statement.Text);
                            }
                            catch (Exception ex)
                            {
                                throw new StatementExecutionException(group.Table, statement.Index, inGroup,
                                    statement.Text, 0, compiled.Statements.Count, ex);
                            }
                            inGroup++;
                        }
                    }

                    store.Begin(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect, options.AppliedBy, unit.Description);
                    store.MarkApplied(unit.Id);
                    executor.Commit();
                }
                catch
                {
                    TryRollback(executor);
                    TryDeleteRecord(store, unit.Id);
                    throw;
                }
                return null;
            }

            store.EnsureStatementTable();
            var appliedStatements = options.Restart
                ? new Dictionary<int, string>()
                : store.LoadStatements(unit.Id);
            if (options.Restart)
            {
                store.ClearStatements(unit.Id);
            }

            store.Begin(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect, options.AppliedBy, unit.Description);

            var index = 0;
            var completedStatements = appliedStatements.Count;
            var executedTables = 0;
            var skippedTables = 0;
            var failures = new List<string>();
            try
            {
                foreach (var group in compiled.Groups)
                {
                    var inGroup = 0;
                    var groupFailed = false;
                    var groupExecuted = false;
                    foreach (var statement in group.Statements)
                    {
                        index = statement.Index;

                        string previousHash;
                        if (appliedStatements.TryGetValue(statement.Index, out previousHash))
                        {
                            if (!string.Equals(previousHash, statement.Hash, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException("语句 " + statement.Index
                                    + " 与上次执行的编译结果不一致，拒绝续跑（确需重跑请用 --restart）");
                            }
                            inGroup++;
                            continue;
                        }

                        try
                        {
                            executor.Execute(statement.Text);
                        }
                        catch (Exception ex)
                        {
                            store.MarkFailure(unit.Id, statement.Index);
                            var failure = new StatementExecutionException(group.Table, statement.Index, inGroup,
                                statement.Text, completedStatements, compiled.Statements.Count, ex);
                            if (!continueOnError)
                            {
                                throw failure;
                            }

                            failures.Add("表 " + group.Table + "：" + ex.Message);
                            groupFailed = true;
                            break;
                        }

                        store.RecordStatement(unit.Id, statement.Index, statement.Hash);
                        completedStatements++;
                        groupExecuted = true;
                        inGroup++;
                    }

                    if (groupFailed)
                    {
                        continue;
                    }

                    if (groupExecuted)
                    {
                        executedTables++;
                    }
                    else
                    {
                        skippedTables++;
                    }
                }

                if (failures.Count == 0)
                {
                    store.MarkApplied(unit.Id);
                    store.ClearStatements(unit.Id);
                }

                options.WriteLog("  完成：共 " + compiled.Groups.Count + " 张表，执行 " + executedTables
                    + "，跳过 " + skippedTables + "，失败 " + failures.Count);
                WriteFailureSummary(options, failures);
            }
            catch (StatementExecutionException)
            {
                throw;
            }
            catch
            {
                store.MarkFailure(unit.Id, index);
                throw;
            }

            return failures;
        }

        private bool IsTableScopedCommit()
        {
            var scope = _config.Options.CommitScope;
            return string.Equals(scope, "table", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "batch", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 按表提交：每张表（batch 模式下每 N 张表）独立事务，提交后立即释放锁；
        /// 失败只回滚当前组，已提交的表记录在 &lt;tables&gt; 中，续跑时跳过。
        /// </summary>
        private List<string> ExecuteByTable(IDbExecutor executor, VersionStore store, ChangeUnit unit, CompileResult compiled, ApplyOptions options, bool continueOnError)
        {
            store.Begin(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect, options.AppliedBy, unit.Description);

            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (options.Restart)
            {
                store.ClearTables(unit.Id);
            }
            else
            {
                foreach (var table in store.LoadAppliedTables(unit.Id))
                {
                    completed.Add(table);
                }
            }

            var isBatch = string.Equals(_config.Options.CommitScope, "batch", StringComparison.OrdinalIgnoreCase);
            var step = isBatch ? Math.Max(1, _config.Options.CommitBatchSize) : 1;
            var skippedTables = completed.Count;
            var failedIndex = 0;
            var failures = new List<string>();

            for (var start = 0; start < compiled.Groups.Count; start += step)
            {
                var end = Math.Min(start + step, compiled.Groups.Count);
                var slice = new List<StatementGroup>();
                for (var i = start; i < end; i++)
                {
                    var group = compiled.Groups[i];
                    if (!completed.Contains(group.Table))
                    {
                        slice.Add(group);
                    }
                }

                if (slice.Count == 0)
                {
                    continue;
                }

                executor.BeginTransaction();
                try
                {
                    foreach (var group in slice)
                    {
                        var inGroup = 0;
                        foreach (var statement in group.Statements)
                        {
                            failedIndex = statement.Index;
                            try
                            {
                                executor.Execute(statement.Text);
                            }
                            catch (Exception ex)
                            {
                                throw new StatementExecutionException(group.Table, statement.Index, inGroup,
                                    statement.Text, completed.Count, compiled.Groups.Count, ex);
                            }
                            inGroup++;
                        }
                    }

                    executor.Commit();
                }
                catch (StatementExecutionException ex)
                {
                    TryRollback(executor);
                    if (!continueOnError)
                    {
                        store.MarkFailure(unit.Id, failedIndex);
                        throw;
                    }

                    failures.Add("表 " + ex.Table + "："
                        + (ex.InnerException == null ? ex.Message : ex.InnerException.Message));
                    continue;
                }
                catch (Exception ex)
                {
                    TryRollback(executor);
                    if (!continueOnError)
                    {
                        store.MarkFailure(unit.Id, failedIndex);
                        throw;
                    }

                    failures.Add(ex.Message);
                    continue;
                }

                foreach (var group in slice)
                {
                    store.MarkTableApplied(unit.Id, group.Table);
                    completed.Add(group.Table);
                }
            }

            if (failures.Count == 0)
            {
                store.MarkApplied(unit.Id);
                store.ClearTables(unit.Id);
            }
            else
            {
                store.MarkFailure(unit.Id, failedIndex);
            }

            options.WriteLog("  完成：共 " + compiled.Groups.Count + " 张表，执行 " + (completed.Count - skippedTables)
                + "，跳过 " + skippedTables + "，失败 " + failures.Count);
            WriteFailureSummary(options, failures);
            return failures;
        }

        /// <summary>
        /// 只预览首条 SQL，并把表名替换为 *（多表单元其余语句同结构、仅表名不同），避免刷屏。
        /// </summary>
        private static string PreviewSql(CompileResult compiled, ISqlDialect dialect)
        {
            if (compiled.Statements.Count == 0)
            {
                return "（无语句）";
            }

            var first = compiled.Statements[0].Text.Trim();
            if (compiled.Groups.Count > 1)
            {
                var table = compiled.Groups[0].Table;
                if (!string.IsNullOrEmpty(table))
                {
                    first = first.Replace(dialect.Quote(table), dialect.Quote("*"));
                }
            }

            if (first.Length > 200)
            {
                first = first.Substring(0, 200) + " ...";
            }

            if (compiled.Statements.Count <= 1)
            {
                return first;
            }

            var tableHint = compiled.Groups.Count > 1 ? "，表名以 * 表示" : "";
            return first + "    （共 " + compiled.Statements.Count + " 条" + tableHint + "）";
        }

        /// <summary>失败明细逐行输出（一条一行），避免挤成一行难以阅读。</summary>
        private static void WriteFailureSummary(ApplyOptions options, List<string> failures)
        {
            foreach (var failure in failures)
            {
                options.WriteLog("    " + failure);
            }
        }

        private static void TryRollback(IDbExecutor executor)
        {
            try
            {
                executor.Rollback();
            }
            catch
            {
            }
        }

        private static void TryDeleteRecord(VersionStore store, string changeId)
        {
            try
            {
                store.Delete(changeId);
            }
            catch
            {
            }
        }

        private void WriteAuditLog(ChangeUnit unit, CompileResult compiled, ApplyOptions options)
        {
            var directory = _config.ResolveVersionLogPath();
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                var writer = new VersionLogWriter(directory);
                var snapshot = writer.WriteSqlSnapshot(unit, compiled, _config.Database.Dialect);
                var database = _config.Database.Dialect + " " + _config.Database.Version;
                writer.AppendEntry(unit, compiled, options.AppliedBy, database, snapshot, _config.Database.Dialect, false);
            }
            catch (Exception ex)
            {
                options.WriteLog("警告：审计日志写入失败（" + ex.Message + "），下次 apply 可补生成");
}
}
}
}