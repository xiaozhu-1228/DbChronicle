using System.Collections.Generic;
using System.Globalization;
using System;

using DbChronicle.Execution;

namespace DbChronicle.Demo.OperationTests
{
    internal sealed class ColumnInfo
    {
        public string Name { get; set; }

        public string RawType { get; set; }

        public long? Length { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }

        public bool Nullable { get; set; }

        public string Default { get; set; }

        public string Comment { get; set; }

        public string Extra { get; set; }

        public bool Unsigned { get; set; }

        public string Charset { get; set; }

        public string Collation { get; set; }
    }

    internal sealed class IndexInfo
    {
        public string Name { get; set; }

        public bool Unique { get; set; }

        public List<string> Columns { get; private set; } = new List<string>();

        public string Type { get; set; }

        public string Definition { get; set; }
    }

    internal sealed class ForeignKeyInfo
    {
        public string Name { get; set; }

        public List<string> Columns { get; private set; } = new List<string>();

        public string RefTable { get; set; }

        public List<string> RefColumns { get; private set; } = new List<string>();

        public string OnDelete { get; set; }

        public string OnUpdate { get; set; }
    }

    internal sealed class SchemaProbe
    {
        private readonly IDbExecutor _executor;

        public SchemaProbe(IDbExecutor executor, string dialect)
        {
            _executor = executor;
            Dialect = dialect;
        }

        public string Dialect { get; private set; }

        public string Quote(string identifier)
        {
            switch (Dialect)
            {
                case "mysql":
                    return "`" + (identifier ?? "").Replace("`", "``") + "`";

                case "sqlserver":
                    return "[" + (identifier ?? "").Replace("]", "]]") + "]";

                default:
                    return "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";
            }
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            return _executor.Query(sql);
        }

        public object Scalar(string sql)
        {
            return _executor.Scalar(sql);
        }

        public long Count(string sql)
        {
            var value = Scalar(sql);
            if (value == null)
            {
                return 0;
            }

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        public long RowCount(string table)
        {
            return Count("SELECT COUNT(*) FROM " + Quote(table));
        }

        public bool TableExists(string table)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Count("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
                        + " AND TABLE_NAME = " + Literal(table) + " AND TABLE_TYPE = 'BASE TABLE'") > 0;

                case "postgresql":
                    return Count("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema()"
                        + " AND table_name = " + Literal(table) + " AND table_type = 'BASE TABLE'") > 0;

                default:
                    return Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = " + Literal(table)) > 0;
            }
        }

        public string TableEngine(string table)
        {
            if (Dialect != "mysql")
            {
                return null;
            }

            return Text("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
                + " AND TABLE_NAME = " + Literal(table));
        }

        public string TableCollation(string table)
        {
            if (Dialect != "mysql")
            {
                return null;
            }

            return Text("SELECT TABLE_COLLATION FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
                + " AND TABLE_NAME = " + Literal(table));
        }

        public string TableComment(string table)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Text("SELECT TABLE_COMMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
                        + " AND TABLE_NAME = " + Literal(table));

                case "postgresql":
                    return Text("SELECT obj_description(" + Literal(table) + "::regclass, 'pg_class')");

