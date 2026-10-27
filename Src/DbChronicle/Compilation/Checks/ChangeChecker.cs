using System.Collections.Generic;
using System.IO;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Checks
{
    public sealed class ChangeChecker
    {
        public IReadOnlyList<CompileDiagnostic> Check(IReadOnlyList<ChangeUnit> units)
        {
            var diagnostics = new List<CompileDiagnostic>();
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in units)
            {
                if (!knownIds.Add(unit.Id))
                {
                    diagnostics.Add(Error("E110", unit.Id, null, null, "变更单元 id 重复：" + unit.Id));
                }
            }

            var added = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var modified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var renamedTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var model = new StructureModel();
            var builder = new StructureModelBuilder();

            CheckCrossUnitOrder(units, diagnostics);
            CheckReleaseCrossDay(units, diagnostics);

            foreach (var unit in units)
            {
                foreach (var operation in unit.Operations)
                {
                    var column = operation as ColumnOperation;
                    if (column != null && column.Action == ColumnAction.Drop
                        && !model.TryGetColumn(operation.Table, column.Name, out _))
                    {
                        diagnostics.Add(Error("E106", unit.Id, operation.Table, column.Name, "DROP 不存在的列：变更链中没有该列"));
                    }
                }

                foreach (var depend in unit.Depends)
                {
                    if (!knownIds.Contains(depend))
                    {
                        diagnostics.Add(Error("E107", unit.Id, null, null, "depends 指向不存在的变更单元：" + depend));
                    }
                }

                if (IsPatchFile(unit) && unit.Depends.Count == 0)
                {
                    diagnostics.Add(Error("E108", unit.Id, null, null, "补丁文件必须用 depends 声明依赖的原变更单元"));
                }

                CheckRenameOrder(unit, diagnostics);

                foreach (var operation in unit.Operations)
                {
                    var column = operation as ColumnOperation;
                    if (column == null)
                    {
                        continue;
                    }

                    var key = operation.Table + "." + column.Name;
                    if (column.Action == ColumnAction.Add)
                    {
                        string owner;
                        if (added.TryGetValue(key, out owner))
                        {
                            diagnostics.Add(Error("E101", unit.Id, operation.Table, column.Name, "重复 ADD：该列已由 " + owner + " 添加"));
                        }
                        else
                        {
                            added[key] = unit.Id;
                        }
                    }
                    else if (column.Action == ColumnAction.Modify)
                    {
                        string owner;
                        if (modified.TryGetValue(key, out owner) && !string.Equals(owner, unit.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            diagnostics.Add(Error("E102", unit.Id, operation.Table, column.Name, "MODIFY 目标冲突：该列已被 " + owner + " 修改"));
                        }
                        else
                        {
                            modified[key] = unit.Id;
                        }
                    }
                    else if (column.Action == ColumnAction.Drop)
                    {
                        string owner;
                        if (added.TryGetValue(key, out owner) && !string.Equals(owner, unit.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            diagnostics.Add(Error("E103", unit.Id, operation.Table, column.Name, "ADD 与 DROP 顺序矛盾：该列已由 " + owner + " 添加，又被本单元删除"));
                        }
                    }
                    else if (column.Action == ColumnAction.Rename && !string.IsNullOrEmpty(column.RenamedFrom))
                    {
                        string owner;
                        if (renamedTargets.TryGetValue(key, out owner) && !string.Equals(owner, unit.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            diagnostics.Add(Error("E104", unit.Id, operation.Table, column.Name, "RENAME 目标冲突：该列名已被 " + owner + " 使用"));
                        }
                        else
                        {
                            renamedTargets[key] = unit.Id;
                        }
                    }
                }

                builder.Apply(model, unit);
            }

            return diagnostics;
        }

        private static void CheckCrossUnitOrder(IReadOnlyList<ChangeUnit> units, List<CompileDiagnostic> diagnostics)
        {
            var createIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < units.Count; i++)
            {
                foreach (var operation in units[i].Operations)
                {
                    var create = operation as CreateTableOperation;
                    if (create != null && !createIndex.ContainsKey(create.Table))
                    {
                        createIndex[create.Table] = i;
                    }
                }
            }

            var dropped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];

                foreach (var operation in unit.Operations)
                {
                    var create = operation as CreateTableOperation;
                    if (create != null)
                    {
                        dropped.Remove(create.Table);
                    }
                }

                foreach (var operation in unit.Operations)
                {
                    var foreignKey = operation as ForeignKeyOperation;
                    if (foreignKey != null
                        && foreignKey.Action != ObjectAction.Drop
                        && !string.IsNullOrEmpty(foreignKey.RefTable))
                    {
                        int createdIn;
                        if (createIndex.TryGetValue(foreignKey.RefTable, out createdIn) && createdIn > i)
                        {
                            diagnostics.Add(Error("E109", unit.Id, operation.Table, null,
                                "外键引用的表 " + foreignKey.RefTable + " 由更晚的变更单元创建，顺序颠倒"));
                        }

                        int droppedIn;
                        if (dropped.TryGetValue(foreignKey.RefTable, out droppedIn))
                        {
                            diagnostics.Add(Error("E109", unit.Id, operation.Table, null,
                                "外键引用的表 " + foreignKey.RefTable + " 已被更早的变更单元删除"));
                        }
                    }

                    var reference = operation.Table ?? "";
                    if (reference.Length > 0 && !(operation is CreateTableOperation))
                    {
                        int droppedIn;
                        if (dropped.TryGetValue(reference, out droppedIn))
                        {
                            diagnostics.Add(Error("E109", unit.Id, reference, null,
                                "该表已由更早的变更单元删除，后续单元不能再引用（如需重建请先 CREATE）"));
                        }
                    }
                }

                foreach (var operation in unit.Operations)
                {
                    var dropTable = operation as DropTableOperation;
                    if (dropTable != null)
                    {
                        dropped[dropTable.Table] = i;
                    }
                }
            }
        }

        private static void CheckReleaseCrossDay(IReadOnlyList<ChangeUnit> units, List<CompileDiagnostic> diagnostics)
        {
            var releaseDates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var releaseOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var unit in units)
            {
                if (unit.IsBaseline || string.IsNullOrEmpty(unit.Release) || string.IsNullOrEmpty(unit.Id) || unit.Id.Length < 8)
                {
                    continue;
                }

                var date = unit.Id.Substring(0, 8);
                HashSet<string> dates;
                if (!releaseDates.TryGetValue(unit.Release, out dates))
                {
                    dates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    releaseDates[unit.Release] = dates;
                    releaseOwner[unit.Release] = unit.Id;
                }

                dates.Add(date);
            }

            foreach (var pair in releaseDates)
            {
                if (pair.Value.Count > 1)
                {
                    diagnostics.Add(new CompileDiagnostic
                    {
                        Code = "W013",
                        Severity = DiagnosticSeverity.Warning,
                        ChangeId = releaseOwner[pair.Key],
                        Message = "同一 release(" + pair.Key + ") 的变更单元跨天提交：" + string.Join(", ", pair.Value)
                    });
                }
            }
        }

        private static void CheckRenameOrder(ChangeUnit unit, List<CompileDiagnostic> diagnostics)
        {
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var operation in unit.Operations)
            {
                var column = operation as ColumnOperation;
                if (column == null)
                {
                    continue;
                }

                var key = operation.Table + "." + column.Name;
                string renamedTo;
                if (column.Action == ColumnAction.Rename && !string.IsNullOrEmpty(column.RenamedFrom))
                {
                    renamed[operation.Table + "." + column.RenamedFrom] = column.Name;
                }
                else if (renamed.TryGetValue(key, out renamedTo))
                {
                    diagnostics.Add(Error("E105", unit.Id, operation.Table, column.Name, "该列已重命名为 " + renamedTo + "，后续操作请使用新名字"));
                }
            }
        }

        private static bool IsPatchFile(ChangeUnit unit)
        {
            if (string.IsNullOrEmpty(unit.SourcePath))
            {
                return false;
            }

            var fileName = Path.GetFileName(unit.SourcePath) ?? "";
            return fileName.Contains("_fix_") || fileName.Contains("_patch_") || fileName.StartsWith("fix_") || fileName.StartsWith("patch_");
        }

        private static CompileDiagnostic Error(string code, string changeId, string table, string column, string message)
        {
            return new CompileDiagnostic
            {
                Code = code,
                Severity = DiagnosticSeverity.Error,
                ChangeId = changeId,
                Table = table,
                Column = column,
                Message = message
            };
        }
    }
}