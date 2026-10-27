using Xunit;

namespace DbChronicle.Tests
{
    /// <summary>
    /// 同一变更单元内的操作排序：依赖顺序见方案 §4.2（RENAME 表排在全部列操作之后）。
    /// </summary>
    public class OrderingTests
    {
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
