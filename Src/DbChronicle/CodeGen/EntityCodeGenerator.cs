using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>
    /// 代码生成（XML → 实体）：把累积结构模型写成 C# / Java / Go / TypeScript 源码。
    /// 每张表一个文件，文件名取表名的 PascalCase。
    /// </summary>
    public sealed class EntityCodeGenerator
    {
        public static IEntityCodeWriter CreateWriter(string language)
        {
            switch ((language ?? "").Trim().ToLowerInvariant())
            {
                case "csharp":
                case "c#":
                case "cs":
                    return new CSharpEntityWriter();
                case "java":
                    return new JavaEntityWriter();
                case "go":
                case "golang":
                    return new GoEntityWriter();
                case "typescript":
                case "ts":
                    return new TypeScriptEntityWriter();
                case "cpp":
                case "c++":
                case "cxx":
                case "cplusplus":
                    return new CppEntityWriter();
                default:
                    return null;
            }
        }

        /// <summary>生成"文件名 → 源码"清单（不落盘，便于宿主自行处理）。</summary>
        public List<KeyValuePair<string, string>> Generate(StructureModel model, EntityCodeOptions options)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            options = options ?? new EntityCodeOptions();
            var writer = CreateWriter(options.Language);
            if (writer == null)
            {
                throw new NotSupportedException("不支持的代码生成语言：" + options.Language);
            }

            var files = new List<KeyValuePair<string, string>>();
            foreach (var table in model.Tables.Values)
            {
                var fileName = CodeNaming.ToPascalCase(table.Name) + writer.FileExtension;
                files.Add(new KeyValuePair<string, string>(fileName, writer.WriteTable(table, options)));
            }
            files.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));

            foreach (var extra in writer.WriteExtraFiles(options))
            {
                files.Add(extra);
            }

            // C++ 需要聚合头便于一次 include；其他语言无此需求
            if (writer is CodeGen.CppEntityWriter cppWriter)
            {
                files.Add(new KeyValuePair<string, string>(
                    "all_entities.h", cppWriter.WriteAggregateHeader(new List<TableModel>(model.Tables.Values), options)));
            }

            return files;
        }

        /// <summary>直接写到目录，返回生成的文件数。</summary>
        public int GenerateToDirectory(StructureModel model, EntityCodeOptions options, string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("输出目录不能为空", nameof(directory));
            }

            Directory.CreateDirectory(directory);
            var files = Generate(model, options);
            foreach (var file in files)
            {
                File.WriteAllText(Path.Combine(directory, file.Key), file.Value, new UTF8Encoding(false));
            }
            return files.Count;
        }
    }
}
