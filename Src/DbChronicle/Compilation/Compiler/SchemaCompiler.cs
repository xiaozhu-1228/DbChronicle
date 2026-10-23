using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Model;
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
namespace DbChronicle.Compilation.Compiler;

public sealed class SchemaCompiler
{
	private readonly ISqlDialect _dialect;

	private readonly CompilerOptions _options;

	private readonly StructureModel _model;

	public const string WildcardTable = "*";

	public SchemaCompiler(ISqlDialect dialect, CompilerOptions options, StructureModel model)
	{
		_dialect = dialect;
		_options = options ?? new CompilerOptions();
		_model = model;
	}

	public CompileResult Compile(ChangeUnit unit)
	{
		_dialect.Context.Begin(unit.Id);
		_dialect.Context.IncrementalModify = _options.IncrementalModify;
		CompileResult compileResult = new CompileResult();
		compileResult.Diagnostics.AddRange(unit.Diagnostics);
		List<SchemaOperation> operations = ExpandWildcards(unit, compileResult);
		List<string> list = CollectRebuildTables(operations);
		StructureModel targetModel = ((list.Count > 0) ? BuildTargetModel(unit) : null);
		bool flag = list.Count == 0;
		IReadOnlyList<SchemaOperation> readOnlyList = Order(operations);
		foreach (SchemaOperation item in readOnlyList)
		{
			if (!flag && Rank(item) >= 40)
			{
				EmitRebuilds(unit, list, targetModel, compileResult);
				flag = true;
			}
			if (!ShouldSkipForRebuild(item, list))
			{
				AddStatements(compileResult, Dispatch(item));
			}
		}
		if (!flag)
		{
			EmitRebuilds(unit, list, targetModel, compileResult);
		}
		foreach (CompileDiagnostic diagnostic in _dialect.Context.Diagnostics)
		{
			compileResult.Diagnostics.Add(diagnostic);
		}
		compileResult.Sql = BuildScript(unit, compileResult);
		return compileResult;
	}

	private void AddStatements(CompileResult result, string sql)
	{
		if (string.IsNullOrWhiteSpace(sql))
		{
			return;
		}
		foreach (string item in SqlStatementSplitter.Split(sql, _dialect.Name, _options.StatementDelimiter))
		{
			result.Statements.Add(new SqlStatement
			{
				Index = result.Statements.Count,
				Text = item,
				Hash = SqlStatement.ComputeHash(item)
			});
		}
	}

	private List<SchemaOperation> ExpandWildcards(ChangeUnit unit, CompileResult result)
	{
		if (!unit.Operations.Any((SchemaOperation x) => IsWildcardTable(x.Table)))
		{
			return unit.Operations;
		}
		IReadOnlyList<string> readOnlyList = _options.WildcardTables ?? new List<string>();
		bool flag = false;
		List<SchemaOperation> list = new List<SchemaOperation>();
		foreach (SchemaOperation operation in unit.Operations)
		{
			if (!IsWildcardTable(operation.Table))
			{
				list.Add(operation);
				continue;
			}
			if (!IsWildcardSupported(operation))
			{
				result.Diagnostics.Add(new CompileDiagnostic
				{
					Code = "E041",
					Severity = DiagnosticSeverity.Error,
					ChangeId = unit.Id,
					Table = "*",
					Message = "table=\"*\" 不支持该操作（仅支持对已有表做 ALTER 类变更）"
				});
				continue;
			}
			List<string> list2 = ParseExplicitTables(operation.Tables);
			IReadOnlyList<string> readOnlyList2;
			if (list2.Count <= 0)
			{
				readOnlyList2 = readOnlyList;
			}
			else
			{
				IReadOnlyList<string> readOnlyList3 = list2;
				readOnlyList2 = readOnlyList3;
			}
			IReadOnlyList<string> readOnlyList4 = readOnlyList2;
			if (readOnlyList4.Count == 0)
			{
				list.Add(operation);
				if (!flag)
				{
					result.Diagnostics.Add(CompileDiagnostic.Warning("W022", "table=\"*\" 保留为占位符：离线 compile 无法确定表清单，将在 apply 连接数据库后按库中实际表展开（含 tables=\"...\" 显式清单除外）"));
					flag = true;
				}
				continue;
			}
			List<string> list3 = readOnlyList4.Where((string x) => !IsSystemTable(x)).ToList();
			foreach (string item in list3)
			{
				SchemaOperation schemaOperation = operation.Clone();
				schemaOperation.Table = item;
				schemaOperation.FromWildcard = true;
				list.Add(schemaOperation);
			}
			result.Diagnostics.Add(CompileDiagnostic.Warning("W021", "table=\"*\" 已展开为 " + list3.Count + " 张表：" + string.Join(", ", list3)));
		}
		return list;
	}

