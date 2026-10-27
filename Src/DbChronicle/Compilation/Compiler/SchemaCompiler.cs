using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Common;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Models.Operations;
using DbChronicle.Models;

namespace DbChronicle.Compilation.Compiler
{
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
                if (!flag && Rank(item) >= 26)
                {
                    EmitRebuilds(unit, list, targetModel, compileResult);
                    flag = true;
                }
                if (!ShouldSkipForRebuild(item, list))
                {
                    AddStatements(compileResult, Dispatch(item), item.Table);
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

        private void AddStatements(CompileResult result, string sql, string table)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return;
            }

            IReadOnlyList<string> statements;
            try
            {
                statements = SqlStatementSplitter.Split(sql, _dialect.Name, _options.StatementDelimiter);
            }
            catch (InvalidOperationException ex)
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Code = "E042",
                    Severity = DiagnosticSeverity.Error,
                    ChangeId = _dialect.Context.ChangeId,
                    Message = ex.Message
                });
                return;
            }

            var group = result.GroupFor(table);
            foreach (string item in statements)
            {
                var statement = new SqlStatement
                {
                    Index = result.Statements.Count,
                    Text = item,
                    Hash = SqlStatement.ComputeHash(item)
                };
                result.Statements.Add(statement);
                group.Statements.Add(statement);
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

        /// <summary>
        /// 旧版本曾在数据库中创建的版本表名：当前版本状态已改为本地文件，
        /// 但历史库可能仍留有这两张表，通配符展开时继续排除，避免误改。
        /// </summary>
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
            return operation is ColumnOperation || operation is PrimaryKeyOperation || operation is ForeignKeyOperation
                || operation is CheckConstraintOperation || operation is DefaultConstraintOperation;
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
                    result.Diagnostics.Add(CompileDiagnostic.Warning("W030",
                        "表 " + rebuildTable + " 走重建策略：不恢复历史索引（外键与检查约束由目标模型重放，需要时请在本单元重建索引或用 <rawSql>）"));
                    AddStatements(result, _dialect.RebuildTable(BuildRebuildOperation(unit, rebuildTable, table)), rebuildTable);
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
            foreach (ForeignKeyOperation foreignKey in tableModel.ForeignKeys)
            {
                rebuildTableOperation.ForeignKeys.Add(foreignKey);
            }
            foreach (CheckConstraintOperation check in tableModel.CheckConstraints)
            {
                rebuildTableOperation.CheckConstraints.Add(check);
            }
            if (_model != null && _model.TryGetTable(table, out var table2))
            {
                foreach (ColumnDefinition column in rebuildTableOperation.Columns)
                {
                    if (table2.Columns.ContainsKey(column.Name))
                    {
                        rebuildTableOperation.ColumnMappings.Add(new KeyValuePair<string, string>(column.Name, column.Name));
                    }

                    ColumnDefinition previousColumn;
                    if (table2.Columns.TryGetValue(column.Name, out previousColumn)
                        && previousColumn.Type.HasValue
                        && column.Type.HasValue)
                    {
                        rebuildTableOperation.ColumnSourceTypes[column.Name] = previousColumn.Type.Value;
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
            if (operation is UniqueConstraintOperation uniqueConstraintOperation)
            {
                return (uniqueConstraintOperation.Action == ObjectAction.Drop) ? 12 : 27;
            }
            if (operation is CheckConstraintOperation checkConstraintOperation)
            {
                return (checkConstraintOperation.Action == ObjectAction.Drop) ? 13 : 28;
            }
            if (operation is DefaultConstraintOperation defaultConstraintOperation)
            {
                return (defaultConstraintOperation.Action == ObjectAction.Drop) ? 14 : 29;
            }
            if (operation is PrimaryKeyOperation primaryKeyOperation)
            {
                return (primaryKeyOperation.Action == ObjectAction.Drop) ? 15 : 26;
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
                // 重命名表排在全部列操作之后：同单元内先改列（旧表名）再改名
                return 25;
            }
            if (operation is IndexOperation indexOperation)
            {
                if (indexOperation.Action == ObjectAction.Comment)
                {
                    return 33;
                }
                return (indexOperation.Action == ObjectAction.Drop) ? 11 : 30;
            }
            if (operation is ForeignKeyOperation foreignKeyOperation)
            {
                return (foreignKeyOperation.Action == ObjectAction.Drop) ? 10 : 31;
            }
            if (operation is CommentTableOperation)
            {
                return 32;
            }
            if (operation is ConstraintCommentOperation)
            {
                return 34;
            }
            if (operation is TableOptionOperation)
            {
                return 35;
            }
            if (operation is ViewOperation viewOperation)
            {
                // 视图依赖表结构，排在全部表级/列级变更之后
                return (viewOperation.Action == ObjectAction.Drop) ? 9 : 36;
            }
            if (operation is SequenceOperation sequenceOperation)
            {
                // 序列可能被建表的 DEFAULT nextval 引用，排在建表之前；删除排在最后
                return (sequenceOperation.Action == ObjectAction.Drop) ? 37 : 18;
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
                if (indexOperation.Action == ObjectAction.Comment)
                {
                    return _dialect.CommentIndex(indexOperation);
                }
                return _dialect.CreateIndex(indexOperation);
            }
            if (operation is ConstraintCommentOperation constraintCommentOperation)
            {
                return _dialect.CommentConstraint(constraintCommentOperation);
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
            if (operation is ViewOperation viewOperation)
            {
                return _dialect.View(viewOperation);
            }
            if (operation is SequenceOperation sequenceOperation)
            {
                return _dialect.Sequence(sequenceOperation);
            }
            if (operation is TableOptionOperation tableOptionOperation)
            {
                return _dialect.TableOption(tableOptionOperation);
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
                if (dataMigrationOperation.SqlByDialect.Count > 0)
                {
                    _dialect.Context.Warn("W033", dataMigrationOperation.Table, null,
                        "未提供 " + _dialect.Name + " 方言的数据迁移 SQL（节点只声明了 "
                        + string.Join(" / ", new List<string>(dataMigrationOperation.SqlByDialect.Keys)) + "），已跳过");
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
                return _dialect.BuildSeed(seed, PrimaryKeyColumnsOf(seed.Table));
            }
            return "";
        }

        private string DispatchColumn(ColumnOperation operation)
        {
            ColumnDefinition previousDefinition;
            ColumnDefinition columnDefinition = ResolveDefinition(operation, out previousDefinition);
            ColumnOperation operation2 = new ColumnOperation
            {
                Table = operation.Table,
                Action = operation.Action,
                Definition = columnDefinition,
                PreviousDefinition = previousDefinition,
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

        private ColumnDefinition ResolveDefinition(ColumnOperation operation, out ColumnDefinition previousDefinition)
        {
            previousDefinition = null;
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
            previousDefinition = definition;
            return ColumnMerger.Merge(definition, operation.Definition);
        }

        private void WarnIncrementalMissing(ColumnOperation operation, string column, string reason)
        {
            _dialect.Context.Warn("W014", operation.Table, column, "无法补全未写属性（" + reason + "），该列按已写内容生成");
        }

        /// <summary>累积模型中该表的主键列（供 SEED 等需要冲突目标的方言使用；无模型时返回空）。</summary>
        private List<string> PrimaryKeyColumnsOf(string table)
        {
            var result = new List<string>();
            TableModel tableModel;
            if (_model != null && _model.TryGetTable(table, out tableModel))
            {
                result.AddRange(tableModel.PrimaryKeyColumns);
            }
            return result;
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

}