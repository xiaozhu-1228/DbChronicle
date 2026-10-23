using System;
using System.Collections.Generic;
using System.IO;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Compilation.Checking;

public sealed class ChangeChecker
{
	public IReadOnlyList<CompileDiagnostic> Check(IReadOnlyList<ChangeUnit> units)
	{
		List<CompileDiagnostic> list = new List<CompileDiagnostic>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ChangeUnit unit in units)
		{
			hashSet.Add(unit.Id);
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary4 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		StructureModel structureModel = new StructureModel();
		StructureModelBuilder structureModelBuilder = new StructureModelBuilder();
		foreach (ChangeUnit unit2 in units)
		{
			foreach (SchemaOperation operation in unit2.Operations)
			{
				if (operation is ColumnOperation { Action: ColumnAction.Drop } columnOperation && !structureModel.TryGetColumn(operation.Table, columnOperation.Name, out var _))
				{
					list.Add(Error(unit2.Id, operation.Table, columnOperation.Name, "DROP 不存在的列：变更链中没有该列"));
				}
			}
			foreach (string depend in unit2.Depends)
			{
				if (!hashSet.Contains(depend))
				{
					list.Add(Error(unit2.Id, null, null, "depends 指向不存在的变更单元：" + depend));
				}
			}
			if (IsPatchFile(unit2) && unit2.Depends.Count == 0)
			{
				list.Add(Error(unit2.Id, null, null, "补丁文件必须用 depends 声明依赖的原变更单元"));
			}
			CheckRenameOrder(unit2, list);
			foreach (SchemaOperation operation2 in unit2.Operations)
			{
				if (!(operation2 is ColumnOperation columnOperation2))
				{
					continue;
				}
				string key = operation2.Table + "." + columnOperation2.Name;
				if (columnOperation2.Action == ColumnAction.Add)
				{
					if (dictionary.TryGetValue(key, out var value))
					{
						list.Add(Error(unit2.Id, operation2.Table, columnOperation2.Name, "重复 ADD：该列已由 " + value + " 添加"));
					}
					else
					{
						dictionary[key] = unit2.Id;
					}
				}
				else if (columnOperation2.Action == ColumnAction.Modify)
				{
					if (dictionary2.TryGetValue(key, out var value2) && !string.Equals(value2, unit2.Id, StringComparison.OrdinalIgnoreCase))
					{
						list.Add(Error(unit2.Id, operation2.Table, columnOperation2.Name, "MODIFY 目标冲突：该列已被 " + value2 + " 修改"));
					}
					else
					{
						dictionary2[key] = unit2.Id;
					}
				}
				else if (columnOperation2.Action == ColumnAction.Drop)
				{
					if (dictionary.TryGetValue(key, out var value3) && !string.Equals(value3, unit2.Id, StringComparison.OrdinalIgnoreCase))
					{
						list.Add(Error(unit2.Id, operation2.Table, columnOperation2.Name, "ADD 与 DROP 顺序矛盾：该列已由 " + value3 + " 添加，又被本单元删除"));
					}
					dictionary3[key] = unit2.Id;
				}
				else if (columnOperation2.Action == ColumnAction.Rename && !string.IsNullOrEmpty(columnOperation2.RenamedFrom))
				{
					string key2 = operation2.Table + "." + columnOperation2.Name;
					if (dictionary4.TryGetValue(key2, out var value4) && !string.Equals(value4, unit2.Id, StringComparison.OrdinalIgnoreCase))
					{
						list.Add(Error(unit2.Id, operation2.Table, columnOperation2.Name, "RENAME 目标冲突：该列名已被 " + value4 + " 使用"));
					}
					else
					{
						dictionary4[key2] = unit2.Id;
					}
				}
			}
			structureModelBuilder.Apply(structureModel, unit2);
		}
		return list;
	}

	private static void CheckRenameOrder(ChangeUnit unit, List<CompileDiagnostic> diagnostics)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (SchemaOperation operation in unit.Operations)
		{
			if (operation is ColumnOperation columnOperation)
			{
				string key = operation.Table + "." + columnOperation.Name;
				string value;
				if (columnOperation.Action == ColumnAction.Rename && !string.IsNullOrEmpty(columnOperation.RenamedFrom))
				{
					string key2 = operation.Table + "." + columnOperation.RenamedFrom;
					dictionary[key2] = columnOperation.Name;
				}
				else if (dictionary.TryGetValue(key, out value))
				{
					diagnostics.Add(Error(unit.Id, operation.Table, columnOperation.Name, "该列已重命名为 " + value + "，后续操作请使用新名字"));
				}
			}
		}
	}

	private static bool IsPatchFile(ChangeUnit unit)
	{
		if (string.IsNullOrEmpty(unit.SourcePath))
		{
			return false;
		}
		string text = Path.GetFileName(unit.SourcePath) ?? "";
		return text.Contains("_fix_") || text.Contains("_patch_") || text.StartsWith("fix_") || text.StartsWith("patch_");
	}

	private static CompileDiagnostic Error(string changeId, string table, string column, string message)
	{
		return new CompileDiagnostic
		{
			Code = "E100",
			Severity = DiagnosticSeverity.Error,
			ChangeId = changeId,
			Table = table,
			Column = column,
			Message = message
		};
	}
}
