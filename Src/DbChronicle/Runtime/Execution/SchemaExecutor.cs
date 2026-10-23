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
using DbChronicle.Domain.Model;
using DbChronicle.Parsing;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Runtime.Execution
{
    public sealed class ApplyOptions
    {
        public string ConnectionString { get; set; }

        public string AppliedBy { get; set; }

        public bool DryRun { get; set; }

        public string MarkAppliedChangeId { get; set; }

        public bool Restart { get; set; }

        public int LockTimeoutSeconds { get; set; } = 10;

        public Action<string> Log { get; set; }

        public void WriteLog(string message)
        {
            var handler = Log;
            if (handler != null)
            {
                handler(message);
            }
        }
    }

    public sealed class BackfillResult
    {
        public bool Success { get; set; }

        public int BackfilledCount { get; set; }

        public int SkippedCount { get; set; }

        public string Message { get; set; }

        public int? ExitCode { get; set; }
    }

    public sealed class ApplyResult
    {
        public bool Success { get; set; }

        public int AppliedCount { get; set; }

        public int SkippedCount { get; set; }

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        public List<CompileDiagnostic> Diagnostics { get; } = new List<CompileDiagnostic>();
    }

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

                    foreach (var unit in ordered)
                    {
                        if (!PrepareWildcardTables(executor, unit, compilerOptions, options, out var wildcardMessage))
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
                        result.Diagnostics.AddRange(compiled.Diagnostics);

                        if (compiled.HasErrors)
                        {
                            result.Message = "变更单元编译失败：" + unit.Id;
                            result.ExitCode = 1;
                            return result;
                        }

                        var record = applied.FirstOrDefault(x => string.Equals(x.ChangeId, unit.Id, StringComparison.OrdinalIgnoreCase));
                        if (record != null && !record.HasFailure)
                        {
                            if (!string.Equals(record.Checksum, unit.Checksum, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Message = "已应用的变更单元文件被修改（checksum 不一致）：" + unit.Id
                                    + "；已应用=" + record.Checksum + "，当前=" + unit.Checksum;
                                result.ExitCode = 3;
                                return result;
                            }

                            result.SkippedCount++;
                            continue;
                        }

                        options.WriteLog("应用 " + unit.Id + "（" + compiled.Statements.Count + " 条语句）");

                        if (options.DryRun)
                        {
                            options.WriteLog(compiled.Sql);
                            continue;
                        }

                        try
                        {
                            ExecuteUnit(executor, store, unit, compiled, options);
                        }
                        catch (Exception ex)
                        {
                            result.Message = "执行失败：" + unit.Id + "：" + ex.Message;
                            result.ExitCode = 3;
                            return result;
                        }

                        result.AppliedCount++;
                        WriteAuditLog(unit, compiled, options);
                    }

                    result.Success = true;
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
            options.WriteLog("通配符 table=\"*\" 将作用于库中 " + tables.Count + " 张表："
                + string.Join(", ", tables));
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

        private void ExecuteUnit(IDbExecutor executor, VersionStore store, ChangeUnit unit, CompileResult compiled, ApplyOptions options)
        {
            if (executor.SupportsTransactionalDdl)
            {
                executor.BeginTransaction();
                try
                {
                    store.Delete(unit.Id);
                    store.Begin(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect, options.AppliedBy, unit.Description);
                    foreach (var statement in compiled.Statements)
                    {
                        executor.Execute(statement.Text);
                    }
                    store.MarkApplied(unit.Id);
                    executor.Commit();
                }
                catch
                {
                    executor.Rollback();
                    throw;
                }
                return;
            }

            store.EnsureStatementTable();
            if (options.Restart)
            {
                store.ClearStatements(unit.Id);
            }

            var appliedStatements = store.LoadStatements(unit.Id);
            store.Delete(unit.Id);
            store.Begin(unit.Id, unit.Release, unit.Checksum, _config.Database.Dialect, options.AppliedBy, unit.Description);

            var index = 0;
            try
            {
                foreach (var statement in compiled.Statements)
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
                        continue;
                    }

                    executor.Execute(statement.Text);
                    store.RecordStatement(unit.Id, statement.Index, statement.Hash);
                }

                store.MarkApplied(unit.Id);
                store.ClearStatements(unit.Id);
            }
            catch
            {
                store.MarkFailure(unit.Id, index);
                throw;
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
