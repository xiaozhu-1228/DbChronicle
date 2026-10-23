using System;
using System.Collections.Generic;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;

namespace DbChronicle.Domain.Structures
{
    public sealed class TableModel
    {
        public TableModel(string name)
        {
            Name = name;
        }

        public string Name { get; set; }

        public Dictionary<string, ColumnDefinition> Columns { get; } =
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

        public List<string> ColumnOrder { get; } = new List<string>();

        public List<string> PrimaryKeyColumns { get; } = new List<string>();

        public void SetColumn(ColumnDefinition definition)
        {
            if (!Columns.ContainsKey(definition.Name))
            {
                ColumnOrder.Add(definition.Name);
            }
            Columns[definition.Name] = definition;
        }

        public void RemoveColumn(string name)
        {
            Columns.Remove(name);
            ColumnOrder.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            PrimaryKeyColumns.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }

        public TableModel Clone()
        {
            var clone = new TableModel(Name);
            foreach (var name in ColumnOrder)
            {
                ColumnDefinition definition;
                if (Columns.TryGetValue(name, out definition))
                {
                    clone.SetColumn(definition.Clone());
                }
            }
            foreach (var key in PrimaryKeyColumns)
            {
                clone.PrimaryKeyColumns.Add(key);
            }
            return clone;
        }

        public void RenameColumn(string from, string to)
        {
            ColumnDefinition definition;
            if (!Columns.TryGetValue(from, out definition))
            {
                return;
            }

            Columns.Remove(from);
            definition.Name = to;
            Columns[to] = definition;

            for (var i = 0; i < ColumnOrder.Count; i++)
            {
                if (string.Equals(ColumnOrder[i], from, StringComparison.OrdinalIgnoreCase))
                {
                    ColumnOrder[i] = to;
                }
            }

            for (var i = 0; i < PrimaryKeyColumns.Count; i++)
            {
                if (string.Equals(PrimaryKeyColumns[i], from, StringComparison.OrdinalIgnoreCase))
                {
                    PrimaryKeyColumns[i] = to;
                }
            }
        }
    }

    public sealed class StructureModel
    {
        public Dictionary<string, TableModel> Tables { get; } =
            new Dictionary<string, TableModel>(StringComparer.OrdinalIgnoreCase);

        public StructureModel Clone()
        {
            var clone = new StructureModel();
            foreach (var table in Tables.Values)
            {
                clone.Tables[table.Name] = table.Clone();
            }
            return clone;
        }

        public bool TryGetTable(string name, out TableModel table)
        {
            return Tables.TryGetValue(name ?? "", out table);
        }

        public bool TryGetColumn(string table, string column, out ColumnDefinition definition)
        {
            definition = null;
            TableModel tableModel;
            if (!Tables.TryGetValue(table ?? "", out tableModel))
            {
                return false;
            }
            return tableModel.Columns.TryGetValue(column ?? "", out definition);
        }
    }

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
                var table = new TableModel(create.Table);
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

    public static class ColumnMerger
    {
        public static ColumnDefinition Merge(ColumnDefinition baseDefinition, ColumnDefinition overlay)
        {
            var result = baseDefinition.Clone();
            result.Name = overlay.Name;

            if (overlay.Type.HasValue)
            {
                result.Type = overlay.Type;
            }
            if (overlay.Length.HasValue)
            {
                result.Length = overlay.Length;
            }
            if (overlay.Precision.HasValue)
            {
                result.Precision = overlay.Precision;
            }
            if (overlay.Scale.HasValue)
            {
                result.Scale = overlay.Scale;
            }
            if (overlay.Nullable.HasValue)
            {
                result.Nullable = overlay.Nullable;
            }
            if (overlay.DefaultValue != null && overlay.DefaultValue.IsDeclared)
            {
                result.DefaultValue = overlay.DefaultValue;
            }
            if (overlay.Comment != null)
            {
                result.Comment = overlay.Comment;
            }
            if (overlay.PrimaryKey.HasValue)
            {
                result.PrimaryKey = overlay.PrimaryKey;
            }
            if (overlay.AutoIncrement.HasValue)
            {
                result.AutoIncrement = overlay.AutoIncrement;
            }
            if (overlay.Unique.HasValue)
            {
                result.Unique = overlay.Unique;
            }
            if (overlay.Unsigned.HasValue)
            {
                result.Unsigned = overlay.Unsigned;
            }
            if (overlay.OnUpdate != null)
            {
                result.OnUpdate = overlay.OnUpdate;
            }
            if (overlay.Charset != null)
            {
                result.Charset = overlay.Charset;
            }
            if (overlay.Collate != null)
            {
                result.Collate = overlay.Collate;
            }
            if (overlay.ForeignKey != null)
            {
                result.ForeignKey = overlay.ForeignKey;
            }

            return result;
        }
    }
}
