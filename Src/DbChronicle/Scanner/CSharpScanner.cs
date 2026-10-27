using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Scanner
{
    public sealed class CSharpScanner
    {
        private static readonly Regex LengthPattern = new Regex(@"\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\)", RegexOptions.IgnoreCase);

        public ScanResult ScanAssembly(string assemblyPath)
        {
            var result = new ScanResult();
            var assembly = Assembly.LoadFrom(assemblyPath);

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(x => x != null).ToArray();
                result.Warnings.Add("部分类型无法加载（依赖缺失），已跳过：" + ex.Message);
            }

            foreach (var type in types.OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                if (!IsEntity(type))
                {
                    continue;
                }

                var table = MapTable(type, result);
                if (table != null && table.Columns.Count > 0)
                {
                    result.Tables.Add(table);
                }
            }

            return result;
        }

        private readonly BaselineXmlWriter _xmlWriter = new BaselineXmlWriter();

        public string GenerateXml(ScanResult result, string author, string date)
        {
            return _xmlWriter.Generate(result, author, date, "C# 实体");
        }

        private static bool IsEntity(Type type)
        {
            if (type == null || !type.IsClass || type.IsAbstract || !type.IsPublic)
            {
                return false;
            }

            if (type.Name.Contains("<"))
            {
                return false;
            }

            foreach (var attribute in type.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.Name;
                if (name == "TableAttribute" || name == "TableNameAttribute" || name == "EntityAttribute")
                {
                    return true;
                }
            }

            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (baseType.Name.Contains("Entity"))
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }

            return false;
        }

        private ScannedTable MapTable(Type type, ScanResult result)
        {
            var table = new ScannedTable
            {
                Name = GetTableName(type),
                Comment = GetComment(type.GetCustomAttributesData())
            };

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || HasAttribute(property.GetCustomAttributesData(), "NotMapped"))
                {
                    continue;
                }

                if (!IsScalar(property.PropertyType))
                {
                    continue;
                }

                table.Columns.Add(MapColumn(property, result));
            }

            foreach (var attribute in type.GetCustomAttributesData())
            {
                if (!attribute.AttributeType.Name.Contains("Index"))
                {
                    continue;
                }

                var name = Convert.ToString(GetArgument(attribute, "Name", "IndexName", null));
                var columns = Convert.ToString(GetArgument(attribute, "ColumnNames", "Columns", null));
                if (string.IsNullOrEmpty(columns))
                {
                    continue;
                }

                var index = new ScannedIndex
                {
                    Name = string.IsNullOrEmpty(name) ? "idx_" + table.Name + "_" + columns.Replace(",", "_") : name,
                    Unique = Convert.ToBoolean(GetArgument(attribute, "IsUnique", "Unique", null) ?? false)
                };
                foreach (var column in columns.Split(','))
                {
                    var trimmed = column.Trim();
                    if (trimmed.Length > 0)
                    {
                        index.Columns.Add(trimmed);
                    }
                }
                table.Indexes.Add(index);
            }

            return table;
        }

        private ScannedColumn MapColumn(PropertyInfo property, ScanResult result)
        {
            var attributes = property.GetCustomAttributesData();
            var columnName = GetArgument(attributes, "Name", "ColumnName", null, true) as string;

            var column = new ScannedColumn
            {
                Name = string.IsNullOrEmpty(columnName) ? property.Name : columnName,
                Nullable = IsNullable(property.PropertyType),
                PrimaryKey = HasAttribute(attributes, "Key") || HasAttribute(attributes, "PrimaryKey") || property.Name == "Id",
                AutoIncrement = HasAttribute(attributes, "AutoIncrement") || HasAttribute(attributes, "Identity")
                    || HasAttribute(attributes, "DatabaseGenerated"),
                Comment = GetComment(attributes),
                DefaultValue = GetDefaultValue(attributes)
            };

            var maxLength = GetIntegerArgument(attributes, "MaxLength", "Length", "StringLength");
            if (maxLength.HasValue)
            {
                column.Length = maxLength;
            }

            MapClrType(property.PropertyType, column, attributes, result);
            return column;
        }

        private void MapClrType(Type type, ScannedColumn column, IList<CustomAttributeData> attributes, ScanResult result)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (underlying == typeof(bool))
            {
                column.Type = LogicalType.Boolean;
                return;
            }
            if (underlying == typeof(byte) || underlying == typeof(sbyte))
            {
                column.Type = LogicalType.Int8;
                column.Unsigned = underlying == typeof(byte);
                return;
            }
            if (underlying == typeof(short) || underlying == typeof(ushort))
            {
                column.Type = LogicalType.Int16;
                column.Unsigned = underlying == typeof(ushort);
                return;
            }
            if (underlying == typeof(int) || underlying == typeof(uint))
            {
                column.Type = LogicalType.Int32;
                column.Unsigned = underlying == typeof(uint);
                return;
            }
            if (underlying == typeof(long) || underlying == typeof(ulong))
            {
                column.Type = LogicalType.Int64;
                column.Unsigned = underlying == typeof(ulong);
                return;
            }
            if (underlying == typeof(float))
            {
                column.Type = LogicalType.Float32;
                return;
            }
            if (underlying == typeof(double))
            {
                column.Type = LogicalType.Float64;
                return;
            }
            if (underlying == typeof(decimal))
            {
                column.Type = LogicalType.Decimal;
                column.Precision = GetIntegerArgument(attributes, "Precision", null, null);
                column.Scale = GetIntegerArgument(attributes, "Scale", null, null);
                return;
            }
            if (underlying == typeof(DateTime))
            {
                column.Type = LogicalType.DateTime;
                return;
            }
            if (underlying == typeof(DateTimeOffset))
            {
                column.Type = LogicalType.DateTimeOffset;
                return;
            }
            if (underlying.Name == "DateOnly")
            {
                column.Type = LogicalType.Date;
                return;
            }
            if (underlying.Name == "TimeOnly")
            {
                column.Type = LogicalType.Time;
                return;
            }
            if (underlying == typeof(Guid))
            {
                column.Type = LogicalType.Uuid;
                return;
            }
            if (underlying == typeof(byte[]))
            {
                column.Type = column.Length.HasValue ? LogicalType.Binary : LogicalType.Blob;
                return;
            }
            if (underlying.IsEnum)
            {
                column.Type = LogicalType.Int32;
                result.Warnings.Add("枚举属性 " + column.Name + " 按 Int32 处理，请人工确认");
                return;
            }
            if (underlying == typeof(string))
            {
                MapStringType(column, attributes);
                return;
            }

            column.Type = LogicalType.Text;
            result.Warnings.Add("属性 " + column.Name + " 的类型 " + underlying.Name + " 无法精确映射，按 Text 处理");
        }

        private static void MapStringType(ScannedColumn column, IList<CustomAttributeData> attributes)
        {
            var typeName = Convert.ToString(GetArgument(attributes, "TypeName", null, null));
            if (!string.IsNullOrEmpty(typeName))
            {
                var upper = typeName.ToUpperInvariant();
                var match = LengthPattern.Match(typeName);
                if (upper.StartsWith("CHAR") && match.Success)
                {
                    column.Type = LogicalType.FixedString;
                    column.Length = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    return;
                }
                if (upper.StartsWith("VARCHAR") && match.Success)
                {
                    column.Type = LogicalType.String;
                    column.Length = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    return;
                }
                if (upper.Contains("JSON"))
                {
                    column.Type = LogicalType.Json;
                    return;
                }
                if (upper.Contains("XML"))
                {
                    column.Type = LogicalType.Xml;
                    return;
                }
                if (upper.Contains("TEXT"))
                {
                    column.Type = LogicalType.Text;
                    return;
                }
            }

            if (column.Length.HasValue)
            {
                column.Type = LogicalType.String;
                return;
            }

            column.Type = LogicalType.Text;
        }

        private static string GetTableName(Type type)
        {
            var name = GetArgument(type.GetCustomAttributesData(), "Name", "TableName", null, true) as string;
            return string.IsNullOrEmpty(name) ? type.Name : name;
        }

        private static string GetComment(IList<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                var typeName = attribute.AttributeType.Name;
                if (typeName.IndexOf("Comment", StringComparison.OrdinalIgnoreCase) < 0
                    && typeName.IndexOf("Description", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foreach (var argument in attribute.NamedArguments)
                {
                    if (IsName(argument.MemberName, "Comment", "Description", "Text"))
                    {
                        return Convert.ToString(argument.TypedValue.Value);
                    }
                }

                if (attribute.ConstructorArguments.Count > 0)
                {
                    return Convert.ToString(attribute.ConstructorArguments[0].Value);
                }
            }

            return null;
        }

        private static string GetDefaultValue(IList<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.Name.IndexOf("Default", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foreach (var argument in attribute.NamedArguments)
                {
                    if (IsName(argument.MemberName, "Value", "DefaultValue", null))
                    {
                        return Convert.ToString(argument.TypedValue.Value, CultureInfo.InvariantCulture);
                    }
                }

                if (attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value != null)
                {
                    return Convert.ToString(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        private static object GetArgument(IList<CustomAttributeData> attributes, string name1, string name2, string name3, bool allowCtorFallback = false)
        {
            foreach (var attribute in attributes)
            {
                var value = GetArgument(attribute, name1, name2, name3, allowCtorFallback);
                if (value != null)
                {
                    return value;
                }
            }
            return null;
        }

        private static object GetArgument(CustomAttributeData attribute, string name1, string name2, string name3, bool allowCtorFallback = false)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (IsName(argument.MemberName, name1, name2, name3))
                {
                    return argument.TypedValue.Value;
                }
            }

            if (allowCtorFallback && attribute.ConstructorArguments.Count > 0)
            {
                var first = attribute.ConstructorArguments[0].Value;
                if (first is string)
                {
                    return first;
                }
            }

            return null;
        }

        private static int? GetIntegerArgument(IList<CustomAttributeData> attributes, string name1, string name2, string name3)
        {
            foreach (var attribute in attributes)
            {
                foreach (var argument in attribute.NamedArguments)
                {
                    if (IsName(argument.MemberName, name1, name2, name3))
                    {
                        return ToInt(argument.TypedValue.Value);
                    }
                }

                foreach (var argument in attribute.ConstructorArguments)
                {
                    if (argument.ArgumentType == typeof(int) && argument.Value != null)
                    {
                        return ToInt(argument.Value);
                    }
                }
            }

            return null;
        }

        private static int? ToInt(object value)
        {
            if (value == null)
            {
                return null;
            }
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsName(string memberName, string name1, string name2, string name3)
        {
            return (!string.IsNullOrEmpty(name1) && string.Equals(memberName, name1, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(name2) && string.Equals(memberName, name2, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(name3) && string.Equals(memberName, name3, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAttribute(IList<CustomAttributeData> attributes, string namePart)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsScalar(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying.IsPrimitive || underlying.IsEnum)
            {
                return true;
            }

            return underlying == typeof(string)
                || underlying == typeof(decimal)
                || underlying == typeof(DateTime)
                || underlying == typeof(DateTimeOffset)
                || underlying == typeof(Guid)
                || underlying == typeof(byte[])
                || underlying.Name == "DateOnly"
                || underlying.Name == "TimeOnly";
        }

        private static bool IsNullable(Type type)
        {
            if (Nullable.GetUnderlyingType(type) != null)
            {
                return true;
            }
            return !type.IsValueType;
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}