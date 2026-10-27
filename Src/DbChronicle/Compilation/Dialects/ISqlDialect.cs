using System.Collections.Generic;

using DbChronicle.Models.Operations;

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

        /// <summary>索引注释：MySQL / SQLite 不支持，PG 用 COMMENT ON INDEX，SQL Server 用扩展属性。</summary>
        string CommentIndex(IndexOperation operation);

        /// <summary>约束注释：MySQL / SQLite 不支持，PG 用 COMMENT ON CONSTRAINT，SQL Server 用扩展属性。</summary>
        string CommentConstraint(ConstraintCommentOperation operation);

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

        /// <summary>视图（第 8 类）：按 Action 生成 CREATE / CREATE OR REPLACE / DROP。</summary>
        string View(ViewOperation operation);

        /// <summary>序列（第 9 类）：仅 PG / SQL Server 有原生对象，其余方言报错。</summary>
        string Sequence(SequenceOperation operation);

        /// <summary>表选项（第 1 类 1.5–1.7）：ENGINE / CHARSET / COLLATE / TABLESPACE。</summary>
        string TableOption(TableOptionOperation operation);

        string RawSql(RawSqlOperation operation);

        string Update(UpdateOperation operation);

        string Delete(DeleteOperation operation);

        string Clean(CleanOperation operation);

        string Backfill(BackfillOperation operation);

        /// <summary>
        /// 生成种子数据 SQL。primaryKeyColumns 为累积模型中该表的主键列（可能为空），
        /// 供需要冲突目标（ON CONFLICT）的方言使用。
        /// </summary>
        string BuildSeed(SeedOperation operation, IReadOnlyList<string> primaryKeyColumns);

        bool RequiresRebuild(SchemaOperation operation);

        string RebuildTable(RebuildTableOperation operation);
    }
}