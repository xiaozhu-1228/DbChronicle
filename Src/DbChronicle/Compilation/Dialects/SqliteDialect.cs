using System;
using System.Collections.Generic;
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

public sealed class SqliteDialect : SqlDialectBase
{
	public override bool SupportsTransactionalDdl => true;

	public override bool RequiresFullColumnDefinition => true;

	public SqliteDialect(string version, DialectContext context)
		: base("sqlite", version, TypeMapper.Load("sqlite"), context)
	{
	}

	public override string Quote(string identifier)
	{
		return "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";
	}

	public override string EscapeString(string value)
	{
		return (value ?? "").Replace("'", "''");
	}

	public override string CreateTable(CreateTableOperation operation)
	{
		List<string> list = new List<string>();
		string text = FindAutoIncrementColumn(operation.Columns, operation.Table);
		foreach (ColumnDefinition column in operation.Columns)
		{
			list.Add("  " + BuildColumnDefinition(column, operation.Table, text));
		}
		List<string> list2 = new List<string>();
		foreach (string primaryKeyColumn in operation.PrimaryKeyColumns)
		{
			if (!string.Equals(primaryKeyColumn, text, StringComparison.OrdinalIgnoreCase))
			{
				list2.Add(primaryKeyColumn);
			}
		}
		if (list2.Count > 0)
		{
			list.Add("  PRIMARY KEY (" + JoinQuoted(list2) + ")");
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(operation.Table)).Append(" (\n");
		stringBuilder.Append(string.Join(",\n", list));
		stringBuilder.Append("\n);");
		if (!string.IsNullOrEmpty(operation.Comment))
		{
			Warn("W006", operation.Table, null, "SQLite 不支持表注释，已跳过");
		}
		return BuildTimeFormatHints(operation.Columns) + stringBuilder;
	}

	public override string DropTable(DropTableOperation operation)
	{
		return "DROP TABLE IF EXISTS " + Quote(operation.Table) + ";";
	}

	public override string RenameTable(RenameTableOperation operation)
	{
		return "ALTER TABLE " + Quote(operation.Table) + " RENAME TO " + Quote(operation.NewName) + ";";
	}

	public override string CommentTable(CommentTableOperation operation)
	{
		Warn("W006", operation.Table, null, "SQLite 不支持表注释，已跳过");
		return "";
	}

