using System;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 插件入口标记：标在 <c>public static</c> 类上，该类需提供
    /// <c>public static void Initialize(IPluginRegistry registry)</c>。
    /// <para>
    /// 用显式特性而非类名约定，避免误匹配；宿主按需加载插件程序集后自动调用 Initialize。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DbChroniclePluginAttribute : Attribute
    {
        /// <summary>插件名（用于日志与冲突提示）。</summary>
        public string Name { get; set; }

        /// <summary>同名能力的优先级，数值大者胜（缺省 0）。</summary>
        public int Order { get; set; }

        /// <summary>要求的最低宿主版本（如 "1.0.0"）；宿主版本更低时跳过加载。</summary>
        public string MinHostVersion { get; set; }
    }
}
