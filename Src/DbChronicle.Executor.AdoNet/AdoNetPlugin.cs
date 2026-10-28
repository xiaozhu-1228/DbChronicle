using DbChronicle.Cli.Plugins;

namespace DbChronicle.Executor.AdoNet
{
    /// <summary>
    /// ADO.NET 执行器插件入口：CLI 需要数据库执行能力时才加载本程序集，
    /// 由 CLI 侧的 PluginHost 反射调用 <see cref="Initialize"/> 完成注册。
    /// </summary>
    [DbChroniclePlugin(Name = "adonet")]
    public static class AdoNetPlugin
    {
        private static readonly string[] Dialects =
        {
            "mysql", "mariadb", "postgresql", "sqlserver", "sqlite"
        };

        public static void Initialize(IPluginRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            foreach (var dialect in Dialects)
            {
                var name = dialect;
                registry.AddExecutor(
                    name,
                    () => new AdoNetExecutorFactory(name),
                    () => new AdoNetExecutorFactory(name));
            }
        }
    }
}
