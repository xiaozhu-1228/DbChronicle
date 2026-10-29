using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// 同一变更单元内的操作排序：依赖顺序见方案 §4.2（RENAME 表排在全部列操作之后）。
    /// </summary>
    public class OrderingTests
    {
        [Fact]
        public void Operations_ShouldBeGroupedByTableWithinSegment()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t2\" action=\"ALTER\">\n"
                + "    <column name=\"b\" action=\"ADD\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <index name=\"idx_a\" action=\"ADD\">\n"
                + "      <columnRef name=\"a\"/>\n"
                + "    </index>\n"
                + "  </table>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            var addA = result.Sql.IndexOf("ADD COLUMN `a`", System.StringComparison.Ordinal);
            var addB = result.Sql.IndexOf("ADD COLUMN `b`", System.StringComparison.Ordinal);
            var idxA = result.Sql.IndexOf("idx_a", System.StringComparison.Ordinal);
            Assert.True(addA >= 0 && addB >= 0 && idxA >= 0, "语句缺失：" + result.Sql);
            Assert.True(addA < idxA && idxA < addB, "同一张表的操作应连续执行：" + result.Sql);
        }

        [Fact]
        public void Operations_ShouldFinishWholeTableBeforeNextTable()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"old1\" action=\"DROP\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t2\" action=\"ALTER\">\n"
                + "    <column name=\"old2\" action=\"DROP\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t1\" action=\"ALTER\">\n"
                + "    <column name=\"a\" action=\"ADD\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <table name=\"t2\" action=\"ALTER\">\n"
                + "    <column name=\"b\" action=\"ADD\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>",
                true);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            var drop1 = result.Sql.IndexOf("`old1`", System.StringComparison.Ordinal);
            var addA = result.Sql.IndexOf("ADD COLUMN `a`", System.StringComparison.Ordinal);
            var drop2 = result.Sql.IndexOf("`old2`", System.StringComparison.Ordinal);
            var addB = result.Sql.IndexOf("ADD COLUMN `b`", System.StringComparison.Ordinal);
            Assert.True(drop1 >= 0 && addA >= 0 && drop2 >= 0 && addB >= 0, "语句缺失：" + result.Sql);
            Assert.True(drop1 < addA && addA < drop2 && drop2 < addB,
                "一张表的所有操作应连续执行完再进入下一张表：" + result.Sql);
        }

        [Fact]
        public void Sequence_ShouldPrecedeCreateTable()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <sequence name=\"seq_order\" action=\"CREATE\" start=\"1000\" increment=\"2\"/>",
                false);

            var result = TestSupport.CompileWith("postgresql", unit, false, null);

            var seq = result.Sql.IndexOf("seq_order", System.StringComparison.Ordinal);
            var table = result.Sql.IndexOf("CREATE TABLE", System.StringComparison.Ordinal);
            Assert.True(seq >= 0 && table >= 0, "语句缺失：" + result.Sql);
            Assert.True(seq < table, "建序列可能被建表引用，应排在前面：" + result.Sql);
        }

        [Fact]
        public void RawSql_ShouldBeClassifiedByKeyword()
        {
            var unit = TestSupport.ParseSingle(
                "  <rawSql name=\"stamp\">UPDATE nor_dbversion SET dbversion = 'x'</rawSql>\n"
                + "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"ADD\" type=\"String\" length=\"10\" nullable=\"true\"/>\n"
                + "  </table>\n"
                + "  <rawSql name=\"misc\">GRANT SELECT ON t TO someone</rawSql>",
                false);

            var result = TestSupport.CompileWith("mysql", unit, false, null);

            var column = result.Sql.IndexOf("ADD COLUMN", System.StringComparison.Ordinal);
            var stamp = result.Sql.IndexOf("UPDATE nor_dbversion", System.StringComparison.Ordinal);
            var misc = result.Sql.IndexOf("GRANT", System.StringComparison.Ordinal);
            Assert.True(column >= 0 && stamp >= 0 && misc >= 0, "语句缺失：" + result.Sql);
            Assert.True(column < stamp, "推断为 DML 的 rawSql 应排在结构变更之后：" + result.Sql);
            Assert.True(stamp < misc, "未识别的 rawSql 仍排在最后：" + result.Sql);
        }

        [Fact]
        public void RenameTable_ShouldRunAfterColumnOperations()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"t\" action=\"RENAME\" newName=\"t2\"/>\n"
                + "  <table name=\"t\" action=\"ALTER\">\n"
                + "    <column name=\"c\" action=\"MODIFY\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.Compile(unit, false, null);

            var modifyIndex = result.Sql.IndexOf("MODIFY COLUMN", System.StringComparison.Ordinal);
            var renameIndex = result.Sql.IndexOf("RENAME TABLE", System.StringComparison.Ordinal);
            Assert.True(modifyIndex >= 0, "未生成 MODIFY 语句");
            Assert.True(renameIndex >= 0, "未生成 RENAME 语句");
            Assert.True(modifyIndex < renameIndex, "列操作应排在重命名表之前");
        }

        [Fact]
        public void CreateTable_ShouldRunBeforeAddForeignKey()
        {
            var unit = TestSupport.ParseSingle(
                "  <table name=\"nor_user\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "  </table>\n"
                + "  <table name=\"nor_order\" action=\"CREATE\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"user_id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                + "    <foreignKey name=\"fk_order_user\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\"/>\n"
                + "  </table>",
                false);

            var result = TestSupport.Compile(unit, false, null);

            var createIndex = result.Sql.IndexOf("CREATE TABLE IF NOT EXISTS `nor_order`", System.StringComparison.Ordinal);
            var foreignKeyIndex = result.Sql.IndexOf("ADD CONSTRAINT `fk_order_user`", System.StringComparison.Ordinal);
            Assert.True(createIndex >= 0 && foreignKeyIndex >= 0);
            Assert.True(createIndex < foreignKeyIndex, "建表应排在加外键之前");
        }
    }
}
