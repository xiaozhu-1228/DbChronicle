using DbChronicle.Models;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 源码扫描器契约：各语言插件实现它，宿主按语言取用。
    /// （核心库只提供 C# 反射扫描与数据库反向扫描，源码级扫描按语言拆成插件。）
    /// </summary>
    public interface ISourceScanner
    {
        /// <summary>语言标识（csharp / java / go / typescript / auto）。</summary>
        string Language { get; }

        /// <summary>扫描目录，返回表清单（失败项记入 <see cref="ScanResult.Warnings"/>）。</summary>
        ScanResult ScanDirectory(string directory);
    }
}
