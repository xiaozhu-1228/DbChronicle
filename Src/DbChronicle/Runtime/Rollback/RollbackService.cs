using System;
using System.Collections.Generic;
using System.Linq;
using DbChronicle.Configuration;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Domain.Model;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Runtime.Rollback
{
    public sealed class RollbackResult
    {
        public bool Success { get; set; }

        public int RolledBackCount { get; set; }

        public string Message { get; set; }

        public int? ExitCode { get; set; }

        public List<string> Blockers { get; } = new List<string>();
    }

    public sealed class RollbackService
    {
        private readonly DbChronicleConfig _config;
        private readonly IDbExecutorFactory _executorFactory;
        private readonly IDbLockFactory _lockFactory;

        public RollbackService(DbChronicleConfig config, IDbExecutorFactory executorFactory, IDbLockFactory lockFactory)
        {
            _config = config;
            _executorFactory = executorFactory;
            _lockFactory = lockFactory;
        }

        public RollbackPlan Plan(IReadOnlyList<ChangeUnit> units, string targetRelease)
        {
            var dialect = DialectFactory.Create(_config.Database.Dialect, _config.Database.Version,
                new DialectContext { IncrementalModify = _config.Options.IncrementalModify });
            return new RollbackPlanner().Build(units, targetRelease, dialect);
        }

        public RollbackResult Execute(RollbackPlan plan, string connectionString)
        {
            var result = new RollbackResult();

            if (plan.Steps.Count == 0)
            {
                result.Success = true;
                result.Message = "没有需要回滚的变更单元";
                return result;
            }

            if (plan.HasBlockers)
            {
                foreach (var step in plan.Steps.Where(x => x.IsBlocked))
                {
                    foreach (var blocker in step.Blockers)
                    {
                        result.Blockers.Add(step.Unit.Id + "：" + blocker);
                    }
                }
                result.Message = "存在无法自动回滚的操作，请先手工处理后再执行";
                result.ExitCode = 1;
                return result;
            }

            using (var executor = _executorFactory.Create(connectionString))
            {
                IDbLock dbLock = null;
                try
                {
                    if (_lockFactory != null)
                    {
                        dbLock = _lockFactory.Create(connectionString);
                        if (!dbLock.TryAcquire(TimeSpan.FromSeconds(Math.Max(0, _config.Options.LockTimeoutSeconds))))
                        {
                            result.Message = "另一进程正在执行 apply / rollback（无法获取库级锁）";
                            result.ExitCode = 4;
                            return result;
                        }
                    }

                    var store = new VersionStore(_config);
                    foreach (var step in plan.Steps)
                    {
                        if (step.Statements.Count == 0)
                        {
                            store.Delete(step.Unit.Id);
                            continue;
                        }

                        try
                        {
                            ExecuteStep(executor, store, step);
                        }
                        catch (Exception ex)
                        {
                            result.Message = "回滚失败：" + step.Unit.Id + "：" + ex.Message;
                            result.ExitCode = 3;
                            return result;
                        }

                        result.RolledBackCount++;
                    }
                }
                finally
                {
                    if (dbLock != null)
                    {
                        dbLock.Dispose();
                    }
                }
            }

            result.Success = true;
            return result;
        }

        private static void ExecuteStep(IDbExecutor executor, VersionStore store, RollbackStep step)
        {
            if (executor.SupportsTransactionalDdl)
            {
                executor.BeginTransaction();
                try
                {
                    foreach (var statement in step.Statements)
                    {
                        executor.Execute(statement.Text);
                    }
                    store.Delete(step.Unit.Id);
                    executor.Commit();
                }
                catch
                {
                    executor.Rollback();
                    throw;
                }
                return;
            }

            foreach (var statement in step.Statements)
            {
                executor.Execute(statement.Text);
            }
            store.Delete(step.Unit.Id);
        }
    }
}
