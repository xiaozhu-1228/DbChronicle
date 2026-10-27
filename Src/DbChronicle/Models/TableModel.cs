using System.Collections.Generic;
using System;

using DbChronicle.Models.Operations;

namespace DbChronicle.Models
{
    public sealed class TableModel
    {
        public TableModel(string name)
        {
            Name = name;
        }

        public string Name { get; set; }

        /// <summary>表注释（来自建表语句或 COMMENT 操作），供代码生成与文档导出使用。</summary>
        public string Comment { get; set; }

        public Dictionary<string, ColumnDefinition> Columns { get; } =
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

        public List<string> ColumnOrder { get; } = new List<string>();

        public List<string> PrimaryKeyColumns { get; } = new List<string>();

        /// <summary>表级外键（按名称去重），供 SQLite rebuild 重放与结构比对使用。</summary>
        public List<ForeignKeyOperation> ForeignKeys { get; } = new List<ForeignKeyOperation>();

        /// <summary>表级检查约束（按名称去重），供 SQLite rebuild 重放使用。</summary>
        public List<CheckConstraintOperation> CheckConstraints { get; } = new List<CheckConstraintOperation>();

        public void SetCheckConstraint(CheckConstraintOperation constraint)
        {
            if (constraint == null || string.IsNullOrEmpty(constraint.Name))
            {
                return;
            }

            CheckConstraints.RemoveAll(x => string.Equals(x.Name, constraint.Name, StringComparison.OrdinalIgnoreCase));
            CheckConstraints.Add(new CheckConstraintOperation
            {
                Table = constraint.Table,
                Action = constraint.Action,
                Name = constraint.Name,
                Expression = constraint.Expression
            });
        }

        public void RemoveCheckConstraint(string name)
        {
            CheckConstraints.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void SetForeignKey(ForeignKeyOperation foreignKey)
        {
            if (foreignKey == null || string.IsNullOrEmpty(foreignKey.Name))
            {
                return;
            }

            ForeignKeys.RemoveAll(x => string.Equals(x.Name, foreignKey.Name, StringComparison.OrdinalIgnoreCase));
            ForeignKeys.Add(CloneForeignKey(foreignKey));
        }

        public void RemoveForeignKey(string name)
        {
            ForeignKeys.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static ForeignKeyOperation CloneForeignKey(ForeignKeyOperation source)
        {
            var clone = new ForeignKeyOperation
            {
                Table = source.Table,
                Action = source.Action,
                Name = source.Name,
                RefTable = source.RefTable,
                OnDelete = source.OnDelete,
                OnUpdate = source.OnUpdate,
                PartOfCreateTable = source.PartOfCreateTable
            };
            clone.Columns.AddRange(source.Columns);
            clone.RefColumns.AddRange(source.RefColumns);
            return clone;
        }

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
            var clone = new TableModel(Name) { Comment = Comment };
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
            foreach (var foreignKey in ForeignKeys)
            {
                clone.SetForeignKey(foreignKey);
            }
            foreach (var check in CheckConstraints)
            {
                clone.SetCheckConstraint(check);
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
}