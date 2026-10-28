using System;
using System.IO;
using System.Linq;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using Xunit;

namespace DbChronicle.Cli.Tests
{
    /// <summary>
    /// 插件目录发现与启停：每插件独占子目录、禁用后不参与加载、禁用状态持久化到用户目录。
    /// </summary>
    public class PluginManagementTests
    {
        [Fact]
        public void DiscoverPluginFolders_ShouldListSubdirectories()
        {
            using (var scope = new TemporaryPluginScope())
            {
                scope.CreatePluginFolder("adonet", "DbChronicle.Executor.AdoNet.dll");
                scope.CreatePluginFolder("scanner-source", "DbChronicle.Scanner.Source.dll");

                using (var host = new PluginHost(scope.PluginRoot))
                {
                    var folders = host.DiscoverPluginFolders();

                    Assert.Equal(2, folders.Count);
                    Assert.Contains(folders, x => x.Name == "adonet" && !x.Disabled);
                    Assert.Contains(folders, x => x.Name == "scanner-source");
                    Assert.Contains("DbChronicle.Executor.AdoNet.dll",
                        folders.Single(x => x.Name == "adonet").Assemblies);
                }
            }
        }

        [Fact]
        public void DiscoverPluginFolders_ShouldMarkDisabled()
        {
            using (var scope = new TemporaryPluginScope())
            {
                scope.CreatePluginFolder("adonet", "DbChronicle.Executor.AdoNet.dll");

                var settings = PluginSettings.Load();
                settings.Disable("adonet");
                settings.Save();

                using (var host = new PluginHost(scope.PluginRoot))
                {
                    var folder = Assert.Single(host.DiscoverPluginFolders());
                    Assert.True(folder.Disabled);
                }
            }
        }

        [Fact]
        public void SearchDirectories_ShouldSkipDisabledPlugin()
        {
            using (var scope = new TemporaryPluginScope())
            {
                scope.CreatePluginFolder("adonet", "DbChronicle.Executor.AdoNet.dll");
                scope.CreatePluginFolder("scanner-source", "DbChronicle.Scanner.Source.dll");

                using (var host = new PluginHost(scope.PluginRoot))
                {
                    Assert.Contains(host.SearchDirectories, x => x.EndsWith("adonet", StringComparison.OrdinalIgnoreCase));

                    var settings = PluginSettings.Load();
                    settings.Disable("adonet");
                    settings.Save();
                    host.Reload();

                    Assert.DoesNotContain(host.SearchDirectories, x => x.EndsWith("adonet", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(host.SearchDirectories, x => x.EndsWith("scanner-source", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        [Fact]
        public void Settings_ShouldPersistDisabledList()
        {
            using (var scope = new TemporaryPluginScope())
            {
                var settings = PluginSettings.Load();
                Assert.True(settings.Disable("adonet"));
                Assert.False(settings.Disable("adonet"), "重复禁用应返回 false");
                settings.Save();

                var reloaded = PluginSettings.Load();
                Assert.True(reloaded.IsDisabled("ADONET"), "名称比较应大小写无关");

                Assert.True(reloaded.Enable("adonet"));
                reloaded.Save();

                Assert.False(PluginSettings.Load().IsDisabled("adonet"));
            }
        }

        [Fact]
        public void PluginDirectory_ShouldFallBackToExeDirectory_WhenNoPluginsFolder()
        {
            var missing = Path.Combine(Path.GetTempPath(), "dbchronicle-missing", Guid.NewGuid().ToString("N"));
            using (var host = new PluginHost(missing))
            {
                Assert.False(Directory.Exists(host.PluginDirectory));
                Assert.Empty(host.DiscoverPluginFolders());
                Assert.Empty(host.SearchDirectories);
            }
        }

        /// <summary>临时插件目录 + 隔离的设置目录。</summary>
        private sealed class TemporaryPluginScope : IDisposable
        {
            private readonly string _root;
            private readonly string _settingsDirectory;

            public TemporaryPluginScope()
            {
                _root = Path.Combine(Path.GetTempPath(), "dbchronicle-plugins", Guid.NewGuid().ToString("N"));
                _settingsDirectory = Path.Combine(Path.GetTempPath(), "dbchronicle-settings", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                Directory.CreateDirectory(_settingsDirectory);

                PluginRoot = _root;
                PluginSettings.DirectoryOverride = _settingsDirectory;
            }

            public string PluginRoot { get; }

            public void CreatePluginFolder(string name, string assemblyName)
            {
                var directory = Path.Combine(_root, name);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, assemblyName), new byte[] { 0x4D, 0x5A });
            }

            public void Dispose()
            {
                PluginSettings.DirectoryOverride = null;
                TryDelete(_settingsDirectory);
                TryDelete(_root);
            }

            private static void TryDelete(string directory)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
                catch (Exception)
                {
                    // 清理失败不影响测试结论
                }
            }
        }
    }
}
