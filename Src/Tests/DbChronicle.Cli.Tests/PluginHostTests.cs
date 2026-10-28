using System;
using System.IO;
using System.Linq;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using Xunit;

namespace DbChronicle.Cli.Tests
{
    /// <summary>
    /// 插件宿主与能力目录：无插件时的友好报错、按需加载的前置行为、注册冲突与清理。
    /// </summary>
    public class PluginHostTests
    {
        [Fact]
        public void Host_WithoutPlugins_ShouldExposeEmptyCatalog()
        {
            using (var host = new PluginHost(TempDirectory()))
            {
                Assert.Empty(host.Catalog.ExecutorDialects);
                Assert.Empty(host.Catalog.ScannerLanguages);
                Assert.Empty(host.Catalog.Commands);
                Assert.Empty(host.Warnings);
            }
        }

        [Fact]
        public void Host_ResolveExecutorWithoutPlugin_ShouldThrowWithHint()
        {
            using (var host = new PluginHost(TempDirectory()))
            {
                var ex = Assert.Throws<PluginNotInstalledException>(() => host.ResolveExecutor("mysql"));

                Assert.Contains("mysql", ex.Capability);
                Assert.Contains("DbChronicle.Executor.AdoNet", ex.InstallHint);
                Assert.Contains(PluginHost.PluginDirectoryName, ex.InstallHint);
            }
        }

        [Fact]
        public void Host_ResolveScannerWithoutPlugin_ShouldThrowWithHint()
        {
            using (var host = new PluginHost(TempDirectory()))
            {
                var csharp = Assert.Throws<PluginNotInstalledException>(() => host.ResolveSourceScanner("csharp"));
                Assert.Contains("DbChronicle.Scanner.Source", csharp.InstallHint);

                var java = Assert.Throws<PluginNotInstalledException>(() => host.ResolveSourceScanner("java"));
                Assert.Contains("DbChronicle.Scanner.Languages", java.InstallHint);
            }
        }

        [Fact]
        public void Host_ResolveCodeGeneratorWithoutPlugin_ShouldReturnNull()
        {
            using (var host = new PluginHost(TempDirectory()))
            {
                // 代码生成有核心库内建实现，缺插件不算错误
                Assert.Null(host.ResolveCodeGenerator("csharp"));
            }
        }

        [Fact]
        public void Catalog_HigherOrderShouldWin()
        {
            var catalog = new PluginCatalog();

            catalog.CurrentPlugin = "low";
            catalog.SetCurrent(0);
            catalog.AddExecutor("mysql", () => null);

            catalog.CurrentPlugin = "high";
            catalog.SetCurrent(5);
            catalog.AddExecutor("mysql", () => null);

            Assert.Contains(catalog.Conflicts, x => x.Contains("high"));
        }

        [Fact]
        public void Catalog_ShouldRegisterCommandsAndScanners()
        {
            var catalog = new PluginCatalog();
            catalog.CurrentPlugin = "test";
            catalog.SetCurrent(0);

            catalog.AddSourceScanner("java", () => null);
            catalog.AddCommand("docgen", context => 0, "生成文档");

            Assert.Single(catalog.ScannerLanguages);
            Assert.True(catalog.HasScanner("JAVA"), "语言名应大小写无关");
            Assert.Single(catalog.Commands);
            Assert.Equal("生成文档", catalog.Commands["docgen"].Help);
        }

        [Fact]
        public void Catalog_ClearShouldDropRegistrations()
        {
            var catalog = new PluginCatalog();
            catalog.AddSourceScanner("java", () => null);
            catalog.AddCommand("docgen", context => 0, "生成文档");
            Assert.Single(catalog.ScannerLanguages);

            catalog.Clear();

            Assert.Empty(catalog.ScannerLanguages);
            Assert.Empty(catalog.Commands);
            Assert.Empty(catalog.ExecutorDialects);
        }

        [Fact]
        public void CommandContext_ShouldForwardArgumentsToPlugin()
        {
            // 注意：CliArguments 把 "--x value" 视为键值对，flag 需放在末尾
            var arguments = CliArguments.Parse(new[] { "--name", "tester", "positional1", "--verbose" });
            var context = new PluginCommandContext(arguments);

            Assert.Equal("tester", context.Get("name"));
            Assert.Null(context.Get("missing"));
            Assert.True(context.Has("verbose"));
            Assert.False(context.Has("missing"));
            Assert.Single(context.Positional);
            Assert.Equal("positional1", context.Positional[0]);
            Assert.NotNull(context.Out);
            Assert.NotNull(context.Error);
        }

        private static string TempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dbchronicle-plugins", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
