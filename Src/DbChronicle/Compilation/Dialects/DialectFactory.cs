using System;
using System.Collections.Generic;

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

public static class DialectFactory
{
	public static IReadOnlyList<string> SupportedDialects => new string[5] { "mysql", "mariadb", "postgresql", "sqlserver", "sqlite" };

	public static ISqlDialect Create(string dialectName, string version, DialectContext context)
	{
		return (dialectName ?? "").Trim().ToLowerInvariant() switch
		{
			"mysql" => new MySqlDialect(DefaultIfEmpty(version, "8.0"), context), 
			"mariadb" => new MariaDbDialect(DefaultIfEmpty(version, "10.5"), context), 
			"postgresql" => new PostgreSqlDialect(DefaultIfEmpty(version, "14"), context), 
			"sqlserver" => new SqlServerDialect(DefaultIfEmpty(version, "2019"), context), 
			"sqlite" => new SqliteDialect(DefaultIfEmpty(version, "3.35"), context), 
			_ => throw new NotSupportedException("暂不支持该方言：" + dialectName), 
		};
	}

	public static bool IsSupported(string dialectName)
	{
		string text = (dialectName ?? "").Trim().ToLowerInvariant();
		int result;
		switch (text)
		{
		default:
			result = ((text == "sqlite") ? 1 : 0);
			break;
		case "mysql":
		case "mariadb":
		case "postgresql":
		case "sqlserver":
			result = 1;
			break;
		}
		return (byte)result != 0;
	}

	private static string DefaultIfEmpty(string value, string fallback)
	{
		return string.IsNullOrEmpty(value) ? fallback : value;
	}
}
