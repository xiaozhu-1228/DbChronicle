using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using DbChronicle.Common.Enums;
using DbChronicle.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DbChronicle.Scanner.Source
{
    /// <summary>
    /// C# 源码扫描（Roslyn）：直接从 .cs 文件识别实体类与属性，生成基线草案。
    /// 相比程序集反射，无需先编译；代价是无法获得运行时类型信息（泛型、继承链上的特性需人工确认）。
    /// </summary>
    public sealed class SourceScanner
    {
        private static readonly HashSet<string> EntityAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Table", "TableAttribute", "TableName", "TableNameAttribute", "Entity", "EntityAttribute"
        };

        public ScanResult ScanDirectory(string directory, string searchPattern = "*.cs")
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("源码目录不存在：" + directory);
            }

            var result = new ScanResult();
            foreach (var file in Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories))
            {
                if (IsGeneratedOrBuildOutput(file))
                {
                    continue;
                }

                try
                {
                    ScanFile(file, result);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("文件解析失败，已跳过：" + Path.GetFileName(file) + "（" + ex.Message + "）");
                }
            }

            return result;
        }

        public ScanResult ScanFiles(IEnumerable<string> files)
        {
            var result = new ScanResult();
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    result.Warnings.Add("文件不存在，已跳过：" + file);
                    continue;
                }

                try
                {
                    ScanFile(file, result);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("文件解析失败，已跳过：" + Path.GetFileName(file) + "（" + ex.Message + "）");
                }
            }
            return result;
        }

        private static bool IsGeneratedOrBuildOutput(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/obj/")
                || normalized.Contains("/bin/")
                || normalized.Contains("/.git/")
                || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
        }

        private void ScanFile(string file, ScanResult result)
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest));
            var root = tree.GetCompilationUnitRoot();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsEntity(classDecl))
                {
                    continue;
                }

                var table = MapTable(classDecl, result, Path.GetFileName(file));
                if (table != null && table.Columns.Count > 0)
                {
                    result.Tables.Add(table);
                }
            }
        }

        private static bool IsEntity(ClassDeclarationSyntax classDecl)
        {
            foreach (var attribute in classDecl.AttributeLists.SelectMany(x => x.Attributes))
            {
                if (EntityAttributes.Contains(AttributeName(attribute)))
                {
                    return true;
                }
            }

            // 类名后缀约定：*Entity
            var name = classDecl.Identifier.ValueText;
            return !string.IsNullOrEmpty(name)
                && name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase)
                && name.Length > "Entity".Length;
        }

        private static ScannedTable MapTable(ClassDeclarationSyntax classDecl, ScanResult result, string fileName)
        {
            var attributes = classDecl.AttributeLists.SelectMany(x => x.Attributes).ToList();
            var table = new ScannedTable
            {
                Name = GetTableName(classDecl, attributes),
                Comment = GetComment(attributes)
            };

            foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                var propertyAttributes = property.AttributeLists.SelectMany(x => x.Attributes).ToList();
                if (HasAttribute(propertyAttributes, "NotMapped", "JsonIgnore", "Ignore"))
                {
                    continue;
                }

                var typeName = property.Type.ToString();
                if (!IsScalarType(typeName))
                {
                    continue;
                }

                var column = MapColumn(property, propertyAttributes, typeName, result);
                if (column != null)
                {
                    table.Columns.Add(column);
                    if (column.PrimaryKey)
                    {
                        table.PrimaryKeyColumns.Add(column.Name);
                    }
                }
            }

            // 类级索引特性：[Index("idx_x", nameof(A), nameof(B), Unique = true)]
            foreach (var attribute in attributes)
            {
                if (!AttributeName(attribute).StartsWith("Index", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var index = new ScannedIndex
                {
                    Name = GetAttributeString(attribute, "Name", "IndexName"),
                    Unique = GetAttributeBool(attribute, "Unique", "IsUnique")
                };

                var positional = attribute.ArgumentList != null ? attribute.ArgumentList.Arguments : default;
                var names = new List<string>();
                if (positional.Count > 0)
                {
                    foreach (var argument in positional)
                    {
                        var value = ArgumentValue(argument);
                        if (string.IsNullOrEmpty(value) || string.Equals(value, index.Name, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        names.Add(value.Replace("nameof(", "").Replace(")", "").Trim());
                    }
                }

                if (names.Count == 0)
                {
                    continue;
                }

                index.Name = string.IsNullOrEmpty(index.Name)
                    ? "idx_" + table.Name + "_" + string.Join("_", names)
                    : index.Name;
                index.Columns.AddRange(names);
                table.Indexes.Add(index);
            }

            if (table.Columns.Count == 0)
            {
                result.Warnings.Add("类 " + classDecl.Identifier.ValueText + "（" + fileName
                    + "）识别为实体但没有可映射的标量属性");
            }

            return table;
        }

        private static ScannedColumn MapColumn(PropertyDeclarationSyntax property, List<AttributeSyntax> attributes,
            string typeName, ScanResult result)
        {
            var clrType = typeName.Replace(" ", "");
            var nullableSuffix = clrType.EndsWith("?", StringComparison.Ordinal);
            if (nullableSuffix)
            {
                clrType = clrType.Substring(0, clrType.Length - 1);
            }

            var columnName = GetAttributeString(attributes, "Name", "ColumnName")
                ?? property.Identifier.ValueText;

            var column = new ScannedColumn
            {
                Name = columnName,
                Nullable = nullableSuffix || IsReferenceType(clrType),
                PrimaryKey = HasAttribute(attributes, "Key", "PrimaryKey")
                    || string.Equals(property.Identifier.ValueText, "Id", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Identifier.ValueText, clrType.Substring(0, 0) + "Id", StringComparison.Ordinal),
                AutoIncrement = HasAttribute(attributes, "AutoIncrement", "Identity", "DatabaseGenerated"),
                Comment = GetComment(attributes),
                DefaultValue = GetAttributeString(attributes, "DefaultValue", "Default")
            };

            if (HasAttribute(attributes, "Required"))
            {
                column.Nullable = false;
            }

            var maxLength = GetAttributeInt(attributes, "MaxLength", "Length", "StringLength");
            if (maxLength.HasValue)
            {
                column.Length = maxLength;
            }

            MapType(clrType, column, result);
            return column;
        }

        private static void MapType(string clrType, ScannedColumn column, ScanResult result)
        {
            switch (clrType)
            {
                case "bool":
                case "Boolean":
                    column.Type = LogicalType.Boolean;
                    return;
                case "byte":
                    column.Type = LogicalType.Int8;
                    column.Unsigned = true;
                    return;
                case "sbyte":
                    column.Type = LogicalType.Int8;
                    return;
                case "short":
                    column.Type = LogicalType.Int16;
                    return;
                case "ushort":
                    column.Type = LogicalType.Int16;
                    column.Unsigned = true;
                    return;
                case "int":
                case "Int32":
                    column.Type = LogicalType.Int32;
                    return;
                case "uint":
                case "UInt32":
                    column.Type = LogicalType.Int32;
                    column.Unsigned = true;
                    return;
                case "long":
                case "Int64":
                    column.Type = LogicalType.Int64;
                    return;
                case "ulong":
                case "UInt64":
                    column.Type = LogicalType.Int64;
                    column.Unsigned = true;
                    return;
                case "float":
                case "Single":
                    column.Type = LogicalType.Float32;
                    return;
                case "double":
                case "Double":
                    column.Type = LogicalType.Float64;
                    return;
                case "decimal":
                case "Decimal":
                    column.Type = LogicalType.Decimal;
                    return;
                case "DateTime":
                    column.Type = LogicalType.DateTime;
                    return;
                case "DateTimeOffset":
                    column.Type = LogicalType.DateTimeOffset;
                    return;
                case "DateOnly":
                    column.Type = LogicalType.Date;
                    return;
                case "TimeOnly":
                    column.Type = LogicalType.Time;
                    return;
                case "Guid":
                    column.Type = LogicalType.Uuid;
                    return;
                case "byte[]":
                    column.Type = LogicalType.Binary;
                    return;
                case "string":
                case "String":
                    column.Type = column.Length.HasValue ? LogicalType.String : LogicalType.Text;
                    return;
            }

            column.Type = LogicalType.Text;
            result.Warnings.Add("属性 " + column.Name + " 的类型 " + clrType + " 无法精确映射，按 Text 处理");
        }

        private static bool IsScalarType(string typeName)
        {
            var name = typeName.Replace("?", "").Replace(" ", "");
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                return name == "byte[]";
            }

            switch (name)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "String":
                case "DateTime":
                case "DateTimeOffset":
                case "DateOnly":
                case "TimeOnly":
                case "Guid":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsReferenceType(string clrType)
        {
            return clrType == "string" || clrType == "String" || clrType == "byte[]";
        }

        private static string GetTableName(ClassDeclarationSyntax classDecl, List<AttributeSyntax> attributes)
        {
            var name = GetAttributeString(attributes, "Name", "TableName");
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            // [Table("nor_user")] 的构造参数
            foreach (var attribute in attributes)
            {
                if (!EntityAttributes.Contains(AttributeName(attribute)) || attribute.ArgumentList == null)
                {
                    continue;
                }
                foreach (var argument in attribute.ArgumentList.Arguments)
                {
                    var value = ArgumentValue(argument);
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }

            return classDecl.Identifier.ValueText;
        }

        /// <summary>只认命名参数（Comment / Description），不把 [Table("x")] 的位置参数误当注释。</summary>
        private static string GetComment(List<AttributeSyntax> attributes)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.ArgumentList == null)
                {
                    continue;
                }

                foreach (var argument in attribute.ArgumentList.Arguments)
                {
                    if (argument.NameEquals == null)
                    {
                        continue;
                    }

                    var target = argument.NameEquals.Name.Identifier.ValueText;
                    if (string.Equals(target, "Comment", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(target, "Description", StringComparison.OrdinalIgnoreCase))
                    {
                        return ArgumentValue(argument);
                    }
                }
            }

            return null;
        }

        private static string AttributeName(AttributeSyntax attribute)
        {
            var text = attribute.Name.ToString();
            var dot = text.LastIndexOf('.');
            return dot >= 0 ? text.Substring(dot + 1) : text;
        }

        private static bool HasAttribute(List<AttributeSyntax> attributes, params string[] names)
        {
            foreach (var attribute in attributes)
            {
                var name = AttributeName(attribute);
                foreach (var candidate in names)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, candidate + "Attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string GetAttributeString(List<AttributeSyntax> attributes, params string[] names)
        {
            foreach (var attribute in attributes)
            {
                var value = GetAttributeString(attribute, names);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return null;
        }

        private static string GetAttributeString(AttributeSyntax attribute, params string[] names)
        {
            if (attribute.ArgumentList == null)
            {
                return null;
            }

            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals != null)
                {
                    var target = argument.NameEquals.Name.Identifier.ValueText;
                    foreach (var candidate in names)
                    {
                        if (string.Equals(target, candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            return ArgumentValue(argument);
                        }
                    }
                }
            }

            // 无命名参数时取第一个位置参数（[Table("x")] / [Column("y")]）
            if (attribute.ArgumentList.Arguments.Count > 0)
            {
                return ArgumentValue(attribute.ArgumentList.Arguments[0]);
            }

            return null;
        }

        private static string ArgumentValue(AttributeArgumentSyntax argument)
        {
            var expression = argument.Expression;
            var literal = expression as LiteralExpressionSyntax;
            if (literal != null)
            {
                return literal.Token.ValueText;
            }

            var text = expression.ToString().Trim();
            if (text.StartsWith("nameof(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = text.Substring("nameof(".Length, text.Length - "nameof(".Length - 1);
                var dot = inner.LastIndexOf('.');
                return dot >= 0 ? inner.Substring(dot + 1) : inner;
            }

            return text;
        }

        private static int? GetAttributeInt(List<AttributeSyntax> attributes, params string[] names)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.ArgumentList == null)
                {
                    continue;
                }

                foreach (var argument in attribute.ArgumentList.Arguments)
                {
                    var matched = argument.NameEquals == null;
                    if (!matched)
                    {
                        var target = argument.NameEquals.Name.Identifier.ValueText;
                        foreach (var candidate in names)
                        {
                            if (string.Equals(target, candidate, StringComparison.OrdinalIgnoreCase))
                            {
                                matched = true;
                                break;
                            }
                        }
                    }

                    if (!matched)
                    {
                        continue;
                    }

                    var value = ArgumentValue(argument);
                    int number;
                    if (!string.IsNullOrEmpty(value) && int.TryParse(value, out number))
                    {
                        return number;
                    }
                }
            }
            return null;
        }

        private static bool GetAttributeBool(AttributeSyntax attribute, params string[] names)
        {
            if (attribute.ArgumentList == null)
            {
                return false;
            }

            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals == null)
                {
                    continue;
                }

                var target = argument.NameEquals.Name.Identifier.ValueText;
                foreach (var candidate in names)
                {
                    if (!string.Equals(target, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var value = ArgumentValue(argument);
                    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }
}