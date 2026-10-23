using System.Security.Cryptography;
using System.Text;
using DbChronicle.Domain.Operations;
namespace DbChronicle.Compilation.Dialects
{
    public sealed class MariaDbDialect : MySqlDialect
    {
        public MariaDbDialect(string version, DialectContext context)
            : base("mariadb", "mariadb", version, context)
        {
        }

        public override string CreateTable(CreateTableOperation operation)
        {
            string text = base.CreateTable(operation);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (ColumnDefinition column in operation.Columns)
            {
                if (column.Type.GetValueOrDefault() == LogicalType.Json)
                {
                    stringBuilder.Append('\n').Append(BuildJsonCheck(operation.Table, column.Name));
                }
            }
            return text + stringBuilder;
        }

        public override string AddColumn(ColumnOperation operation)
        {
            string text = base.AddColumn(operation);
            if (operation.Definition.Type.GetValueOrDefault() == LogicalType.Json)
            {
                text = text + "\n" + BuildJsonCheck(operation.Table, operation.Name);
            }
            return text;
        }

        private string BuildJsonCheck(string table, string column)
        {
            string text = "ck_" + table + "_" + column + "_json";
            if (text.Length > 64)
            {
                text = text.Substring(0, 55) + "_" + ShortHash(text);
            }
            return "ALTER TABLE " + Quote(table) + " ADD CONSTRAINT " + Quote(text) + " CHECK (" + Quote(column) + " IS NULL OR JSON_VALID(" + Quote(column) + "));";
        }

        private static string ShortHash(string value)
        {
            using SHA256 sHA = SHA256.Create();
            byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(value));
            StringBuilder stringBuilder = new StringBuilder(8);
            for (int i = 0; i < 4; i++)
            {
                stringBuilder.Append(array[i].ToString("x2"));
            }
            return stringBuilder.ToString();
        }
    }
}


