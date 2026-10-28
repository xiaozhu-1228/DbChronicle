using System;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 需要某项能力但对应插件未安装。宿主据此给出"该部署哪个 dll"的提示，
    /// 而不是抛出 <c>Could not load file or assembly</c> 这类用户无法理解的错误。
    /// </summary>
    public sealed class PluginNotInstalledException : Exception
    {
        public PluginNotInstalledException(string capability, string installHint, Exception inner = null)
            : base("缺少「" + capability + "」能力：" + installHint, inner)
        {
            Capability = capability;
            InstallHint = installHint;
        }

        /// <summary>缺失的能力描述（如"数据库执行器（mysql）"）。</summary>
        public string Capability { get; }

        /// <summary>部署提示。</summary>
        public string InstallHint { get; }
    }
}
