using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DbChronicle.Configuration;

namespace DbChronicle.Demo.Xml
{
    /// <summary>
    /// 演示用的变更单元生成器。真实项目应由开发者手写或用 CLI 的 new 命令生成。
    /// </summary>
    internal sealed class ChangeUnitXmlWriter
    {
        public IReadOnlyList<string> WriteSqliteScenarioUnits(DbChronicleConfig config)
        {
            var baseline = WriteIfMissing(config, "00000000_000000_baseline.xml",
                "  <table name=\"nor_user\" action=\"CREATE\" comment=\"用户表\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"false\" comment=\"用户名\"/>\n"
                + "    <column name=\"email\" action=\"ADD\" type=\"String\" length=\"128\" nullable=\"true\" defaultValue=\"''\" comment=\"邮箱\"/>\n"
                + "  </table>", "基线：用户表", "00000000");

            var change = WriteIfMissing(config, "20261022_100000_a1b2c3_adjust_email_and_phone.xml",
                "  <table name=\"nor_user\" action=\"ALTER\">\n"
                + "    <column name=\"email\" action=\"MODIFY\" length=\"200\"/>\n"
                + "    <column name=\"phone\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\" comment=\"手机号\"/>\n"
                + "  </table>", "调整邮箱长度并加手机号", "20261022");

            return new[] { baseline, change };
        }

        public string WriteMySqlAbnorMsgUnit(DbChronicleConfig config)
        {
            return WriteIfMissing(config, "20261022_180000_d7e8f9_add_test1_and_msg_text.xml",
                "  <table name=\"abnor_msg\" action=\"ALTER\">\n"
                + "    <column name=\"test1\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"true\"\n"
                + "            charset=\"utf8mb4\" collate=\"utf8mb4_general_ci\" defaultValue=\"''\"\n"
                + "            comment=\"演示字段：可空 + 字符集 + 排序规则 + 默认值 + 注释\"/>\n"
                + "    <column name=\"msg\" action=\"MODIFY\" type=\"Text\" comment=\"消息内容（TEXT）\"/>\n"
                + "  </table>", "abnor_msg 新增 test1，msg 改为 TEXT", "20261022");
        }

        public IReadOnlyList<string> WriteEmbeddedScenarioUnits(DbChronicleConfig config)
        {
            var baseline = WriteIfMissing(config, "00000000_000000_baseline.xml",
                "  <table name=\"dbchronicle_embedded_demo\" action=\"CREATE\" comment=\"嵌入式演示表\">\n"
                + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n"
                + "    <column name=\"code\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"false\" comment=\"编码\"/>\n"
                + "    <column name=\"payload\" action=\"ADD\" type=\"String\" length=\"128\" nullable=\"true\" comment=\"内容\"/>\n"
                + "    <column name=\"created_at\" action=\"ADD\" type=\"DateTime\" nullable=\"false\" defaultValue=\"CURRENT_TIMESTAMP\"/>\n"
                + "  </table>", "基线：嵌入式演示表", "00000000");

            var change = WriteIfMissing(config, "20261022_190000_e1f2a3_payload_text_and_retry.xml",
                "  <table name=\"dbchronicle_embedded_demo\" action=\"ALTER\">\n"
                + "    <column name=\"payload\" action=\"MODIFY\" type=\"Text\" comment=\"内容（TEXT）\"/>\n"
                + "    <column name=\"retry\" action=\"ADD\" type=\"Int32\" nullable=\"false\" defaultValue=\"0\" comment=\"重试次数\"/>\n"
                + "    <update set=\"retry = 0\" where=\"retry IS NULL\"/>\n"
                + "  </table>", "payload 改为 TEXT，新增 retry，并回填", "20261022");

            return new[] { baseline, change };
        }

        /// <summary>
        /// 已存在则复用（基线一旦应用就不能重新生成，否则 checksum 变化会被拒绝）。
        /// </summary>
        public string WriteIfMissing(DbChronicleConfig config, string fileName, string tablesXml, string description, string release)
        {
            var directory = config.ResolveMigrationsPath();
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                return path;
            }

            var name = fileName.Substring(0, fileName.Length - ".xml".Length);
            var parts = name.Split('_');
            var changeId = parts.Length >= 3
                ? parts[0] + "_" + parts[1] + "_" + parts[2]
                : name;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<changeUnit id=\"").Append(changeId).Append("\" author=\"demo\" release=\"").Append(release).Append("\">\n");
            sb.Append("  <description>").Append(description).Append("</description>\n");
            sb.Append(tablesXml).Append('\n');
            sb.Append("</changeUnit>\n");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
    }
}
