using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Compilation.Dialects;

public sealed class TypeMapper
{
	private readonly Dictionary<LogicalType, string> _map;

	public string DialectName { get; private set; }

	private TypeMapper(string dialectName, Dictionary<LogicalType, string> map)
	{
		DialectName = dialectName;
		_map = map;
	}

	public static TypeMapper Load(string dialectName)
	{
		string text = (dialectName ?? "").ToLowerInvariant();
		string name = "DbChronicle.dialects." + text + ".types.xml";
		Assembly assembly = typeof(TypeMapper).GetTypeInfo().Assembly;
		using Stream stream = assembly.GetManifestResourceStream(name);
		if (stream == null)
		{
			throw new InvalidOperationException("未内置方言类型映射：" + dialectName);
		}
		XDocument xDocument = XDocument.Load(stream);
		Dictionary<LogicalType, string> dictionary = new Dictionary<LogicalType, string>();
		foreach (XElement item in xDocument.Root.Elements("type"))
		{
			string value = (string)item.Attribute("logical");
			string text2 = (string)item.Attribute("sql");
			if (!string.IsNullOrEmpty(value) && text2 != null && Enum.TryParse<LogicalType>(value, ignoreCase: true, out var result))
			{
				dictionary[result] = text2;
			}
		}
		return new TypeMapper(text, dictionary);
	}

	public bool Supports(LogicalType type)
	{
		return _map.ContainsKey(type);
	}

	public string Resolve(LogicalType type, int? length, int? precision, int? scale, bool unsigned)
	{
		if (!_map.TryGetValue(type, out var value))
		{
			throw new NotSupportedException("方言 " + DialectName + " 不支持类型：" + type);
		}
		string text = value.Replace("{length}", length.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)).Replace("{precision}", precision.GetValueOrDefault(18).ToString(CultureInfo.InvariantCulture)).Replace("{scale}", scale.GetValueOrDefault(2).ToString(CultureInfo.InvariantCulture));
		if (unsigned && (DialectName == "mysql" || DialectName == "mariadb"))
		{
			text += " UNSIGNED";
		}
		return text;
	}
}
