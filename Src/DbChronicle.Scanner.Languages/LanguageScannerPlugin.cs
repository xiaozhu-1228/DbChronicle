using DbChronicle.Cli.Plugins;

namespace DbChronicle.Scanner.Languages
{
    /// <summary>
    /// 多语言源码扫描插件入口：注册 java / go / typescript 与 auto 分派，
    /// 只在 <c>scan --source --lang java|go|typescript|auto</c> 时被按需加载。
    /// </summary>
    [DbChroniclePlugin(Name = "scanner-languages")]
    public static class LanguageScannerPlugin
    {
        public static void Initialize(IPluginRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            registry.AddSourceScanner("java", () => new JavaSourceScanner());
            registry.AddSourceScanner("go", () => new GoSourceScanner());
            registry.AddSourceScanner("typescript", () => new TypeScriptSourceScanner());
            registry.AddSourceScanner("auto", () => new LanguageSourceScanner());
        }
    }
}