	public override string AddColumn(ColumnOperation operation)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("ALTER TABLE ").Append(Quote(operation.Table)).Append(" ADD COLUMN ")
			.Append(BuildColumnDefinition(operation.Definition, operation.Table, null))
			.Append(';');
		if (operation.Definition.Comment != null)
		{
			Warn("W007", operation.Table, operation.Name, "SQLite 不支持列注释，已跳过");
		}
		return BuildTimeFormatHints(new ColumnDefinition[1] { operation.Definition }) + stringBuilder;
	}

	public override string ModifyColumn(ColumnOperation operation)
	{
		Warn("W026", operation.Table, operation.Name, "SQLite 不支持 ALTER COLUMN，应走 rebuild");
		return "";
	}

	public override string DropColumn(ColumnOperation operation)
	{
		if (!VersionAtLeast(3, 35))
		{
			Warn("W026", operation.Table, operation.Name, "SQLite 3.35 以下不支持 DROP COLUMN，应走 rebuild");
			return "";
		}
		return "ALTER TABLE " + Quote(operation.Table) + " DROP COLUMN " + Quote(operation.Name) + ";";
	}

	public override string RenameColumn(ColumnOperation operation)
	{
		if (!VersionAtLeast(3, 25))
		{
			Warn("W026", operation.Table, operation.Name, "SQLite 3.25 以下不支持 RENAME COLUMN，应走 rebuild");
			return "";
		}
		return "ALTER TABLE " + Quote(operation.Table) + " RENAME COLUMN " + Quote(operation.RenamedFrom) + " TO " + Quote(operation.Name) + ";";
	}

	public override string CommentColumn(ColumnOperation operation)
	{
		Warn("W007", operation.Table, operation.Name, "SQLite 不支持列注释，已跳过");
		return "";
	}

	public override string ReorderColumn(ColumnOperation operation)
	{
		Warn("W010", operation.Table, operation.Name, "SQLite 不支持列顺序调整，已忽略");
		return "";
	}

	public override string AddPrimaryKey(PrimaryKeyOperation operation)
	{
		Warn("W026", operation.Table, null, "SQLite 不支持新增主键，应走 rebuild");
		return "";
	}

	public override string DropPrimaryKey(PrimaryKeyOperation operation)
	{
		Warn("W026", operation.Table, null, "SQLite 不支持删除主键，应走 rebuild");
		return "";
	}

	public override string CreateIndex(IndexOperation operation)
	{
		WarnIndexTypeUnsupported(operation);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("CREATE ").Append(operation.Unique ? "UNIQUE " : "").Append("INDEX IF NOT EXISTS ")
			.Append(Quote(operation.Name))
			.Append(" ON ")
			.Append(Quote(operation.Table))
			.Append(" (");
		if (!string.IsNullOrEmpty(operation.Expression))
		{
			stringBuilder.Append(operation.Expression);
		}
		else
		{
			stringBuilder.Append(JoinQuoted(operation.Columns));
		}
		stringBuilder.Append(')');
		if (!string.IsNullOrEmpty(operation.Where))
		{
			stringBuilder.Append(" WHERE ").Append(operation.Where);
		}
		stringBuilder.Append(';');
		return stringBuilder.ToString();
	}

	public override string DropIndex(IndexOperation operation)
	{
		return "DROP INDEX IF EXISTS " + Quote(operation.Name) + ";";
	}

	public override string RenameIndex(IndexOperation operation)
	{
		Warn("W022", operation.Table, null, "SQLite 不支持重命名索引，请改用 DROP + CREATE");
		return "";
	}

	public override string AddForeignKey(ForeignKeyOperation operation)
	{
		Warn("W026", operation.Table, null, "SQLite 的外键只能在建表时声明，应走 rebuild");
		return "";
	}

	public override string DropForeignKey(ForeignKeyOperation operation)
	{
		Warn("W026", operation.Table, null, "SQLite 不支持删除外键，应走 rebuild");
		return "";
	}

	public override string AddUniqueConstraint(UniqueConstraintOperation operation)
	{
		string identifier = (string.IsNullOrEmpty(operation.Name) ? ("uk_" + operation.Table + "_" + string.Join("_", operation.Columns)) : operation.Name);
		return "CREATE UNIQUE INDEX IF NOT EXISTS " + Quote(identifier) + " ON " + Quote(operation.Table) + " (" + JoinQuoted(operation.Columns) + ");";
	}

	public override string DropUniqueConstraint(UniqueConstraintOperation operation)
	{
		return "DROP INDEX IF EXISTS " + Quote(operation.Name) + ";";
	}

	public override string AddCheckConstraint(CheckConstraintOperation operation)
	{
		Warn("W025", operation.Table, null, "SQLite 不支持新增检查约束，已跳过");
		return "";
	}

	public override string DropCheckConstraint(CheckConstraintOperation operation)
	{
		Warn("W025", operation.Table, null, "SQLite 不支持删除检查约束，已跳过");
		return "";
	}

	public override string AddDefaultConstraint(DefaultConstraintOperation operation)
	{
		Warn("W025", operation.Table, operation.Column, "SQLite 不支持独立默认值约束，已跳过");
		return "";
	}

	public override string DropDefaultConstraint(DefaultConstraintOperation operation)
	{
		Warn("W025", operation.Table, operation.Column, "SQLite 不支持独立默认值约束，已跳过");
		return "";
	}

	public override string RawSql(RawSqlOperation operation)
	{
		if (operation.SqlByDialect.TryGetValue("sqlite", out var value) || operation.SqlByDialect.TryGetValue("any", out value))
		{
			return value;
		}
		return "";
	}

	public override bool RequiresRebuild(SchemaOperation operation)
	{
		if (operation is ColumnOperation columnOperation)
		{
			return columnOperation.Action switch
			{
				ColumnAction.Modify => true, 
				ColumnAction.Add => columnOperation.Definition.PrimaryKey.GetValueOrDefault() || columnOperation.Definition.Unique.GetValueOrDefault(), 
				ColumnAction.Drop => !VersionAtLeast(3, 35), 
				ColumnAction.Rename => !VersionAtLeast(3, 25), 
				_ => false, 
			};
		}
		if (operation is PrimaryKeyOperation)
		{
			return true;
		}
		return operation is ForeignKeyOperation;
	}

	public override string RebuildTable(RebuildTableOperation operation)
	{
		string identifier = operation.Table + "__rebuild";
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		string text = FindAutoIncrementColumn(operation.Columns, operation.Table);
		foreach (ColumnDefinition column in operation.Columns)
		{
			list2.Add("  " + BuildColumnDefinition(column, operation.Table, text));
		}
		List<string> list3 = new List<string>();
		foreach (string primaryKeyColumn in operation.PrimaryKeyColumns)
		{
			if (!string.Equals(primaryKeyColumn, text, StringComparison.OrdinalIgnoreCase))
			{
				list3.Add(primaryKeyColumn);
			}
		}
		if (list3.Count > 0)
		{
			list2.Add("  PRIMARY KEY (" + JoinQuoted(list3) + ")");
		}
		list.Add("DROP TABLE IF EXISTS " + Quote(identifier) + ";");
		list.Add("CREATE TABLE " + Quote(identifier) + " (\n" + string.Join(",\n", list2) + "\n);");
		if (operation.ColumnMappings.Count > 0)
		{
			List<string> list4 = new List<string>();
			List<string> list5 = new List<string>();
			foreach (KeyValuePair<string, string> columnMapping in operation.ColumnMappings)
			{
				list4.Add(Quote(columnMapping.Value));
				list5.Add(Quote(columnMapping.Key));
			}
			list.Add("INSERT INTO " + Quote(identifier) + " (" + string.Join(", ", list4) + ") SELECT " + string.Join(", ", list5) + " FROM " + Quote(operation.Table) + ";");
		}
		list.Add("DROP TABLE " + Quote(operation.Table) + ";");
		list.Add("ALTER TABLE " + Quote(identifier) + " RENAME TO " + Quote(operation.Table) + ";");
		return BuildTimeFormatHints(operation.Columns) + string.Join("\n", list);
	}

	private string BuildColumnDefinition(ColumnDefinition column, string table, string autoIncrementColumn)
	{
		if (!column.Type.HasValue)
		{
			throw new InvalidOperationException("列 " + table + "." + column.Name + " 缺少类型定义");
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(Quote(column.Name)).Append(' ').Append(ResolveType(column, table));
		if (string.Equals(column.Name, autoIncrementColumn, StringComparison.OrdinalIgnoreCase))
		{
			stringBuilder.Append(" PRIMARY KEY AUTOINCREMENT");
		}
		else if (column.Nullable == false)
		{
			stringBuilder.Append(" NOT NULL");
		}
		DefaultValueSpec defaultValueSpec = column.DefaultValue ?? DefaultValueSpec.Undeclared;
		if (defaultValueSpec.IsDeclared && !defaultValueSpec.IsNone)
		{
			stringBuilder.Append(" DEFAULT ").Append(defaultValueSpec.IsNullLiteral ? "NULL" : FormatDefaultValue(defaultValueSpec.RawValue));
		}
		if (!string.IsNullOrEmpty(column.Charset) || !string.IsNullOrEmpty(column.Collate))
		{
			Warn("W015", table, column.Name, "SQLite 不支持列级 charset / collate，已忽略");
		}
		if (!string.IsNullOrEmpty(column.ForeignKey))
		{
			string[] array = column.ForeignKey.Split(new char[1] { '.' });
			if (array.Length == 2)
			{
				stringBuilder.Append(" REFERENCES ").Append(Quote(array[0])).Append(" (")
					.Append(Quote(array[1]))
					.Append(")");
			}
			else
			{
				Warn("W016", table, column.Name, "列级 foreignKey 必须为 表名.列名，已忽略：" + column.ForeignKey);
			}
		}
		return stringBuilder.ToString();
	}

	private static string BuildTimeFormatHints(IEnumerable<ColumnDefinition> columns)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (ColumnDefinition column in columns)
		{
			if (column.Type.GetValueOrDefault() == LogicalType.DateTime)
			{
				stringBuilder.Append("-- ").Append(column.Name).AppendLine(" 列存储格式：ISO8601 无时区（如 2026-10-22T10:30:00），格式一致性由应用层保证");
			}
			else if (column.Type.GetValueOrDefault() == LogicalType.DateTimeOffset)
			{
				stringBuilder.Append("-- ").Append(column.Name).AppendLine(" 列存储格式：ISO8601 带偏移（如 2026-10-22T10:30:00+08:00），格式一致性由应用层保证");
			}
		}
		return stringBuilder.ToString();
	}

	private string FindAutoIncrementColumn(IEnumerable<ColumnDefinition> columns, string table)
	{
		foreach (ColumnDefinition column in columns)
		{
			if (!column.AutoIncrement.GetValueOrDefault())
			{
				continue;
			}
			if (column.Type.GetValueOrDefault() != LogicalType.Int64 || !column.PrimaryKey.GetValueOrDefault())
			{
				throw new InvalidOperationException("SQLite 自增列必须同时满足：类型为 Int64 且 primaryKey=true（表 " + table + "，列 " + column.Name + "）");
			}
			return column.Name;
		}
		return null;
	}

	private string ResolveType(ColumnDefinition column, string table)
	{
		LogicalType value = column.Type.Value;
		string result = base.TypeMapper.Resolve(value, column.Length, column.Precision, column.Scale, unsigned: false);
		if (column.Unsigned.GetValueOrDefault())
		{
			Warn("W001", table, column.Name, "unsigned 被 SQLite 忽略");
		}
		if (!string.IsNullOrEmpty(column.OnUpdate))
		{
			Warn("W002", table, column.Name, "onUpdate 被 SQLite 忽略，需要触发器实现");
		}
		if (value == LogicalType.Json || value == LogicalType.Xml)
		{
			Warn("W004", table, column.Name, value.ToString() + " 降级为 TEXT，格式一致性由应用层保证");
		}
		return result;
	}
}
