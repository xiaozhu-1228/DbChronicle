using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Domain.Operations;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Compilation.Dialects
{
    public interface ISqlDialect
    {
        string Name { get; }

        string Version { get; }

        TypeMapper TypeMapper { get; }

        DialectContext Context { get; }

        bool SupportsTransactionalDdl { get; }

        bool RequiresFullColumnDefinition { get; }

        string Quote(string identifier);

        string EscapeString(string value);

        string CreateTable(CreateTableOperation operation);

        string DropTable(DropTableOperation operation);

        string RenameTable(RenameTableOperation operation);

        string CommentTable(CommentTableOperation operation);

        string AddColumn(ColumnOperation operation);

        string ModifyColumn(ColumnOperation operation);

        string DropColumn(ColumnOperation operation);

        string RenameColumn(ColumnOperation operation);

        string CommentColumn(ColumnOperation operation);

        string ReorderColumn(ColumnOperation operation);

        string AddPrimaryKey(PrimaryKeyOperation operation);

        string DropPrimaryKey(PrimaryKeyOperation operation);

        string CreateIndex(IndexOperation operation);

        string DropIndex(IndexOperation operation);

        string RenameIndex(IndexOperation operation);

        string AddForeignKey(ForeignKeyOperation operation);

        string DropForeignKey(ForeignKeyOperation operation);

        string AddUniqueConstraint(UniqueConstraintOperation operation);

        string DropUniqueConstraint(UniqueConstraintOperation operation);

        string AddCheckConstraint(CheckConstraintOperation operation);

        string DropCheckConstraint(CheckConstraintOperation operation);

        string AddDefaultConstraint(DefaultConstraintOperation operation);

        string DropDefaultConstraint(DefaultConstraintOperation operation);

        string RawSql(RawSqlOperation operation);

        string Update(UpdateOperation operation);

        string Delete(DeleteOperation operation);

        string Clean(CleanOperation operation);

        string Backfill(BackfillOperation operation);

        bool RequiresRebuild(SchemaOperation operation);

        string RebuildTable(RebuildTableOperation operation);
    }
}
