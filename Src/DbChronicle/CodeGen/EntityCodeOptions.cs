namespace DbChronicle.CodeGen
{
    /// <summary>代码生成选项。</summary>
    public sealed class EntityCodeOptions
    {
        /// <summary>目标语言：csharp / java / go / typescript。</summary>
        public string Language { get; set; } = "csharp";

        /// <summary>C# 命名空间 / Java 包名 / Go 包名 / TS 模块名。</summary>
        public string Namespace { get; set; } = "Entities";

        /// <summary>是否输出数据库列名映射（C# 用 [Column]，Go 用 tag，JS/TS 用注释）。</summary>
        public bool IncludeColumnMapping { get; set; } = true;

        /// <summary>是否输出实体注释（表 / 列注释）。</summary>
        public bool IncludeComments { get; set; } = true;

        /// <summary>是否生成到独立文件（false 时全部拼进一个文件）。</summary>
        public bool SplitFiles { get; set; } = true;
    }
}
