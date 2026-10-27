using System;

using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Models.Operations;

namespace DbChronicle.Migrations.Rollback
{
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