	private static bool IsSystemTable(string table)
	{
		return string.Equals(table, "dbchronicle_version_log", StringComparison.OrdinalIgnoreCase) || string.Equals(table, "dbchronicle_version_stmt_log", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsWildcardTable(string table)
	{
		return string.Equals((table ?? "").Trim(), "*", StringComparison.Ordinal);
	}

	private static List<string> ParseExplicitTables(string value)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(value))
		{
			return list;
		}
		string[] array = value.Split(new char[1] { ',' });
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.Length > 0 && !list.Contains(text2))
			{
				list.Add(text2);
			}
		}
		return list;
	}

	private static bool IsWildcardSupported(SchemaOperation operation)
	{
		return !(operation is CreateTableOperation) && !(operation is DropTableOperation) && !(operation is RenameTableOperation) && !(operation is RebuildTableOperation) && !(operation is RawSqlOperation) && !(operation is DataMigrationOperation);
	}

	private List<string> CollectRebuildTables(IEnumerable<SchemaOperation> operations)
	{
		List<string> list = new List<string>();
		foreach (SchemaOperation operation in operations)
		{
			if (!IsWildcardTable(operation.Table) && _dialect.RequiresRebuild(operation))
			{
				string table = operation.Table ?? "";
				if (!list.Any((string x) => string.Equals(x, table, StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(table);
				}
			}
		}
		return list;
	}

	private static bool ShouldSkipForRebuild(SchemaOperation operation, List<string> rebuildTables)
	{
		if (rebuildTables.Count == 0)
		{
			return false;
		}
		if (!rebuildTables.Any((string x) => string.Equals(x, operation.Table ?? "", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		return operation is ColumnOperation || operation is PrimaryKeyOperation || operation is ForeignKeyOperation || operation is UniqueConstraintOperation;
	}

	private StructureModel BuildTargetModel(ChangeUnit unit)
	{
		if (_model == null)
		{
			return null;
		}
		StructureModel structureModel = _model.Clone();
		new StructureModelBuilder().Apply(structureModel, unit);
		return structureModel;
	}

	private void EmitRebuilds(ChangeUnit unit, List<string> rebuildTables, StructureModel targetModel, CompileResult result)
	{
		foreach (string rebuildTable in rebuildTables)
		{
			TableModel table = null;
			if (targetModel == null || !targetModel.TryGetTable(rebuildTable, out table))
			{
				result.Diagnostics.Add(new CompileDiagnostic
				{
					Code = "W014",
					Severity = DiagnosticSeverity.Error,
					ChangeId = unit.Id,
					Table = rebuildTable,
					Message = "该表需要重建，但缺少完整表结构（请提供基线）"
				});
			}
			else
			{
				AddStatements(result, _dialect.RebuildTable(BuildRebuildOperation(unit, rebuildTable, table)));
			}
		}
	}

	private RebuildTableOperation BuildRebuildOperation(ChangeUnit unit, string table, TableModel tableModel)
	{
		RebuildTableOperation rebuildTableOperation = new RebuildTableOperation
		{
			Table = table
		};
		foreach (string item in tableModel.ColumnOrder)
		{
			if (tableModel.Columns.TryGetValue(item, out var value))
			{
				rebuildTableOperation.Columns.Add(value);
			}
		}
		rebuildTableOperation.PrimaryKeyColumns.AddRange(tableModel.PrimaryKeyColumns);
		if (_model != null && _model.TryGetTable(table, out var table2))
		{
			foreach (ColumnDefinition column in rebuildTableOperation.Columns)
			{
				if (table2.Columns.ContainsKey(column.Name))
				{
					rebuildTableOperation.ColumnMappings.Add(new KeyValuePair<string, string>(column.Name, column.Name));
				}
			}
			foreach (SchemaOperation operation in unit.Operations)
			{
				if (operation is ColumnOperation columnOperation && (string.Equals(columnOperation.Table ?? "", table, StringComparison.OrdinalIgnoreCase) || IsWildcardTable(columnOperation.Table)) && columnOperation.Action == ColumnAction.Rename && !string.IsNullOrEmpty(columnOperation.RenamedFrom) && table2.Columns.ContainsKey(columnOperation.RenamedFrom))
				{
					rebuildTableOperation.ColumnMappings.Add(new KeyValuePair<string, string>(columnOperation.RenamedFrom, columnOperation.Name));
				}
			}
		}
		return rebuildTableOperation;
	}

	private IReadOnlyList<SchemaOperation> Order(IReadOnlyList<SchemaOperation> operations)
	{
		Dictionary<string, int> tableOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < operations.Count; i++)
		{
			string key = operations[i].Table ?? "";
			if (!tableOrder.ContainsKey(key))
			{
				tableOrder[key] = i;
			}
		}
		return (from x in operations.Select((SchemaOperation operation, int index) => new
			{
				Operation = operation,
				Index = index,
				Rank = Rank(operation),
				TableIndex = tableOrder[operation.Table ?? ""]
			})
			orderby x.Rank, x.TableIndex, x.Index
			select x.Operation).ToList();
	}

	private static int Rank(SchemaOperation operation)
	{
		if (operation.FromWildcard || IsWildcardTable(operation.Table))
		{
			return -1000;
		}
		if (operation is DataMigrationOperation dataMigrationOperation)
		{
			return (dataMigrationOperation.Stage != 0) ? 40 : 0;
		}
		if (operation is SeedOperation seedOperation)
		{
			return (seedOperation.Stage != 0) ? 40 : 0;
		}
		if (operation is UpdateOperation || operation is DeleteOperation || operation is CleanOperation || operation is BackfillOperation)
		{
			return (operation.Stage != 0) ? 40 : 0;
		}
		if (operation is ForeignKeyOperation foreignKeyOperation)
		{
			return (foreignKeyOperation.Action == ObjectAction.Drop) ? 10 : 30;
		}
		if (operation is IndexOperation indexOperation)
		{
			return (indexOperation.Action == ObjectAction.Drop) ? 11 : 29;
		}
		if (operation is UniqueConstraintOperation uniqueConstraintOperation)
		{
			return (uniqueConstraintOperation.Action == ObjectAction.Drop) ? 12 : 26;
		}
		if (operation is CheckConstraintOperation checkConstraintOperation)
		{
			return (checkConstraintOperation.Action == ObjectAction.Drop) ? 13 : 27;
		}
		if (operation is DefaultConstraintOperation defaultConstraintOperation)
		{
			return (defaultConstraintOperation.Action == ObjectAction.Drop) ? 14 : 28;
		}
		if (operation is PrimaryKeyOperation primaryKeyOperation)
		{
			return (primaryKeyOperation.Action == ObjectAction.Drop) ? 15 : 25;
		}
		if (operation is ColumnOperation columnOperation)
		{
			return columnOperation.Action switch
			{
				ColumnAction.Drop => 16, 
				ColumnAction.Rename => 22, 
				ColumnAction.Modify => 23, 
				ColumnAction.Comment => 23, 
				ColumnAction.Add => 24, 
				ColumnAction.Reorder => 24, 
				_ => 24, 
			};
		}
		if (operation is DropTableOperation)
		{
			return 17;
		}
		if (operation is CreateTableOperation)
		{
			return 20;
		}
		if (operation is RenameTableOperation)
		{
			return 21;
		}
		if (operation is CommentTableOperation)
		{
			return 31;
		}
		return 50;
	}

	private string Dispatch(SchemaOperation operation)
	{
		if (operation is CreateTableOperation operation2)
		{
			return _dialect.CreateTable(operation2);
		}
		if (operation is DropTableOperation operation3)
		{
			return _dialect.DropTable(operation3);
		}
		if (operation is RenameTableOperation operation4)
		{
			return _dialect.RenameTable(operation4);
		}
		if (operation is CommentTableOperation operation5)
		{
			return _dialect.CommentTable(operation5);
		}
		if (operation is ColumnOperation operation6)
		{
			return DispatchColumn(operation6);
		}
		if (operation is PrimaryKeyOperation primaryKeyOperation)
		{
			if (primaryKeyOperation.Action == ObjectAction.Drop)
			{
				return _dialect.DropPrimaryKey(primaryKeyOperation);
			}
			if (primaryKeyOperation.Action == ObjectAction.Replace)
			{
				return _dialect.DropPrimaryKey(primaryKeyOperation) + "\n" + _dialect.AddPrimaryKey(primaryKeyOperation);
			}
			return _dialect.AddPrimaryKey(primaryKeyOperation);
		}
		if (operation is IndexOperation indexOperation)
		{
			if (indexOperation.Action == ObjectAction.Drop)
			{
				return _dialect.DropIndex(indexOperation);
			}
			if (indexOperation.Action == ObjectAction.Rename)
			{
				return _dialect.RenameIndex(indexOperation);
			}
			return _dialect.CreateIndex(indexOperation);
		}
		if (operation is ForeignKeyOperation foreignKeyOperation)
		{
			if (foreignKeyOperation.Action == ObjectAction.Drop)
			{
				return _dialect.DropForeignKey(foreignKeyOperation);
			}
			if (foreignKeyOperation.Action == ObjectAction.Replace || foreignKeyOperation.Action == ObjectAction.Modify)
			{
				return _dialect.DropForeignKey(foreignKeyOperation) + "\n" + _dialect.AddForeignKey(foreignKeyOperation);
			}
			return _dialect.AddForeignKey(foreignKeyOperation);
		}
		if (operation is UniqueConstraintOperation uniqueConstraintOperation)
		{
			return (uniqueConstraintOperation.Action == ObjectAction.Drop) ? _dialect.DropUniqueConstraint(uniqueConstraintOperation) : _dialect.AddUniqueConstraint(uniqueConstraintOperation);
		}
		if (operation is CheckConstraintOperation checkConstraintOperation)
		{
			return (checkConstraintOperation.Action == ObjectAction.Drop) ? _dialect.DropCheckConstraint(checkConstraintOperation) : _dialect.AddCheckConstraint(checkConstraintOperation);
		}
		if (operation is DefaultConstraintOperation defaultConstraintOperation)
		{
			return (defaultConstraintOperation.Action == ObjectAction.Drop) ? _dialect.DropDefaultConstraint(defaultConstraintOperation) : _dialect.AddDefaultConstraint(defaultConstraintOperation);
		}
		if (operation is RawSqlOperation operation7)
		{
			return _dialect.RawSql(operation7);
		}
		if (operation is DataMigrationOperation dataMigrationOperation)
		{
			if (dataMigrationOperation.SqlByDialect.TryGetValue(_dialect.Name, out var value) || dataMigrationOperation.SqlByDialect.TryGetValue("any", out value))
			{
				return value;
			}
			return "";
		}
		if (operation is UpdateOperation operation8)
		{
			return _dialect.Update(operation8);
		}
		if (operation is DeleteOperation operation9)
		{
			return _dialect.Delete(operation9);
		}
		if (operation is CleanOperation operation10)
		{
			return _dialect.Clean(operation10);
		}
		if (operation is BackfillOperation operation11)
		{
			return _dialect.Backfill(operation11);
		}
		if (operation is SeedOperation seed)
		{
			return BuildSeed(seed);
		}
		return "";
	}

	private string DispatchColumn(ColumnOperation operation)
	{
		ColumnDefinition columnDefinition = ResolveDefinition(operation);
		ColumnOperation operation2 = new ColumnOperation
		{
			Table = operation.Table,
			Action = operation.Action,
			Definition = columnDefinition,
			RenamedFrom = operation.RenamedFrom,
			After = operation.After,
			Comment = operation.Comment,
			Stage = operation.Stage
		};
		switch (operation.Action)
		{
		case ColumnAction.Add:
			return _dialect.AddColumn(operation2);
		case ColumnAction.Modify:
			if (!_options.IncrementalModify && !columnDefinition.DefaultValue.IsDeclared)
			{
				_dialect.Context.Warn("W008", operation.Table, operation.Name, "MODIFY 未声明 defaultValue，生成的列定义将没有默认值");
			}
			return _dialect.ModifyColumn(operation2);
		case ColumnAction.Drop:
			return _dialect.DropColumn(operation2);
		case ColumnAction.Rename:
			return _dialect.RenameColumn(operation2);
		case ColumnAction.Comment:
			return _dialect.CommentColumn(operation2);
		case ColumnAction.Reorder:
			return _dialect.ReorderColumn(operation2);
		default:
			return "";
		}
	}

	private ColumnDefinition ResolveDefinition(ColumnOperation operation)
	{
		if (operation.Action == ColumnAction.Add || operation.Action == ColumnAction.Drop)
		{
			return operation.Definition;
		}
		if (!_options.IncrementalModify)
		{
			return operation.Definition;
		}
		if (operation.Action == ColumnAction.Rename)
		{
			return operation.Definition;
		}
		string name = operation.Name;
		if (_model == null)
		{
			WarnIncrementalMissing(operation, name, "缺少基线或累积模型不可用");
			return operation.Definition;
		}
		if (!_model.TryGetColumn(operation.Table, name, out var definition))
		{
			WarnIncrementalMissing(operation, name, "该列不在变更链中");
			return operation.Definition;
		}
		return ColumnMerger.Merge(definition, operation.Definition);
	}

	private void WarnIncrementalMissing(ColumnOperation operation, string column, string reason)
	{
		_dialect.Context.Warn("W014", operation.Table, column, "无法补全未写属性（" + reason + "），该列按已写内容生成");
	}

	private string BuildSeed(SeedOperation seed)
	{
		if (seed.Rows.Count == 0 || seed.Columns.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (List<string> row in seed.Rows)
		{
			List<string> list2 = new List<string>();
			foreach (string item in row)
			{
				list2.Add(item);
			}
			list.Add("(" + string.Join(", ", list2) + ")");
		}
		List<string> list3 = new List<string>();
		foreach (string column in seed.Columns)
		{
			list3.Add(_dialect.Quote(column));
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("INSERT INTO ").Append(_dialect.Quote(seed.Table)).Append(" (")
			.Append(string.Join(", ", list3))
			.Append(") VALUES\n")
			.Append(string.Join(",\n", list));
		if (string.Equals(seed.Conflict, "UPDATE", StringComparison.OrdinalIgnoreCase) && _dialect.Name == "mysql")
		{
			List<string> list4 = new List<string>();
			foreach (string column2 in seed.Columns)
			{
				list4.Add(_dialect.Quote(column2) + " = VALUES(" + _dialect.Quote(column2) + ")");
			}
			stringBuilder.Append("\nON DUPLICATE KEY UPDATE ").Append(string.Join(", ", list4));
		}
		stringBuilder.Append(';');
		return stringBuilder.ToString();
	}

	private string BuildScript(ChangeUnit unit, CompileResult result)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (_options.IncludeComments)
		{
			stringBuilder.Append("-- ChangeUnit: ").AppendLine(unit.Id);
			stringBuilder.Append("-- Author: ").AppendLine(unit.Author ?? "");
			stringBuilder.Append("-- Release: ").AppendLine(unit.Release ?? "");
			stringBuilder.Append("-- Description: ").AppendLine(unit.Description ?? "");
			stringBuilder.Append("-- Dialect: ").Append(_dialect.Name).Append(' ')
				.AppendLine(_dialect.Version ?? "");
			List<CompileDiagnostic> list = result.Diagnostics.Where((CompileDiagnostic x) => x.Severity == DiagnosticSeverity.Warning).ToList();
			if (list.Count > 0)
			{
				List<string> values = (from x in list
					group x by x.Code into g
					select g.Key + " x" + g.Count()).ToList();
				stringBuilder.Append("-- WARNINGS: ").Append(list.Count).Append(" (")
					.Append(string.Join(", ", values))
					.AppendLine(")");
			}
			stringBuilder.AppendLine();
		}
		foreach (SqlStatement statement in result.Statements)
		{
			stringBuilder.Append(statement.Text);
			if (!statement.Text.TrimEnd(Array.Empty<char>()).EndsWith(";", StringComparison.Ordinal))
			{
				stringBuilder.Append(';');
			}
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString().TrimEnd(Array.Empty<char>()) + "\n";
	}
}
