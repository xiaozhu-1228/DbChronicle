using System;
using System.IO;

using DbChronicle.Models;

namespace DbChronicle.Scanner.Languages
{
    /// <summary>
    /// 多语言源码扫描分派：按语言或按目录里的文件扩展名自动选择扫描器。
    /// `auto` 模式下同一目录可混合多种语言，结果合并。
    /// </summary>
    public sealed class LanguageSourceScanner
    {
        public ScanResult ScanDirectory(string directory, string language)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("源码目录不存在：" + directory);
            }

            var normalized = (language ?? "auto").Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "java":
                    return new JavaSourceScanner().ScanDirectory(directory);
                case "go":
                case "golang":
                    return new GoSourceScanner().ScanDirectory(directory);
                case "typescript":
                case "ts":
                    return new TypeScriptSourceScanner().ScanDirectory(directory);
                case "auto":
                    return ScanAuto(directory);
                default:
                    throw new NotSupportedException("不支持的语言：" + language + "（可用：java / go / typescript / auto）");
            }
        }

        private static ScanResult ScanAuto(string directory)
        {
            var merged = new ScanResult();
            Merge(merged, new JavaSourceScanner().ScanDirectory(directory));
            Merge(merged, new GoSourceScanner().ScanDirectory(directory));
            Merge(merged, new TypeScriptSourceScanner().ScanDirectory(directory));
            return merged;
        }

        private static void Merge(ScanResult target, ScanResult source)
        {
            foreach (var table in source.Tables)
            {
                target.Tables.Add(table);
            }
            foreach (var warning in source.Warnings)
            {
                target.Warnings.Add(warning);
            }
        }
    }
}
