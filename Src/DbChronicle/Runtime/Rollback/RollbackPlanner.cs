using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Configuration;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Runtime.Rollback
{
    public sealed class RollbackStep
    {
        public ChangeUnit Unit { get; set; }

        public List<SqlStatement> Statements { get; } = new List<SqlStatement>();

        public List<string> Blockers { get; } = new List<string>();

        public bool IsBlocked
        {
            get { return Blockers.Count > 0; }
        }
    }

    public sealed class RollbackPlan
    {
        public RollbackPlan(string targetRelease)
        {
            TargetRelease = targetRelease;
        }

        public string TargetRelease { get; private set; }

        public List<RollbackStep> Steps { get; } = new List<RollbackStep>();

        public bool HasBlockers
        {
            get { return Steps.Any(x => x.IsBlocked); }
        }

        public string ToSql()
        {
            var sb = new StringBuilder();
            foreach (var step in Steps)
            {
                sb.Append("-- Rollback: ").AppendLine(step.Unit.Id);
                foreach (var blocker in step.Blockers)
                {
                    sb.Append("-- BLOCKED: ").AppendLine(blocker);
                }
                foreach (var statement in step.Statements)
                {
                    sb.AppendLine(statement.Text);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public sealed class RollbackPlanner
    {
        public RollbackPlan Build(IReadOnlyList<ChangeUnit> units, string targetRelease, ISqlDialect dialect)
        {
            var plan = new RollbackPlan(targetRelease);
            var ordered = units
                .OrderBy(x => x.Release ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                var unit = ordered[i];
                if (string.IsNullOrEmpty(unit.Release) || unit.IsBaseline)
                {
                    continue;
                }
                if (string.Compare(unit.Release, targetRelease, StringComparison.OrdinalIgnoreCase) <= 0)
                {
                    continue;
                }

                plan.Steps.Add(BuildStep(ordered, unit, dialect));
            }

            return plan;
        }

        private RollbackStep BuildStep(IReadOnlyList<ChangeUnit> units, ChangeUnit unit, ISqlDialect dialect)
        {
            dialect.Context.Begin(unit.Id);
            var model = new StructureModelBuilder().Build(units, unit.Id);
            var step = new RollbackStep { Unit = unit };

            for (var i = unit.Operations.Count - 1; i >= 0; i--)
            {
                var reverse = BuildReverse(unit.Operations[i], model, step, dialect);
                foreach (var operation in reverse)
                {
                    var sql = RollbackDispatcher.Dispatch(dialect, operation);
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        continue;
                    }

                    foreach (var text in SqlStatementSplitter.Split(sql, dialect.Name))
                    {
                        step.Statements.Add(new SqlStatement
                        {
                            Index = step.Statements.Count,
                            Text = text,
                            Hash = SqlStatement.ComputeHash(text)
                        });
                    }
                }
            }

            return step;
        }

        private IEnumerable<SchemaOperation> BuildReverse(SchemaOperation operation, StructureModel model, RollbackStep step, ISqlDialect dialect)
        {
            var column = operation as ColumnOperation;
            if (column != null)
            {
                switch (column.Action)
                {
                    case ColumnAction.Add:
                        return new SchemaOperation[]
                        {
                            new ColumnOperation
                            {
                                Table = operation.Table,
                                Action = ColumnAction.Drop,
                                Definition = new ColumnDefinition { Name = column.Name }
                            }
                        };

                    case ColumnAction.Modify:
                    case ColumnAction.Comment:
                        ColumnDefinition previous;
                        if (model.TryGetColumn(operation.Table, column.Name, out previous))
                        {
                            var restore = new ColumnOperation
                            {
                                Table = operation.Table,
                                Action = ColumnAction.Modify,
                                Definition = previous.Clone()
                            };

                            if (dialect.RequiresRebuild(restore))
                            {
                                step.Blockers.Add("列 " + operation.Table + "." + column.Name
                                    + " 的定义回滚在 " + dialect.Name + " 上需要整表重建，请手工执行 rebuild");
                                return new SchemaOperation[0];
                            }

                            return new SchemaOperation[] { restore };
                        }
                        step.Blockers.Add("无法恢复列 " + operation.Table + "." + column.Name + " 的历史定义（缺少变更链）");
                        return new SchemaOperation[0];

                    case ColumnAction.Rename:
                        return new SchemaOperation[]
                        {
                            new ColumnOperation
                            {
                                Table = operation.Table,
                                Action = ColumnAction.Rename,
                                Definition = new ColumnDefinition { Name = column.RenamedFrom },
                                RenamedFrom = column.Name
                            }
                        };

                    case ColumnAction.Drop:
                        step.Blockers.Add("删除列 " + operation.Table + "." + column.Name + " 无法自动回滚（数据已丢失）");
                        return new SchemaOperation[0];

                    default:
                        return new SchemaOperation[0];
                }
            }

            if (operation is CreateTableOperation)
            {
                return new SchemaOperation[] { new DropTableOperation { Table = operation.Table } };
            }

            if (operation is DropTableOperation)
            {
                step.Blockers.Add("删除表 " + operation.Table + " 无法自动回滚（数据已丢失）");
                return new SchemaOperation[0];
            }

            if (operation is RenameTableOperation)
            {
                var rename = (RenameTableOperation)operation;
                return new SchemaOperation[] { new RenameTableOperation { Table = rename.NewName, NewName = rename.Table } };
            }

            if (operation is CommentTableOperation)
            {
                return new SchemaOperation[] { new CommentTableOperation { Table = operation.Table, Comment = "" } };
            }

            var index = operation as IndexOperation;
            if (index != null)
            {
                if (index.Action == ObjectAction.Drop)
                {
                    step.Blockers.Add("删除索引 " + index.Name + " 无法自动回滚（定义已丢失）");
                    return new SchemaOperation[0];
                }
                if (index.Action == ObjectAction.Rename)
                {
                    return new SchemaOperation[]
                    {
                        new IndexOperation
                        {
                            Table = operation.Table,
                            Action = ObjectAction.Rename,
                            Name = index.NewName,
                            NewName = index.Name
                        }
                    };
                }
                return new SchemaOperation[] { new IndexOperation { Table = operation.Table, Action = ObjectAction.Drop, Name = index.Name } };
            }

            var foreignKey = operation as ForeignKeyOperation;
            if (foreignKey != null)
            {
                if (foreignKey.Action != ObjectAction.Add)
                {
                    step.Blockers.Add("外键 " + foreignKey.Name + " 的 DROP / MODIFY 无法自动回滚（原定义未进累积模型）");
                    return new SchemaOperation[0];
                }
                return new SchemaOperation[]
                {
                    new ForeignKeyOperation { Table = operation.Table, Action = ObjectAction.Drop, Name = foreignKey.Name }
                };
            }

            var primaryKey = operation as PrimaryKeyOperation;
            if (primaryKey != null)
            {
                if (primaryKey.Action == ObjectAction.Drop)
                {
                    step.Blockers.Add("删除主键无法自动回滚（定义已丢失）");
                    return new SchemaOperation[0];
                }

                var reversePrimaryKey = new List<SchemaOperation>();
                reversePrimaryKey.Add(new PrimaryKeyOperation
                {
                    Table = operation.Table,
                    Action = ObjectAction.Drop,
                    Name = primaryKey.Name
                });

                TableModel previousTable;
                if (model.TryGetTable(operation.Table, out previousTable) && previousTable.PrimaryKeyColumns.Count > 0)
                {
                    var restore = new PrimaryKeyOperation
                    {
                        Table = operation.Table,
                        Action = ObjectAction.Add,
                        Name = primaryKey.Name
                    };
                    restore.Columns.AddRange(previousTable.PrimaryKeyColumns);
                    reversePrimaryKey.Add(restore);
                }

                return reversePrimaryKey;
            }

            var unique = operation as UniqueConstraintOperation;
            if (unique != null)
            {
                if (unique.Action == ObjectAction.Drop)
                {
                    step.Blockers.Add("删除唯一约束 " + unique.Name + " 无法自动回滚（定义已丢失）");
                    return new SchemaOperation[0];
                }
                return new SchemaOperation[]
                {
                    new UniqueConstraintOperation { Table = operation.Table, Action = ObjectAction.Drop, Name = unique.Name }
                };
            }

            var check = operation as CheckConstraintOperation;
            if (check != null)
            {
                if (check.Action == ObjectAction.Drop)
                {
                    step.Blockers.Add("删除检查约束 " + check.Name + " 无法自动回滚（定义已丢失）");
                    return new SchemaOperation[0];
                }
                return new SchemaOperation[]
                {
                    new CheckConstraintOperation { Table = operation.Table, Action = ObjectAction.Drop, Name = check.Name }
                };
            }

            var defaultConstraint = operation as DefaultConstraintOperation;
            if (defaultConstraint != null)
            {
                if (defaultConstraint.Action == ObjectAction.Drop)
                {
                    step.Blockers.Add("删除默认值约束 " + defaultConstraint.Name + " 无法自动回滚（定义已丢失）");
                    return new SchemaOperation[0];
                }
                return new SchemaOperation[]
                {
                    new DefaultConstraintOperation
                    {
                        Table = operation.Table,
                        Action = ObjectAction.Drop,
                        Name = defaultConstraint.Name,
                        Column = defaultConstraint.Column
                    }
                };
            }

            if (operation is DataMigrationOperation || operation is SeedOperation || operation is RawSqlOperation
                || operation is UpdateOperation || operation is DeleteOperation
                || operation is CleanOperation || operation is BackfillOperation)
            {
                step.Blockers.Add("数据操作 / 原生 SQL 无法自动回滚，需要手工处理");
                return new SchemaOperation[0];
            }

            return new SchemaOperation[0];
        }
    }

    internal static class RollbackDispatcher
    {
        public static string Dispatch(ISqlDialect dialect, SchemaOperation operation)
        {
            var column = operation as ColumnOperation;
            if (column != null)
            {
                switch (column.Action)
                {
                    case ColumnAction.Add:
                        return dialect.AddColumn(column);
                    case ColumnAction.Modify:
                        return dialect.ModifyColumn(column);
                    case ColumnAction.Drop:
                        return dialect.DropColumn(column);
                    case ColumnAction.Rename:
                        return dialect.RenameColumn(column);
                    case ColumnAction.Comment:
                        return dialect.CommentColumn(column);
                    case ColumnAction.Reorder:
                        return dialect.ReorderColumn(column);
                }
                return "";
            }

            if (operation is CreateTableOperation)
            {
                return dialect.CreateTable((CreateTableOperation)operation);
            }
            if (operation is DropTableOperation)
            {
                return dialect.DropTable((DropTableOperation)operation);
            }
            if (operation is RenameTableOperation)
            {
                return dialect.RenameTable((RenameTableOperation)operation);
            }
            if (operation is CommentTableOperation)
            {
                return dialect.CommentTable((CommentTableOperation)operation);
            }

            var index = operation as IndexOperation;
            if (index != null)
            {
                if (index.Action == ObjectAction.Drop)
                {
                    return dialect.DropIndex(index);
                }
                if (index.Action == ObjectAction.Rename)
                {
                    return dialect.RenameIndex(index);
                }
                return dialect.CreateIndex(index);
            }

            var foreignKey = operation as ForeignKeyOperation;
            if (foreignKey != null)
            {
                return foreignKey.Action == ObjectAction.Drop
                    ? dialect.DropForeignKey(foreignKey)
                    : dialect.AddForeignKey(foreignKey);
            }

            var primaryKey = operation as PrimaryKeyOperation;
            if (primaryKey != null)
            {
                return primaryKey.Action == ObjectAction.Drop
                    ? dialect.DropPrimaryKey(primaryKey)
                    : dialect.AddPrimaryKey(primaryKey);
            }

            var unique = operation as UniqueConstraintOperation;
            if (unique != null)
            {
                return unique.Action == ObjectAction.Drop
                    ? dialect.DropUniqueConstraint(unique)
                    : dialect.AddUniqueConstraint(unique);
            }

            var check = operation as CheckConstraintOperation;
            if (check != null)
            {
                return check.Action == ObjectAction.Drop
                    ? dialect.DropCheckConstraint(check)
                    : dialect.AddCheckConstraint(check);
            }

            var defaultConstraint = operation as DefaultConstraintOperation;
            if (defaultConstraint != null)
            {
                return defaultConstraint.Action == ObjectAction.Drop
                    ? dialect.DropDefaultConstraint(defaultConstraint)
                    : dialect.AddDefaultConstraint(defaultConstraint);
            }

            return "";
        }
    }
}
