using System;

using DbChronicle.Execution;

namespace DbChronicle.Embedded
{
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
            if (options.Config == null && string.IsNullOrEmpty(options.ConfigPath))
            {
                throw new ArgumentException("必须提供 ConfigPath（启动配置文件）或 Config（代码内构造的配置）", "options");
            }

            return new DefaultDbChronicle(executor, options);
        }
    }
}