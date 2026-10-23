using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

public abstract class SqlDialectBase : ISqlDialect
{
	public string Name { get; private set; }

	public string Version { get; private set; }

	public TypeMapper TypeMapper { get; private set; }

	public DialectContext Context { get; private set; }

	public abstract bool SupportsTransactionalDdl { get; }

	public abstract bool RequiresFullColumnDefinition { get; }

	protected SqlDialectBase(string name, string version, TypeMapper typeMapper, DialectContext context)
	{
		Name = name;
		Version = version;
		TypeMapper = typeMapper;
		Context = context;
	}

	public abstract string Quote(string identifier);

	public abstract string EscapeString(string value);

	public abstract string CreateTable(CreateTableOperation operation);

	public abstract string DropTable(DropTableOperation operation);

	public abstract string RenameTable(RenameTableOperation operation);

	public abstract string CommentTable(CommentTableOperation operation);

	public abstract string AddColumn(ColumnOperation operation);

	public abstract string ModifyColumn(ColumnOperation operation);

	public abstract string DropColumn(ColumnOperation operation);

	public abstract string RenameColumn(ColumnOperation operation);

	public abstract string CommentColumn(ColumnOperation operation);

	public abstract string ReorderColumn(ColumnOperation operation);

	public abstract string AddPrimaryKey(PrimaryKeyOperation operation);

	public abstract string DropPrimaryKey(PrimaryKeyOperation operation);

	public abstract string CreateIndex(IndexOperation operation);

	public abstract string DropIndex(IndexOperation operation);

	public abstract string RenameIndex(IndexOperation operation);

	public abstract string AddForeignKey(ForeignKeyOperation operation);

	public abstract string DropForeignKey(ForeignKeyOperation operation);

	public abstract string AddUniqueConstraint(UniqueConstraintOperation operation);

	public abstract string DropUniqueConstraint(UniqueConstraintOperation operation);

	public abstract string AddCheckConstraint(CheckConstraintOperation operation);

	public abstract string DropCheckConstraint(CheckConstraintOperation operation);

	public abstract string AddDefaultConstraint(DefaultConstraintOperation operation);

	public abstract string DropDefaultConstraint(DefaultConstraintOperation operation);

	public abstract string RawSql(RawSqlOperation operation);

	public virtual string Update(UpdateOperation operation)
	{
		string text = "UPDATE " + Quote(operation.Table) + " SET " + operation.Set;
		if (!string.IsNullOrEmpty(operation.Where))
		{
			text = text + " WHERE " + operation.Where;
		}
		return text + ";";
	}

	public virtual string Delete(DeleteOperation operation)
	{
		string text = "DELETE FROM " + Quote(operation.Table);
		if (!string.IsNullOrEmpty(operation.Where))
		{
			text = text + " WHERE " + operation.Where;
		}
		return text + ";";
	}

	public virtual string Clean(CleanOperation operation)
	{
		string text = (operation.Rules ?? "").Replace(";", ",");
		string text2 = "UPDATE " + Quote(operation.Table) + " SET " + text;
		if (!string.IsNullOrEmpty(operation.Where))
		{
			text2 = text2 + " WHERE " + operation.Where;
		}
		return text2 + ";";
	}

	public virtual string Backfill(BackfillOperation operation)
	{
		string text = "UPDATE " + Quote(operation.Table) + " SET " + Quote(operation.Column) + " = " + operation.Value;
		if (!string.IsNullOrEmpty(operation.Where))
		{
			text = text + " WHERE " + operation.Where;
		}
		return text + ";";
	}

	public virtual bool RequiresRebuild(SchemaOperation operation)
	{
		return false;
	}

	public virtual string RebuildTable(RebuildTableOperation operation)
	{
		return "";
	}

	protected bool VersionAtLeast(int major, int minor)
	{
		if (!System.Version.TryParse(Version, out var result))
		{
			return true;
		}
		if (result.Major > major)
		{
			return true;
		}
		return result.Major == major && result.Minor >= minor;
	}

	protected string JoinQuoted(IEnumerable<string> identifiers)
	{
		List<string> list = new List<string>();
		foreach (string identifier in identifiers)
		{
			list.Add(Quote(identifier));
		}
		return string.Join(", ", list);
	}

	protected string FormatDefaultValue(string value)
	{
		if (value == null)
		{
			return "NULL";
		}
		string text = value.Trim();
		if (text.Length == 0)
		{
			return "''";
		}
		if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
		{
			return "NULL";
		}
		if (text.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase) || text.StartsWith("NOW()", StringComparison.OrdinalIgnoreCase) || text.StartsWith("SYSUTCDATETIME", StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		if ((text.StartsWith("'") && text.EndsWith("'") && text.Length >= 2) || (text.StartsWith("(") && text.EndsWith(")")))
		{
			return text;
		}
		if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var _))
		{
			return text;
		}
		if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
		}
		return "'" + EscapeString(value) + "'";
	}

	protected void Warn(string code, string table, string column, string message)
	{
		Context.Warn(code, table, column, message);
	}

	protected void WarnIndexTypeUnsupported(IndexOperation operation)
	{
		if (!string.IsNullOrEmpty(operation.Type) && !string.Equals(operation.Type, "BTREE", StringComparison.OrdinalIgnoreCase) && !string.Equals(operation.Type, "HASH", StringComparison.OrdinalIgnoreCase))
		{
			Warn("W017", operation.Table, null, Name + " 不支持索引类型 " + operation.Type + "，已按普通索引生成");
		}
	}

	protected static string Append(params string[] parts)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string value in parts)
		{
			if (!string.IsNullOrEmpty(value))
			{
				stringBuilder.Append(value);
			}
		}
		return stringBuilder.ToString();
	}
}