                default:
                    return null;
            }
        }

        public ColumnInfo Column(string table, string column)
        {
            switch (Dialect)
            {
                case "mysql":
                    return MySqlColumn(table, column);

                case "postgresql":
                    return PostgreSqlColumn(table, column);

                default:
                    return SqliteColumn(table, column);
            }
        }

        public bool ColumnExists(string table, string column)
        {
            return Column(table, column) != null;
        }

        public List<string> ColumnNames(string table)
        {
            var result = new List<string>();
            switch (Dialect)
            {
                case "mysql":
                    foreach (var row in Query("SELECT COLUMN_NAME AS n FROM information_schema.COLUMNS"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table) + " ORDER BY ORDINAL_POSITION"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                case "postgresql":
                    foreach (var row in Query("SELECT column_name AS n FROM information_schema.columns"
                        + " WHERE table_schema = current_schema() AND table_name = " + Literal(table) + " ORDER BY ordinal_position"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                default:
                    foreach (var row in Query("SELECT name AS n FROM pragma_table_info(" + Literal(table) + ") ORDER BY cid"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;
            }

            return result;
        }

        public List<string> PrimaryKeyColumns(string table)
        {
            var result = new List<string>();
            switch (Dialect)
            {
                case "mysql":
                    foreach (var row in Query("SELECT COLUMN_NAME AS n FROM information_schema.KEY_COLUMN_USAGE"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table)
                        + " AND CONSTRAINT_NAME = 'PRIMARY' ORDER BY ORDINAL_POSITION"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                case "postgresql":
                    foreach (var row in Query("SELECT a.attname AS n FROM pg_index i"
                        + " JOIN pg_class c ON c.oid = i.indrelid"
                        + " JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)"
                        + " WHERE c.relname = " + Literal(table) + " AND i.indisprimary"
                        + " ORDER BY array_position(i.indkey::int2[], a.attnum)"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                default:
                    foreach (var row in Query("SELECT name AS n FROM pragma_table_info(" + Literal(table) + ") WHERE pk > 0 ORDER BY pk"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;
            }

            return result;
        }

        public IndexInfo Index(string table, string index)
        {
            switch (Dialect)
            {
                case "mysql":
                    return MySqlIndex(table, index);

                case "postgresql":
                    return PostgreSqlIndex(table, index);

                default:
                    return SqliteIndex(table, index);
            }
        }

        public bool IndexExists(string table, string index)
        {
            return Index(table, index) != null;
        }

        public List<string> IndexNames(string table)
        {
            var result = new List<string>();
            switch (Dialect)
            {
                case "mysql":
                    foreach (var row in Query("SELECT DISTINCT INDEX_NAME AS n FROM information_schema.STATISTICS"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table) + " ORDER BY INDEX_NAME"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                case "postgresql":
                    foreach (var row in Query("SELECT indexname AS n FROM pg_indexes WHERE schemaname = current_schema()"
                        + " AND tablename = " + Literal(table) + " ORDER BY indexname"))
                    {
                        result.Add(TextOf(row, "n"));
                    }
                    break;

                default:
                    foreach (var row in Query("PRAGMA index_list(" + Literal(table) + ")"))
                    {
                        result.Add(TextOf(row, "name"));
                    }
                    break;
            }

            return result;
        }

        public bool ConstraintExists(string table, string name, string type)
        {
            switch (Dialect)
            {
                case "mysql":
                {
                    var value = Text("SELECT CONSTRAINT_TYPE FROM information_schema.TABLE_CONSTRAINTS"
                        + " WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table)
                        + " AND CONSTRAINT_NAME = " + Literal(name));
                    return string.Equals(value, type, StringComparison.OrdinalIgnoreCase);
                }

                case "postgresql":
                {
                    var code = ConstraintTypeCode(type);
                    return Count("SELECT COUNT(*) FROM pg_constraint WHERE conrelid = " + Literal(table) + "::regclass"
                        + " AND conname = " + Literal(name) + " AND contype = " + Literal(code)) > 0;
                }

                default:
                {
                    var sql = Text("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = " + Literal(table)) ?? "";
                    if (string.Equals(type, "UNIQUE", StringComparison.OrdinalIgnoreCase))
                    {
                        return IndexExists(table, name);
                    }

                    return sql.ToUpperInvariant().Contains((name ?? "").ToUpperInvariant());
                }
            }
        }

        public string ConstraintDefinition(string table, string name)
        {
            switch (Dialect)
            {
                case "mysql":
                {
                    var type = Text("SELECT CONSTRAINT_TYPE FROM information_schema.TABLE_CONSTRAINTS"
                        + " WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table)
                        + " AND CONSTRAINT_NAME = " + Literal(name));
                    if (string.Equals(type, "CHECK", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var check = Text("SELECT CHECK_CLAUSE FROM information_schema.CHECK_CONSTRAINTS"
                                + " WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = " + Literal(name));
                            if (!string.IsNullOrEmpty(check))
                            {
                                return check;
                            }
                        }
                        catch
                        {
                            return null;
                        }
                    }

                    var columns = new List<string>();
                    foreach (var row in Query("SELECT COLUMN_NAME AS n FROM information_schema.KEY_COLUMN_USAGE"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = " + Literal(table)
                        + " AND CONSTRAINT_NAME = " + Literal(name) + " ORDER BY ORDINAL_POSITION"))
                    {
                        columns.Add(TextOf(row, "n"));
                    }
                    return string.Join(",", columns);
                }

                case "postgresql":
                    return Text("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = "
                        + Literal(table) + "::regclass AND conname = " + Literal(name));

                default:
                    return Text("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = " + Literal(table));
            }
        }

        public ForeignKeyInfo ForeignKey(string table, string name)
        {
            switch (Dialect)
            {
                case "mysql":
                    return MySqlForeignKey(table, name);

                case "postgresql":
                    return PostgreSqlForeignKey(table, name);

                default:
                    return SqliteForeignKey(table, name);
            }
        }

        public bool IsAutoIncrement(string table, string column)
        {
            switch (Dialect)
            {
                case "mysql":
                {
                    var info = Column(table, column);
                    return info != null && (info.Extra ?? "").ToLowerInvariant().Contains("auto_increment");
                }

                case "postgresql":
                    return Count("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema()"
                        + " AND table_name = " + Literal(table) + " AND column_name = " + Literal(column)
                        + " AND is_identity = 'YES'") > 0;

                default:
                {
                    var sql = Text("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = " + Literal(table)) ?? "";
                    return sql.ToUpperInvariant().Contains("AUTOINCREMENT");
                }
            }
        }

        public string IndexComment(string index)
        {
            if (Dialect != "postgresql")
            {
                return null;
            }

            return Text("SELECT obj_description(oid, 'pg_class') FROM pg_class WHERE relname = " + Literal(index));
        }

        public string ConstraintComment(string table, string name)
        {
            if (Dialect != "postgresql")
            {
                return null;
            }

            return Text("SELECT obj_description(oid, 'pg_constraint') FROM pg_constraint WHERE conrelid = "
                + Literal(table) + "::regclass AND conname = " + Literal(name));
        }

        public bool ViewExists(string view)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Count("SELECT COUNT(*) FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE()"
                        + " AND TABLE_NAME = " + Literal(view)) > 0;

                case "postgresql":
                    return Count("SELECT COUNT(*) FROM pg_views WHERE schemaname = current_schema()"
                        + " AND viewname = " + Literal(view)) > 0;

                default:
                    return Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = " + Literal(view)) > 0;
            }
        }

        public string ViewDefinition(string view)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Text("SELECT VIEW_DEFINITION FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE()"
                        + " AND TABLE_NAME = " + Literal(view));

                case "postgresql":
                    return Text("SELECT definition FROM pg_views WHERE schemaname = current_schema() AND viewname = " + Literal(view));

                default:
                    return Text("SELECT sql FROM sqlite_master WHERE type = 'view' AND name = " + Literal(view));
            }
        }

        public bool SequenceExists(string sequence)
        {
            if (Dialect != "postgresql")
            {
                return false;
            }

            return Count("SELECT COUNT(*) FROM information_schema.sequences WHERE sequence_schema = current_schema()"
                + " AND sequence_name = " + Literal(sequence)) > 0;
        }

        public long SequenceStart(string sequence)
        {
            if (Dialect != "postgresql")
            {
                return -1;
            }

            return Count("SELECT start_value FROM information_schema.sequences WHERE sequence_schema = current_schema()"
                + " AND sequence_name = " + Literal(sequence));
        }

        public bool TriggerExists(string name)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Count("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA = DATABASE()"
                        + " AND TRIGGER_NAME = " + Literal(name)) > 0;

                case "postgresql":
                    return Count("SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = current_schema()"
                        + " AND trigger_name = " + Literal(name)) > 0;

                default:
                    return Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = " + Literal(name)) > 0;
            }
        }

        public bool RoutineExists(string name)
        {
            switch (Dialect)
            {
                case "mysql":
                    return Count("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = DATABASE()"
                        + " AND ROUTINE_NAME = " + Literal(name)) > 0;

                case "postgresql":
                    return Count("SELECT COUNT(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace"
                        + " WHERE n.nspname = current_schema() AND p.proname = " + Literal(name)) > 0;

                default:
                    return false;
            }
        }

        public long PartitionCount(string table)
        {
            if (Dialect != "mysql")
            {
                return 0;
            }

            return Count("SELECT COUNT(*) FROM information_schema.PARTITIONS WHERE TABLE_SCHEMA = DATABASE()"
                + " AND TABLE_NAME = " + Literal(table) + " AND PARTITION_NAME IS NOT NULL");
        }

        public static string Literal(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        private ColumnInfo MySqlColumn(string table, string column)
        {
            var rows = Query("SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_COMMENT, EXTRA,"
                + " CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, CHARACTER_SET_NAME, COLLATION_NAME"
                + " FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()"
                + " AND TABLE_NAME = " + Literal(table) + " AND COLUMN_NAME = " + Literal(column));
            if (rows.Count == 0)
            {
                return null;
            }

            var row = rows[0];
            var rawType = TextOf(row, "COLUMN_TYPE");
            var info = new ColumnInfo
            {
                Name = TextOf(row, "COLUMN_NAME"),
                RawType = rawType,
                Nullable = string.Equals(TextOf(row, "IS_NULLABLE"), "YES", StringComparison.OrdinalIgnoreCase),
                Default = TextOf(row, "COLUMN_DEFAULT"),
                Comment = TextOf(row, "COLUMN_COMMENT"),
                Extra = TextOf(row, "EXTRA"),
                Unsigned = rawType != null && rawType.ToLowerInvariant().Contains("unsigned"),
                Charset = TextOf(row, "CHARACTER_SET_NAME"),
                Collation = TextOf(row, "COLLATION_NAME"),
                Length = LongOf(row, "CHARACTER_MAXIMUM_LENGTH"),
                Precision = IntOf(row, "NUMERIC_PRECISION"),
                Scale = IntOf(row, "NUMERIC_SCALE")
            };
            return info;
        }

        private ColumnInfo PostgreSqlColumn(string table, string column)
        {
            var rows = Query("SELECT column_name, data_type, is_nullable, column_default, character_maximum_length,"
                + " numeric_precision, numeric_scale, udt_name FROM information_schema.columns"
                + " WHERE table_schema = current_schema() AND table_name = " + Literal(table)
                + " AND column_name = " + Literal(column));
            if (rows.Count == 0)
            {
                return null;
            }

            var row = rows[0];
            var comment = Text("SELECT d.description FROM pg_description d"
                + " JOIN pg_class c ON c.oid = d.objoid"
                + " JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = d.objsubid"
                + " WHERE c.relname = " + Literal(table) + " AND a.attname = " + Literal(column) + " AND d.objsubid > 0");
            var info = new ColumnInfo
            {
                Name = TextOf(row, "column_name"),
                RawType = TextOf(row, "data_type"),
                Nullable = string.Equals(TextOf(row, "is_nullable"), "YES", StringComparison.OrdinalIgnoreCase),
                Default = TextOf(row, "column_default"),
                Comment = comment,
                Extra = TextOf(row, "udt_name"),
                Length = LongOf(row, "character_maximum_length"),
                Precision = IntOf(row, "numeric_precision"),
                Scale = IntOf(row, "numeric_scale")
            };
            return info;
        }

        private ColumnInfo SqliteColumn(string table, string column)
        {
            foreach (var row in Query("SELECT * FROM pragma_table_info(" + Literal(table) + ")"))
            {
                if (!string.Equals(TextOf(row, "name"), column, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var type = TextOf(row, "type") ?? "";
                var info = new ColumnInfo
                {
                    Name = TextOf(row, "name"),
                    RawType = type,
                    Nullable = !string.Equals(TextOf(row, "notnull"), "1", StringComparison.Ordinal),
                    Default = TextOf(row, "dflt_value"),
                    Comment = null,
                    Extra = TextOf(row, "pk")
                };
                info.Length = ParseLength(type);
                return info;
            }

            return null;
        }

        private IndexInfo MySqlIndex(string table, string index)
        {
            List<Dictionary<string, object>> rows;
            try
            {
                rows = Query("SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE, EXPRESSION"
                    + " FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()"
                    + " AND TABLE_NAME = " + Literal(table) + " AND INDEX_NAME = " + Literal(index)
                    + " ORDER BY SEQ_IN_INDEX");
            }
            catch
            {
                rows = Query("SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SEQ_IN_INDEX, INDEX_TYPE"
                    + " FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE()"
                    + " AND TABLE_NAME = " + Literal(table) + " AND INDEX_NAME = " + Literal(index)
                    + " ORDER BY SEQ_IN_INDEX");
            }

            if (rows.Count == 0)
            {
                return null;
            }

            var info = new IndexInfo
            {
                Name = index,
                Unique = string.Equals(TextOf(rows[0], "NON_UNIQUE"), "0", StringComparison.Ordinal),
                Type = TextOf(rows[0], "INDEX_TYPE")
            };
            var parts = new List<string>();
            foreach (var row in rows)
            {
                var column = TextOf(row, "COLUMN_NAME");
                var expression = TextOf(row, "EXPRESSION");
                info.Columns.Add(column);
                parts.Add(string.IsNullOrEmpty(expression) ? column : expression);
            }

            info.Definition = "CREATE " + (info.Unique ? "UNIQUE " : "") + "INDEX " + index
                + " ON " + table + " (" + string.Join(", ", parts) + ")";
            return info;
        }

        private IndexInfo PostgreSqlIndex(string table, string index)
        {
            var definition = Text("SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema()"
                + " AND tablename = " + Literal(table) + " AND indexname = " + Literal(index));
            if (string.IsNullOrEmpty(definition))
            {
                return null;
            }

            var info = new IndexInfo
            {
                Name = index,
                Unique = definition.IndexOf("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase) >= 0,
                Definition = definition,
                Type = "btree"
            };
            var open = definition.LastIndexOf('(');
            var close = definition.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                foreach (var part in definition.Substring(open + 1, close - open - 1).Split(','))
                {
                    info.Columns.Add(part.Trim().Trim('"'));
                }
            }

            return info;
        }

        private IndexInfo SqliteIndex(string table, string index)
        {
            var exists = false;
            var unique = false;
            foreach (var row in Query("PRAGMA index_list(" + Literal(table) + ")"))
            {
                if (!string.Equals(TextOf(row, "name"), index, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                exists = true;
                unique = string.Equals(TextOf(row, "unique"), "1", StringComparison.Ordinal);
            }

            if (!exists)
            {
                return null;
            }

            var info = new IndexInfo { Name = index, Unique = unique };
            foreach (var row in Query("PRAGMA index_info(" + Literal(index) + ")"))
            {
                info.Columns.Add(TextOf(row, "name"));
            }

            info.Definition = Text("SELECT sql FROM sqlite_master WHERE type = 'index' AND name = " + Literal(index));
            return info;
        }

        private ForeignKeyInfo MySqlForeignKey(string table, string name)
        {
            var rows = Query("SELECT k.CONSTRAINT_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME,"
                + " k.ORDINAL_POSITION, r.DELETE_RULE, r.UPDATE_RULE"
                + " FROM information_schema.KEY_COLUMN_USAGE k"
                + " JOIN information_schema.REFERENTIAL_CONSTRAINTS r"
                + "   ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME"
                + " WHERE k.TABLE_SCHEMA = DATABASE() AND k.TABLE_NAME = " + Literal(table)
                + " AND k.CONSTRAINT_NAME = " + Literal(name) + " ORDER BY k.ORDINAL_POSITION");
            if (rows.Count == 0)
            {
                return null;
            }

            var info = new ForeignKeyInfo
            {
                Name = name,
                RefTable = TextOf(rows[0], "REFERENCED_TABLE_NAME"),
                OnDelete = TextOf(rows[0], "DELETE_RULE"),
                OnUpdate = TextOf(rows[0], "UPDATE_RULE")
            };
            foreach (var row in rows)
            {
                info.Columns.Add(TextOf(row, "COLUMN_NAME"));
                info.RefColumns.Add(TextOf(row, "REFERENCED_COLUMN_NAME"));
            }

            return info;
        }

        private ForeignKeyInfo PostgreSqlForeignKey(string table, string name)
        {
            var definition = Text("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = "
                + Literal(table) + "::regclass AND conname = " + Literal(name) + " AND contype = 'f'");
            if (string.IsNullOrEmpty(definition))
            {
                return null;
            }

            var info = new ForeignKeyInfo
            {
                Name = name,
                OnDelete = SortRule(Text("SELECT confdeltype FROM pg_constraint WHERE conrelid = "
                    + Literal(table) + "::regclass AND conname = " + Literal(name))),
                OnUpdate = SortRule(Text("SELECT confupdtype FROM pg_constraint WHERE conrelid = "
                    + Literal(table) + "::regclass AND conname = " + Literal(name)))
            };

            var refIndex = definition.IndexOf("REFERENCES", StringComparison.OrdinalIgnoreCase);
            if (refIndex >= 0)
            {
                info.RefTable = Between(definition.Substring(refIndex + "REFERENCES".Length), "", "(").Trim().Trim('"');
            }

            var first = definition.IndexOf('(');
            if (first >= 0 && refIndex > first)
            {
                foreach (var part in definition.Substring(first + 1, refIndex - first - 1).TrimEnd(' ', ')').Split(','))
                {
                    info.Columns.Add(part.Trim().Trim('"'));
                }
            }

            var refOpen = refIndex >= 0 ? definition.IndexOf('(', refIndex) : -1;
            var refClose = refOpen >= 0 ? definition.IndexOf(')', refOpen + 1) : -1;
            if (refOpen >= 0 && refClose > refOpen)
            {
                foreach (var part in definition.Substring(refOpen + 1, refClose - refOpen - 1).Split(','))
                {
                    info.RefColumns.Add(part.Trim().Trim('"'));
                }
            }

            return info;
        }

        private ForeignKeyInfo SqliteForeignKey(string table, string name)
        {
            ForeignKeyInfo info = null;
            foreach (var row in Query("SELECT * FROM pragma_foreign_key_list(" + Literal(table) + ")"))
            {
                var id = TextOf(row, "id");
                if (info == null || !string.Equals(info.Name, id, StringComparison.Ordinal))
                {
                    info = new ForeignKeyInfo { Name = id };
                }

                info.Columns.Add(TextOf(row, "from"));
                info.RefTable = TextOf(row, "table");
                info.RefColumns.Add(TextOf(row, "to"));
                info.OnDelete = TextOf(row, "on_delete");
                info.OnUpdate = TextOf(row, "on_update");
            }

            return info;
        }

        private string Text(string sql)
        {
            var value = Scalar(sql);
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string TextOf(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value))
            {
                return null;
            }

            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long? LongOf(Dictionary<string, object> row, string key)
        {
            var text = TextOf(row, key);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return Convert.ToInt64(text, CultureInfo.InvariantCulture);
        }

        private static int? IntOf(Dictionary<string, object> row, string key)
        {
            var text = TextOf(row, key);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return Convert.ToInt32(text, CultureInfo.InvariantCulture);
        }

        private static long? ParseLength(string type)
        {
            var text = type ?? "";
            var open = text.IndexOf('(');
            var close = text.IndexOf(')');
            if (open < 0 || close <= open)
            {
                return null;
            }

            long value;
            return long.TryParse(text.Substring(open + 1, close - open - 1).Split(',')[0].Trim(), out value)
                ? value
                : (long?)null;
        }

        private static string ConstraintTypeCode(string type)
        {
            switch ((type ?? "").ToUpperInvariant())
            {
                case "UNIQUE":
                    return "u";

                case "CHECK":
                    return "c";

                case "FOREIGN KEY":
                    return "f";

                case "PRIMARY KEY":
                    return "p";

                default:
                    return "?";
            }
        }

        private static string SortRule(string code)
        {
            switch (code)
            {
                case "c":
                    return "CASCADE";

                case "n":
                    return "SET NULL";

                case "d":
                    return "SET DEFAULT";

                case "r":
                    return "RESTRICT";

                case "a":
                    return "NO ACTION";

                default:
                    return null;
            }
        }

        private static string Between(string text, string start, string end)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var begin = string.IsNullOrEmpty(start) ? 0 : text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            if (begin < 0)
            {
                return "";
            }

            begin += start.Length;
            var finish = string.IsNullOrEmpty(end) ? text.Length : text.IndexOf(end, begin, StringComparison.OrdinalIgnoreCase);
            if (finish < 0)
            {
                finish = text.Length;
            }

            return text.Substring(begin, finish - begin).Trim();
        }
    }
}
