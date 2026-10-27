using System.Collections.Generic;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Common;
using DbChronicle.Models.Operations;

namespace DbChronicle.Models
{
    public sealed class StructureModelBuilder
    {
        public StructureModel Build(IEnumerable<ChangeUnit> orderedUnits, string beforeChangeId)
        {
            var model = new StructureModel();
            foreach (var unit in orderedUnits)
            {
                if (!string.IsNullOrEmpty(beforeChangeId)
                    && string.Equals(unit.Id, beforeChangeId, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                Apply(model, unit);
            }
            return model;
        }

        public void Apply(StructureModel model, ChangeUnit unit)
        {
            foreach (var operation in unit.Operations)
            {
                Apply(model, operation);
            }
        }

        private void Apply(StructureModel model, SchemaOperation operation)
        {
            var create = operation as CreateTableOperation;
            if (create != null)
            {
                var table = new TableModel(create.Table) { Comment = create.Comment };
                foreach (var columnDefinition in create.Columns)
                {
                    table.SetColumn(columnDefinition.Clone());
                }
                foreach (var key in create.PrimaryKeyColumns)
                {
                    table.PrimaryKeyColumns.Add(key);
                }
                model.Tables[create.Table] = table;
                return;
            }

            var column = operation as ColumnOperation;
            if (column != null)
            {
                ApplyColumn(model, column);
                return;
            }

            var dropTable = operation as DropTableOperation;
            if (dropTable != null)
            {
                model.Tables.Remove(dropTable.Table);
                return;
            }

            var renameTable = operation as RenameTableOperation;
            if (renameTable != null)
            {
                TableModel table;
                if (model.Tables.TryGetValue(renameTable.Table, out table))
                {
                    model.Tables.Remove(renameTable.Table);
                    table.Name = renameTable.NewName;
                    model.Tables[renameTable.NewName] = table;
                }
                return;
            }

            var primaryKey = operation as PrimaryKeyOperation;
            if (primaryKey != null)
            {
                TableModel table;
                if (model.Tables.TryGetValue(primaryKey.Table, out table))
                {
                    table.PrimaryKeyColumns.Clear();
                    if (primaryKey.Action != ObjectAction.Drop)
                    {
                        table.PrimaryKeyColumns.AddRange(primaryKey.Columns);
                    }
                }
            }

            var foreignKey = operation as ForeignKeyOperation;
            if (foreignKey != null)
            {
                TableModel table;
                if (!model.Tables.TryGetValue(foreignKey.Table, out table))
                {
                    if (foreignKey.Action == ObjectAction.Drop)
                    {
                        return;
                    }
                    table = new TableModel(foreignKey.Table);
                    model.Tables[foreignKey.Table] = table;
                }
                if (foreignKey.Action == ObjectAction.Drop)
                {
                    table.RemoveForeignKey(foreignKey.Name);
                }
                else
                {
                    table.SetForeignKey(foreignKey);
                }
            }

            var check = operation as CheckConstraintOperation;
            if (check != null)
            {
                TableModel table;
                if (!model.Tables.TryGetValue(check.Table, out table))
                {
                    if (check.Action == ObjectAction.Drop)
                    {
                        return;
                    }
                    table = new TableModel(check.Table);
                    model.Tables[check.Table] = table;
                }
                if (check.Action == ObjectAction.Drop)
                {
                    table.RemoveCheckConstraint(check.Name);
                }
                else
                {
                    table.SetCheckConstraint(check);
                }
            }

            var commentTable = operation as CommentTableOperation;
            if (commentTable != null)
            {
                TableModel table;
                if (model.Tables.TryGetValue(commentTable.Table, out table))
                {
                    table.Comment = commentTable.Comment;
                }
            }

            var defaultConstraint = operation as DefaultConstraintOperation;
            if (defaultConstraint != null && !string.IsNullOrEmpty(defaultConstraint.Column))
            {
                TableModel table;
                if (model.Tables.TryGetValue(defaultConstraint.Table, out table))
                {
                    ColumnDefinition targetColumn;
                    if (table.Columns.TryGetValue(defaultConstraint.Column, out targetColumn))
                    {
                        var updated = targetColumn.Clone();
                        updated.DefaultValue = (defaultConstraint.Action == ObjectAction.Drop)
                            ? DefaultValueSpec.None()
                            : DefaultValueSpec.Value(defaultConstraint.Value);
                        table.SetColumn(updated);
                    }
                }
            }
        }

        private void ApplyColumn(StructureModel model, ColumnOperation operation)
        {
            TableModel table;
            if (!model.Tables.TryGetValue(operation.Table, out table))
            {
                if (operation.Action != ColumnAction.Add)
                {
                    return;
                }
                table = new TableModel(operation.Table);
                model.Tables[operation.Table] = table;
            }

            switch (operation.Action)
            {
                case ColumnAction.Add:
                    table.SetColumn(operation.Definition.Clone());
                    break;

                case ColumnAction.Modify:
                    ColumnDefinition existing;
                    if (table.Columns.TryGetValue(operation.Name, out existing))
                    {
                        table.SetColumn(ColumnMerger.Merge(existing, operation.Definition));
                    }
                    else
                    {
                        table.SetColumn(operation.Definition.Clone());
                    }
                    break;

                case ColumnAction.Drop:
                    table.RemoveColumn(operation.Name);
                    break;

                case ColumnAction.Rename:
                    if (!string.IsNullOrEmpty(operation.RenamedFrom))
                    {
                        table.RenameColumn(operation.RenamedFrom, operation.Name);
                    }
                    break;

                case ColumnAction.Comment:
                    ColumnDefinition target;
                    if (table.Columns.TryGetValue(operation.Name, out target))
                    {
                        var updated = target.Clone();
                        if (operation.Comment != null)
                        {
                            updated.Comment = operation.Comment;
                        }
                        table.SetColumn(updated);
                    }
                    break;
            }
        }
    }
}