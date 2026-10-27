using System;

using DbChronicle.Execution;
using Microsoft.EntityFrameworkCore;

namespace DbChronicle.Executor.EfCore
{
    /// <summary>
    /// 用宿主提供的 <see cref="DbContext"/> 构造执行器。
    /// 连接串参数由 <c>contextFactory</c> 决定（通常忽略，因为上下文已绑定连接）。
    /// </summary>
    public sealed class EfCoreExecutorFactory : IDbExecutorFactory
    {
        private readonly Func<string, DbContext> _contextFactory;

        public EfCoreExecutorFactory(string dialectName, Func<string, DbContext> contextFactory)
        {
            if (contextFactory == null)
            {
                throw new ArgumentNullException(nameof(contextFactory));
            }

            DialectName = (dialectName ?? "").Trim().ToLowerInvariant();
            _contextFactory = contextFactory;
        }

        /// <summary>直接用已创建的上下文（最常用的内嵌用法）。</summary>
        public EfCoreExecutorFactory(DbContext context, string dialectName)
            : this(dialectName, _ => context)
        {
        }

        public string DialectName { get; }

        public IDbExecutor Create(string connectionString)
        {
            var context = _contextFactory(connectionString);
            if (context == null)
            {
                throw new InvalidOperationException("contextFactory 未返回可用的 DbContext");
            }
            return new EfCoreExecutor(context, DialectName);
        }
    }
}
