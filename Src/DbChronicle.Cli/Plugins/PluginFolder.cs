using System.Collections.Generic;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>插件目录的一条记录（`plugin list` 与内部发现共用）。</summary>
    public sealed class PluginFolder
    {
        /// <summary>插件名（目录名；扁平布局时为 "(root)"）。</summary>
        public string Name { get; set; }

        /// <summary>目录绝对路径。</summary>
        public string Path { get; set; }

        /// <summary>是否被用户禁用。</summary>
        public bool Disabled { get; set; }

        /// <summary>目录内的插件程序集文件名（形如 DbChronicle.Executor.AdoNet.dll）。</summary>
        public List<string> Assemblies { get; set; } = new List<string>();
    }
}
