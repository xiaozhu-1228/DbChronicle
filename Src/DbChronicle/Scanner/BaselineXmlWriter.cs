using System.Globalization;
using System.Text;

using DbChronicle.Models;

namespace DbChronicle.Scanner
{
    /// <summary>
    /// 把 <see cref="ScanResult"/> 序列化为基线变更单元 XML。
    /// C# 实体扫描与数据库反向扫描共用，保证两种来源生成的基线格式一致。
    /// </summary>
    public sealed class BaselineXmlWriter
    {
        public string Generate(ScanResult result, string author, string date, string source)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<!--\n");
            sb.Append("  【基线文件】已冻结，禁止修改。\n");
            sb.Append("  此文件由 ").Append(Escape(source)).Append(" 反向生成，请人工确认后冻结。\n");
            sb.Append("  后续所有结构变更请新增增量变更单元文件。\n");
            sb.Append("-->\n");
            sb.Append("<changeUnit id=\"00000000_000000_baseline\" author=\"").Append(Escape(author))
              .Append("\" release=\"00000000\">\n");
            sb.Append("  <description>基线：从 ").Append(Escape(source)).Append(" 反向生成（")
              .Append(Escape(date)).Append("）</description>\n");

            foreach (var table in result.Tables)
            {
                AppendTable(sb, table);
            }

            if (result.Warnings.Count > 0)
            {
                sb.Append('\n');
                sb.Append("  <!-- 扫描警告：\n");
                foreach (var warning in result.Warnings)
                {
                    sb.Append("       ").Append(XmlCommentSafe(warning)).Append('\n');
                }
                sb.Append("  -->\n");
            }

            sb.Append("</changeUnit>\n");
            return sb.ToString();
        }

        private void AppendTable(StringBuilder sb, ScannedTable table)
        {
            sb.Append('\n');
            sb.Append("  <table name=\"").Append(Escape(table.Name)).Append("\" action=\"CREATE\"");
            if (!string.IsNullOrEmpty(table.Comment))
            {
                sb.Append(" comment=\"").Append(Escape(table.Comment)).Append('"');
            }
            sb.Append(">\n");

            foreach (var column in table.Columns)
            {
                sb.Append("    <column").Append(BuildColumnAttributes(column)).Append("/>\n");
            }

            // 复合主键：列级 primaryKey 只标单列，多列主键额外用节点声明以保留顺序
            if (table.PrimaryKeyColumns.Count > 1)
            {
                sb.Append("    <primaryKey action=\"ADD\" columns=\"")
                  .Append(Escape(string.Join(", ", table.PrimaryKeyColumns))).Append("\"/>\n");
            }

            foreach (var unique in table.UniqueConstraints)
            {
                sb.Append("    <uniqueConstraint name=\"").Append(Escape(unique.Name)).Append("\" action=\"ADD\" columns=\"")
                  .Append(Escape(string.Join(", ", unique.Columns))).Append("\"/>\n");
            }

            foreach (var check in table.CheckConstraints)
            {
                sb.Append("    <checkConstraint name=\"").Append(Escape(check.Name)).Append("\" action=\"ADD\" expression=\"")
                  .Append(Escape(check.Expression)).Append("\"/>\n");
            }

            foreach (var index in table.Indexes)
            {
                sb.Append("    <index name=\"").Append(Escape(index.Name)).Append("\" action=\"ADD\"");
                if (index.Unique)
                {
                    sb.Append(" unique=\"true\"");
                }
                sb.Append(">\n");
                foreach (var column in index.Columns)
                {
                    sb.Append("      <columnRef name=\"").Append(Escape(column)).Append("\"/>\n");
                }
                sb.Append("    </index>\n");
            }

            foreach (var foreignKey in table.ForeignKeys)
            {
                sb.Append("    <foreignKey name=\"").Append(Escape(foreignKey.Name)).Append("\" action=\"ADD\"");
                sb.Append(" columns=\"").Append(Escape(string.Join(", ", foreignKey.Columns))).Append('"');
                sb.Append(" refTable=\"").Append(Escape(foreignKey.RefTable)).Append('"');
                sb.Append(" refColumns=\"").Append(Escape(string.Join(", ", foreignKey.RefColumns))).Append('"');
                if (!string.IsNullOrEmpty(foreignKey.OnDelete) && !string.Equals(foreignKey.OnDelete, "NO ACTION", System.StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(" onDelete=\"").Append(Escape(foreignKey.OnDelete)).Append('"');
                }
                if (!string.IsNullOrEmpty(foreignKey.OnUpdate) && !string.Equals(foreignKey.OnUpdate, "NO ACTION", System.StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(" onUpdate=\"").Append(Escape(foreignKey.OnUpdate)).Append('"');
                }
                sb.Append("/>\n");
            }

            sb.Append("  </table>\n");
        }

        private string BuildColumnAttributes(ScannedColumn column)
        {
            var sb = new StringBuilder();
            sb.Append(" name=\"").Append(Escape(column.Name)).Append('"');
            sb.Append(" action=\"ADD\"");
            sb.Append(" type=\"").Append(column.Type).Append('"');

            if (column.Length.HasValue)
            {
                sb.Append(" length=\"").Append(column.Length.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            }
            if (column.Precision.HasValue)
            {
                sb.Append(" precision=\"").Append(column.Precision.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            }
            if (column.Scale.HasValue)
            {
                sb.Append(" scale=\"").Append(column.Scale.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            sb.Append(" nullable=\"").Append(column.Nullable ? "true" : "false").Append('"');

            if (column.PrimaryKey)
            {
                sb.Append(" primaryKey=\"true\"");
            }
            if (column.AutoIncrement)
            {
                sb.Append(" autoIncrement=\"true\"");
            }
            if (column.Unsigned)
            {
                sb.Append(" unsigned=\"true\"");
            }
            if (!string.IsNullOrEmpty(column.DefaultValue))
            {
                sb.Append(" defaultValue=\"").Append(Escape(column.DefaultValue)).Append('"');
            }
            if (!string.IsNullOrEmpty(column.Comment))
            {
                sb.Append(" comment=\"").Append(Escape(column.Comment)).Append('"');
            }

            return sb.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string XmlCommentSafe(string value)
        {
            return (value ?? "").Replace("--", "- -");
        }
    }
}
