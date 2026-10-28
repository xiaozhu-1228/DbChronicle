using DbChronicle.Cli.Plugins;

namespace DbChronicle.Scanner.Source
{
    /// <summary>
    /// C# 源码扫描插件入口：Roslyn 依赖只随本程序集发布，
    /// 只在 <c>scan --source --lang csharp</c> 时被按需加载。
    /// </summary>
    [DbChroniclePlugin(Name = "scanner-source")]
    public static class SourceScannerPlugin
    {
        public static void Initialize(IPluginRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            registry.AddSourceScanner("csharp", () => new SourceScanner());
        }
    }
}
