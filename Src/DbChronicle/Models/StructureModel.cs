using System;
using System.Collections.Generic;

namespace DbChronicle.Models
{
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
}