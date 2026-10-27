using System.Collections.Generic;

using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>把结构模型写成某一语言的实体源码。</summary>
    public interface IEntityCodeWriter
    {
        /// <summary>语言标识（csharp / java / go / typescript）。</summary>
        string Language { get; }

        /// <summary>文件扩展名（含点）。</summary>
        string FileExtension { get; }

        /// <summary>生成单个表的源码。</summary>
        string WriteTable(TableModel table, EntityCodeOptions options);

        /// <summary>生成附加文件（如需要注册的辅助类型），没有则返回空。</summary>
        IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options);
    }
}
