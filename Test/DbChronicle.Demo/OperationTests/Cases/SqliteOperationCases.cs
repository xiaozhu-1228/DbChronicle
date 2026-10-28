using System.Collections.Generic;
using System.Linq;
using System;

namespace DbChronicle.Demo.OperationTests.Cases
{
    internal static class SqliteOperationCases
    {
        public static IReadOnlyList<OperationTestCase> Create()
        {
            return new List<OperationTestCase>
            {
                T01TableLevel(),
                T02ColumnLevel(),
                T02bColumnReorder(),
                T02dReorderBefore(),
                T03PrimaryKey(),
                T04Index(),
                T05ForeignKey(),
                T06Constraint(),
                T07Comment(),
                T08View(),
                T09Sequence(),
                T10Trigger(),
                T11Routine(),
                T12Data(),
                T12bSeedConflict(),
                T12cSeedStrategy(),
                T13Advanced(),
                T13bTableOption(),
                T13cUnknownAction(),
                T14RawSql(),
                T14bRawSqlEmpty(),
                T14cRawSqlEmptySqlNode(),
                T14dRawSqlSplitFailed(),
                T14eRawSqlStageOrder(),
                T15Types(),
                T15bTypeLengthRequired()
            };
        }

        private static OperationTestCase T01TableLevel()
        {
            return new OperationTestCase
            {
                CaseId = "T01",
                Title = "第 1 类 表级操作（CREATE/DROP/RENAME/COMMENT 跳过/REBUILD）",
                FileName = "20261028_120101_c01001_table_level.xml",
                ExpectedWarnings = new[] { "W006", "W018" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t01_base");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t01_renamed");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t01_dropped");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t01_dropped (id INTEGER NOT NULL PRIMARY KEY)");
                },
                Verify = ctx =>
                {
                    ctx.Check("1.1", "CREATE TABLE 建出的表改名后存在", ctx.Probe.TableExists("dcops_t01_renamed"));
                    ctx.Check("1.3", "RENAME TABLE 后旧表名不存在", !ctx.Probe.TableExists("dcops_t01_base"));
                    ctx.Equal("1.2", "DROP TABLE 后表不存在", false, ctx.Probe.TableExists("dcops_t01_dropped"));
                    ctx.Check("1.1", "自增列 id 为 INTEGER PRIMARY KEY AUTOINCREMENT",
                        ctx.Probe.IsAutoIncrement("dcops_t01_renamed", "id"));
                    ctx.Equal("1.1", "PRIMARY KEY 为 (id)", "id",
                        string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t01_renamed")));
                    ctx.Skip("1.4", "表注释", "SQLite 无注释机制，编译输出 W006 并跳过");
                }
            };
        }

        private static OperationTestCase T02ColumnLevel()
        {
            return new OperationTestCase
            {
                CaseId = "T02",
                Title = "第 2 类 列级操作（ADD/DROP/RENAME/MODIFY rebuild/COMMENT 跳过）",
                FileName = "20261028_120102_c02002_column_level.xml",
                ExpectedWarnings = new[] { "W001", "W002", "W030" },
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t02_cols\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"c_type\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_len\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_comment\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_charset\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_unsigned\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_onupdate\" action=\"ADD\" type=\"DateTime\" nullable=\"true\"/>\n"
                    + "    <column name=\"c_unique\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "    <column name=\"legacy_col\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t02_rename\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"c_old\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t02_ai\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"note\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"true\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t02_cols");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t02_rename");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t02_ai");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t02_cols (id INTEGER NOT NULL PRIMARY KEY, c_type INTEGER NULL,"
                        + " c_len TEXT NULL, c_comment TEXT NULL, c_charset TEXT NULL, c_unsigned INTEGER NULL,"
                        + " c_onupdate TEXT NULL, c_unique TEXT NULL, legacy_col INTEGER NULL)");
                    ctx.Setup("INSERT INTO dcops_t02_cols (id,c_type,c_len) VALUES (1,10,'abc')");
                    ctx.Setup("CREATE TABLE dcops_t02_rename (id INTEGER NOT NULL PRIMARY KEY, c_old TEXT NULL)");
                    ctx.Setup("CREATE TABLE dcops_t02_ai (id INTEGER NOT NULL PRIMARY KEY, note TEXT NULL)");
                },
                Verify = ctx =>
                {
                    ctx.Check("2.1", "rebuild 后 ADD 列 c_added 存在", ctx.Probe.ColumnExists("dcops_t02_cols", "c_added"));
                    ctx.Check("2.2", "rebuild 后 DROP 列 legacy_col 不存在",
                        !ctx.Probe.ColumnExists("dcops_t02_cols", "legacy_col"));
                    ctx.Check("2.3", "RENAME 列新名存在", ctx.Probe.ColumnExists("dcops_t02_rename", "c_new"));
                    ctx.Check("2.3", "RENAME 列旧名不存在", !ctx.Probe.ColumnExists("dcops_t02_rename", "c_old"));

                    var row = ctx.Probe.Query("SELECT id,c_type,c_len FROM dcops_t02_cols WHERE id = 1");
                    ctx.Equal("2.5", "MODIFY 走 rebuild 后原有数据保留", 1, row.Count);
                    ctx.Equal("2.5", "rebuild 保留原值", "10|abc",
                        row.Count == 0 ? "" : OpsTestContext.ToText(row[0]["c_type"]) + "|" + OpsTestContext.ToText(row[0]["c_len"]));

                    ctx.Skip("2.6", "修改长度", "SQLite 类型系统不保存长度（String→TEXT），结构上不可观测");
                    ctx.Skip("2.7", "修改精度/小数位", "SQLite 类型系统不保存精度（Decimal→NUMERIC），结构上不可观测");
                    ctx.Skip("2.12", "修改列注释", "SQLite 无列注释；该列操作被 rebuild 吸收，不单独产生 W007");
                    ctx.Gap("2.9", "修改可空性", "SQLite 的 rebuild 依赖累积模型；本用例已提供基线，可空性可从 pragma 验证，但类型系统宽松");

                    ctx.Check("2.14", "MODIFY charset/collate 被忽略且不报错",
                        ctx.Probe.ColumnExists("dcops_t02_cols", "c_charset"));
                    ctx.Check("2.16", "MODIFY unsigned 被忽略并告警 W001",
                        ctx.Probe.ColumnExists("dcops_t02_cols", "c_unsigned"));
                    ctx.Check("2.17", "MODIFY onUpdate 被忽略并告警 W002",
                        ctx.Probe.ColumnExists("dcops_t02_cols", "c_onupdate"));

                    var ddl = OpsTestContext.ToText(ctx.Probe.Scalar(
                        "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'dcops_t02_cols'"));
                    ctx.Check("2.15", "MODIFY unique=true 在重建后的表定义中生成 UNIQUE",
                        ddl.ToUpperInvariant().Contains("UNIQUE"), "DDL=" + ddl);

                    if (ctx.Probe.IsAutoIncrement("dcops_t02_ai", "id"))
                    {
                        ctx.Pass("2.13", "MODIFY autoIncrement 经 rebuild 生效（PRIMARY KEY AUTOINCREMENT）");
                    }
                    else
                    {
                        ctx.Fail("2.13", "MODIFY autoIncrement 未生效", "rebuild 后未生成 AUTOINCREMENT");
                    }
                }
            };
        }

        private static OperationTestCase T02bColumnReorder()
        {
            return new OperationTestCase
            {
                CaseId = "T02b",
                Title = "第 2 类边界 REORDER（SQLite → E044）",
                FileName = "20261028_120103_c02003_column_reorder.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E044"
            };
        }

        private static OperationTestCase T02dReorderBefore()
        {
            return new OperationTestCase
            {
                CaseId = "T02d",
                Title = "第 2 类边界 REORDER before（→ E053，改用 after/FIRST）",
                FileName = "20261028_120126_c02005_reorder_before.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E053"
            };
        }

        private static OperationTestCase T03PrimaryKey()
        {
            return new OperationTestCase
            {
                CaseId = "T03",
                Title = "第 3 类 主键操作（ADD/DROP/REPLACE 全部 rebuild）",
                FileName = "20261028_120104_c03003_primary_key.xml",
                ExpectedWarnings = new[] { "W030" },
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t03_add\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" nullable=\"false\"/>\n"
                    + "    <column name=\"code\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"false\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t03_drop\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"code\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"false\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t03_replace\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"code\" action=\"ADD\" type=\"String\" length=\"20\" nullable=\"false\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t03_add");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t03_drop");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t03_replace");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t03_add (id INTEGER NOT NULL, code TEXT NOT NULL)");
                    ctx.Setup("CREATE TABLE dcops_t03_drop (id INTEGER NOT NULL PRIMARY KEY, code TEXT NOT NULL)");
                    ctx.Setup("CREATE TABLE dcops_t03_replace (id INTEGER NOT NULL PRIMARY KEY, code TEXT NOT NULL)");
                },
                Verify = ctx =>
                {
                    ctx.Equal("3.1", "ADD PRIMARY KEY (id)（rebuild）", "id",
                        string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_add")));
                    ctx.Equal("3.2", "DROP PRIMARY KEY（rebuild）后无主键", "",
                        string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_drop")));
                    ctx.Equal("3.3", "REPLACE PRIMARY KEY 改为 (code)", "code",
                        string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_replace")));
                }
            };
        }

        private static OperationTestCase T04Index()
        {
            return new OperationTestCase
            {
                CaseId = "T04",
                Title = "第 4 类 索引操作（普通/唯一/复合/DROP/函数/部分/全文降级/RENAME 不支持）",
                FileName = "20261028_120105_c04004_index.xml",
                ExpectedWarnings = new[] { "W017", "W028" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t04_idx");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t04_idx (id INTEGER NOT NULL PRIMARY KEY, a INTEGER NULL,"
                        + " b INTEGER NULL, c INTEGER NULL, email TEXT NULL, body TEXT NULL)");
                    ctx.Setup("CREATE INDEX idx_t04_legacy ON dcops_t04_idx (a)");
                    ctx.Setup("CREATE INDEX idx_t04_old_name ON dcops_t04_idx (b)");
                },
                Verify = ctx =>
                {
                    var normal = ctx.Probe.Index("dcops_t04_idx", "idx_t04_normal");
                    ctx.Check("4.1", "普通索引存在且列为 (a)",
                        normal != null && !normal.Unique && string.Join(",", normal.Columns) == "a",
                        normal == null ? "索引不存在" : "columns=" + string.Join(",", normal.Columns));

                    var unique = ctx.Probe.Index("dcops_t04_idx", "idx_t04_unique");
                    ctx.Check("4.2", "唯一索引存在且 unique=true", unique != null && unique.Unique);

                    var composite = ctx.Probe.Index("dcops_t04_idx", "idx_t04_composite");
                    ctx.Equal("4.3", "复合索引列为 (b,c)", "b,c",
                        composite == null ? "<null>" : string.Join(",", composite.Columns));

                    var function = ctx.Probe.Index("dcops_t04_idx", "idx_t04_func");
                    ctx.Contains("4.6", "函数索引含 LOWER(email)", "lower",
                        function == null ? "" : (function.Definition ?? ""));

                    var partial = ctx.Probe.Index("dcops_t04_idx", "idx_t04_partial");
                    ctx.Contains("4.7", "部分索引含 WHERE 条件", "where",
                        partial == null ? "" : (partial.Definition ?? ""));

                    var fulltext = ctx.Probe.Index("dcops_t04_idx", "idx_t04_fulltext");
                    ctx.Check("4.8", "全文索引降级为普通索引（W017）", fulltext != null);

                    ctx.Check("4.4", "DROP INDEX 后旧索引不存在", !ctx.Probe.IndexExists("dcops_t04_idx", "idx_t04_legacy"));
                    ctx.Skip("4.5", "RENAME INDEX", "SQLite 不支持重命名索引（W028），需 DROP + CREATE");
                }
            };
        }

        private static OperationTestCase T05ForeignKey()
        {
            return new OperationTestCase
            {
                CaseId = "T05",
                Title = "第 5 类 外键操作（ADD/DROP 均走 rebuild）",
                FileName = "20261028_120106_c05005_foreign_key.xml",
                ExpectedWarnings = new[] { "W030" },
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t05_parent\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t05_add\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"parent_id\" action=\"ADD\" type=\"Int64\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t05_drop\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"parent_id\" action=\"ADD\" type=\"Int64\" nullable=\"true\"/>\n"
                    + "    <foreignKey name=\"fk_t05_legacy\" action=\"ADD\" columns=\"parent_id\" refTable=\"dcops_t05_parent\" refColumns=\"id\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t05_mod\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"parent_id\" action=\"ADD\" type=\"Int64\" nullable=\"true\"/>\n"
                    + "    <foreignKey name=\"fk_t05_mod\" action=\"ADD\" columns=\"parent_id\" refTable=\"dcops_t05_parent\" refColumns=\"id\" onDelete=\"CASCADE\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_add");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_drop");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_mod");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_parent");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t05_parent (id INTEGER NOT NULL PRIMARY KEY)");
                    ctx.Setup("CREATE TABLE dcops_t05_add (id INTEGER NOT NULL PRIMARY KEY, parent_id INTEGER NULL)");
                    ctx.Setup("CREATE TABLE dcops_t05_drop (id INTEGER NOT NULL PRIMARY KEY, parent_id INTEGER NULL,"
                        + " CONSTRAINT fk_t05_legacy FOREIGN KEY (parent_id) REFERENCES dcops_t05_parent (id))");
                    ctx.Setup("CREATE TABLE dcops_t05_mod (id INTEGER NOT NULL PRIMARY KEY, parent_id INTEGER NULL,"
                        + " CONSTRAINT fk_t05_mod FOREIGN KEY (parent_id) REFERENCES dcops_t05_parent (id) ON DELETE CASCADE)");
                },
                Verify = ctx =>
                {
                    var added = ctx.Probe.ForeignKey("dcops_t05_add", "fk_t05_new");
                    ctx.Check("5.1", "ADD FOREIGN KEY（rebuild 内联）存在",
                        added != null && string.Equals(added.RefTable, "dcops_t05_parent", StringComparison.OrdinalIgnoreCase),
                        added == null ? "外键不存在" : "ref=" + added.RefTable);
                    ctx.Equal("5.3", "级联规则 ON DELETE=CASCADE", "CASCADE", added == null ? null : added.OnDelete);

                    var dropped = ctx.Probe.ForeignKey("dcops_t05_drop", "fk_t05_legacy");
                    ctx.Check("5.2", "DROP FOREIGN KEY（rebuild 重放目标模型）后外键不存在", dropped == null,
                        dropped == null ? null : "ref=" + dropped.RefTable);

                    var modified = ctx.Probe.ForeignKey("dcops_t05_mod", "fk_t05_mod");
                    ctx.Equal("5.3", "MODIFY 外键级联规则 ON DELETE=CASCADE→SET NULL", "SET NULL",
                        modified == null ? null : modified.OnDelete);
                    ctx.Equal("5.3", "MODIFY 外键级联规则 ON UPDATE=RESTRICT→CASCADE", "CASCADE",
                        modified == null ? null : modified.OnUpdate);
                }
            };
        }

        private static OperationTestCase T06Constraint()
        {
            return new OperationTestCase
            {
                CaseId = "T06",
                Title = "第 6 类 约束操作（唯一 索引形式 / 检查 与 默认值 rebuild）",
                FileName = "20261028_120107_c06006_constraint.xml",
                ExpectedWarnings = new[] { "W030" },
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t06_uk\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"code\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"false\"/>\n"
                    + "    <column name=\"age\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "    <uniqueConstraint name=\"uk_t06_legacy\" action=\"ADD\" columns=\"code\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t06_ck\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"age\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t06_ck_drop\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"age\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t06_df\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"status\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "    <column name=\"legacy_status\" action=\"ADD\" type=\"Int32\" nullable=\"true\" defaultValue=\"7\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_uk");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_ck");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_ck_drop");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_df");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t06_uk (id INTEGER NOT NULL PRIMARY KEY, code TEXT NOT NULL, age INTEGER NULL)");
                    ctx.Setup("CREATE UNIQUE INDEX uk_t06_legacy ON dcops_t06_uk (code)");
                    ctx.Setup("CREATE TABLE dcops_t06_ck (id INTEGER NOT NULL PRIMARY KEY, age INTEGER NULL)");
                    ctx.Setup("CREATE TABLE dcops_t06_ck_drop (id INTEGER NOT NULL PRIMARY KEY, age INTEGER NULL,"
                        + " CONSTRAINT ck_t06_legacy CHECK (age IS NULL OR age >= 0))");
                    ctx.Setup("CREATE TABLE dcops_t06_df (id INTEGER NOT NULL PRIMARY KEY, status INTEGER NULL,"
                        + " legacy_status INTEGER NULL DEFAULT 7)");
                },
                Verify = ctx =>
                {
                    ctx.Check("6.1", "ADD UNIQUE（CREATE UNIQUE INDEX）存在",
                        ctx.Probe.IndexExists("dcops_t06_uk", "uk_t06_new"));
                    ctx.Check("6.2", "DROP UNIQUE 后旧索引不存在",
                        !ctx.Probe.IndexExists("dcops_t06_uk", "uk_t06_legacy"));

                    var check = ctx.Probe.ConstraintDefinition("dcops_t06_ck", "ck_t06_age") ?? "";
                    ctx.Contains("6.3", "ADD CHECK（rebuild 内联）含 age", "check", check);

                    var checkDrop = ctx.Probe.ConstraintDefinition("dcops_t06_ck_drop", "ck_t06_legacy") ?? "";
                    ctx.Check("6.4", "DROP CHECK（rebuild 重放目标模型）后约束消失",
                        !checkDrop.ToLowerInvariant().Contains("ck_t06_legacy"), checkDrop);

                    var status = ctx.Probe.Column("dcops_t06_df", "status");
                    ctx.Equal("6.5", "ADD DEFAULT（rebuild）后默认值为 0", "0", status == null ? null : status.Default);
                    var legacy = ctx.Probe.Column("dcops_t06_df", "legacy_status");
                    ctx.Check("6.6", "DROP DEFAULT（rebuild）后旧默认值消失",
                        legacy == null || string.IsNullOrEmpty(legacy.Default),
                        legacy == null ? "列不存在" : "default=" + (legacy.Default ?? "<null>"));
                }
            };
        }

        private static OperationTestCase T07Comment()
        {
            return new OperationTestCase
            {
                CaseId = "T07",
                Title = "第 7 类 注释操作（SQLite 全部跳过：W006/W007/W031）",
                FileName = "20261028_120108_c07007_comment.xml",
                ExpectedWarnings = new[] { "W006", "W007", "W031" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t07_comment");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t07_comment (id INTEGER NOT NULL PRIMARY KEY, phone TEXT NULL)");
                    ctx.Setup("CREATE INDEX idx_t07_phone ON dcops_t07_comment (phone)");
                },
                Verify = ctx =>
                {
                    ctx.Check("7.1", "表注释被跳过，表与列仍存在",
                        ctx.Probe.TableExists("dcops_t07_comment")
                            && ctx.Probe.ColumnExists("dcops_t07_comment", "phone"));
                    ctx.Skip("7.2", "列注释", "SQLite 无列注释，编译输出 W007 并跳过");
                    ctx.Skip("7.3", "索引注释", "SQLite 无索引注释，编译输出 W031 并跳过");
                    ctx.Skip("7.4", "约束注释", "SQLite 无约束注释，编译输出 W031 并跳过");
                }
            };
        }

        private static OperationTestCase T08View()
        {
            return new OperationTestCase
            {
                CaseId = "T08",
                Title = "第 8 类 视图操作（CREATE/REPLACE 先删后建/DROP）",
                FileName = "20261028_120109_c08008_view.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v08_summary");
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v08_columns");
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v08_passthrough");
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v08_legacy");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t08_src");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t08_src (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL)");
                    ctx.Setup("CREATE VIEW dcops_v08_legacy AS SELECT id FROM dcops_t08_src");
                },
                Verify = ctx =>
                {
                    ctx.Check("8.1", "CREATE VIEW 后视图存在", ctx.Probe.ViewExists("dcops_v08_summary"));
                    var definition = ctx.Probe.ViewDefinition("dcops_v08_summary") ?? "";
                    var flat = System.Text.RegularExpressions.Regex.Replace(definition, "\\s+", " ").Trim();
                    ctx.Contains("8.2", "REPLACE（DROP+CREATE）后定义包含 name", "name", flat);
                    ctx.Check("8.3", "DROP VIEW 后旧视图不存在", !ctx.Probe.ViewExists("dcops_v08_legacy"));
                    ctx.Equal("8.4", "视图列清单 columns 生效", "key_id,label",
                        string.Join(",", ctx.Probe.ColumnNames("dcops_v08_columns")));
                    ctx.Check("8.5", "视图 <sql> 透传回退（无 select，按方言原样执行）",
                        ctx.Probe.ViewExists("dcops_v08_passthrough"));
                }
            };
        }

        private static OperationTestCase T09Sequence()
        {
            return new OperationTestCase
            {
                CaseId = "T09",
                Title = "第 9 类 序列操作（SQLite 无原生序列 → E046）",
                FileName = "20261028_120110_c09009_sequence.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E046"
            };
        }

        private static OperationTestCase T10Trigger()
        {
            return new OperationTestCase
            {
                CaseId = "T10",
                Title = "第 10 类 触发器操作（CREATE/DROP + 实际触发）",
                FileName = "20261028_120111_c10010_trigger.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t10_log");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t10_src");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t10_src (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, name TEXT NULL)");
                    ctx.Setup("CREATE TABLE dcops_t10_log (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, msg TEXT NULL)");
                    ctx.Setup("CREATE TRIGGER trg_t10_legacy BEFORE INSERT ON dcops_t10_src"
                        + " FOR EACH ROW BEGIN SELECT 1; END");
                },
                Verify = ctx =>
                {
                    ctx.Check("10.1", "CREATE TRIGGER 后触发器存在", ctx.Probe.TriggerExists("trg_t10_after_insert"));
                    ctx.Check("10.2", "DROP TRIGGER 后旧触发器不存在", !ctx.Probe.TriggerExists("trg_t10_legacy"));

                    ctx.Execute("INSERT INTO dcops_t10_src (name) VALUES ('trigger-check')");
                    var count = ctx.Probe.Count("SELECT COUNT(*) FROM dcops_t10_log WHERE msg LIKE 'inserted:%'");
                    ctx.Equal("10.1", "触发器实际生效（插入一行写入日志表）", 1L, count);
                }
            };
        }

        private static OperationTestCase T11Routine()
        {
            return new OperationTestCase
            {
                CaseId = "T11",
                Title = "第 11 类 存储过程/函数（SQLite 无例程，rawSql 透传等价视图）",
                FileName = "20261028_120112_c11011_routine.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v11_echo");
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v11_legacy");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE VIEW dcops_v11_legacy AS SELECT 1 AS r");
                },
                Verify = ctx =>
                {
                    ctx.Check("11.1", "rawSql 透传创建等价对象（视图）", ctx.Probe.ViewExists("dcops_v11_echo"));
                    ctx.Check("11.2", "DROP 等价对象后旧视图不存在", !ctx.Probe.ViewExists("dcops_v11_legacy"));

                    var value = ctx.Probe.Scalar("SELECT r FROM dcops_v11_echo");
                    ctx.Equal("11.1", "等价对象求值 21*2", "42", OpsTestContext.ToText(value));
                    ctx.Skip("11", "存储过程/函数", "SQLite 无例程对象，只能用 rawSql 表达等价逻辑");
                }
            };
        }

        private static OperationTestCase T12Data()
        {
            return new OperationTestCase
            {
                CaseId = "T12",
                Title = "第 12 类 数据操作（SEED/UPDATE/DELETE/CLEAN/MIGRATE/BACKFILL + stage）",
                FileName = "20261028_120113_c12012_data.xml",
                ExpectedWarnings = new[] { "W033" },
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t12_data\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"name\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "    <column name=\"email\" action=\"ADD\" type=\"String\" length=\"64\" nullable=\"true\"/>\n"
                    + "    <column name=\"status\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "    <column name=\"score\" action=\"ADD\" type=\"Int32\" nullable=\"true\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12_data");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t12_data (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL,"
                        + " email TEXT NULL, status INTEGER NULL, score INTEGER NULL)");
                    ctx.Setup("INSERT INTO dcops_t12_data (id,name,email,status,score) VALUES"
                        + " (1,'a',NULL,0,10),(2,'b','',0,20),(3,'c','x@y',1,30)");
                },
                Verify = ctx =>
                {
                    ctx.Equal("12.3", "DELETE 后行数为 3", 3L, ctx.Probe.RowCount("dcops_t12_data"));

                    var rows = ctx.Probe.Query("SELECT id,name,email,status,score,note FROM dcops_t12_data ORDER BY id");
                    var actual = rows.Select(row => string.Join("|", new[]
                    {
                        OpsTestContext.ToText(row["id"]),
                        OpsTestContext.ToText(row["name"]),
                        OpsTestContext.ToText(row["email"]),
                        OpsTestContext.ToText(row["status"]),
                        OpsTestContext.ToText(row["score"]),
                        OpsTestContext.ToText(row["note"])
                    })).ToList();

                    var expected = new List<string>
                    {
                        "1|a-updated|a@x|1|16|filled",
                        "2|before-mig|clean@x|77|20|filled",
                        "4|d|d@x|1|100|filled"
                    };

                    ctx.Equal("12.1", "SEED/UPDATE/CLEAN/MIGRATE/BACKFILL/stage 综合结果",
                        string.Join(" ; ", expected), string.Join(" ; ", actual));
                    ctx.Equal("12.5", "stage=before 的数据迁移先于结构变更执行", "before-mig",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT name FROM dcops_t12_data WHERE id = 2")));
                    ctx.Equal("12.5", "缺当前方言的 dataMigration 被跳过（W033），未改动数据", "16",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT score FROM dcops_t12_data WHERE id = 1")));
                    ctx.Equal("12.6", "BACKFILL 列 note 全部填充", 0L,
                        ctx.Probe.Count("SELECT COUNT(*) FROM dcops_t12_data WHERE note IS NULL"));
                    ctx.Equal("12.4", "CLEAN 空邮箱已清洗", 0L,
                        ctx.Probe.Count("SELECT COUNT(*) FROM dcops_t12_data WHERE email = ''"));
                }
            };
        }

        private static OperationTestCase T12bSeedConflict()
        {
            return new OperationTestCase
            {
                CaseId = "T12b",
                Title = "第 12 类边界 SEED 冲突策略（DEFAULT 普通插入 / ERROR 冲突中止）",
                FileName = "20261028_120116_c12013_seed_conflict.xml",
                ExpectExecutionError = true,
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12b_plain");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12b_data");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t12b_plain (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL)");
                    ctx.Setup("CREATE TABLE dcops_t12b_data (id INTEGER NOT NULL PRIMARY KEY, name TEXT NULL)");
                    ctx.Setup("INSERT INTO dcops_t12b_data (id,name) VALUES (1,'origin')");
                },
                Verify = ctx =>
                {
                    if (string.Equals(ctx.Dialect, "mysql", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Equal("12.1", "conflict=DEFAULT 插入成功（无事务 DDL，前一组已提交）", 1L,
                            ctx.Probe.RowCount("dcops_t12b_plain"));
                    }
                    else
                    {
                        ctx.Equal("12.1", "conflict=ERROR 使整个单元回滚（事务方言），DEFAULT 插入无残留", 0L,
                            ctx.Probe.RowCount("dcops_t12b_plain"));
                    }

                    ctx.Equal("12.1", "conflict=ERROR 冲突时 INSERT 失败，原行未被覆盖", "origin",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT name FROM dcops_t12b_data WHERE id = 1")));
                }
            };
        }

        private static OperationTestCase T12cSeedStrategy()
        {
            return new OperationTestCase
            {
                CaseId = "T12c",
                Title = "第 12 类 SEED 冲突策略（IGNORE 跳过 / UPDATE 覆盖）",
                FileName = "20261028_120125_c12014_seed_strategy.xml",
                IncrementalModify = true,
                BaselineXml = "  <table name=\"dcops_t12c_ignore\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"note\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "  </table>\n"
                    + "  <table name=\"dcops_t12c_update\" action=\"CREATE\">\n"
                    + "    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" nullable=\"false\"/>\n"
                    + "    <column name=\"note\" action=\"ADD\" type=\"String\" length=\"32\" nullable=\"true\"/>\n"
                    + "  </table>\n",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12c_ignore");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12c_update");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t12c_ignore (id INTEGER NOT NULL PRIMARY KEY, note TEXT NULL)");
                    ctx.Setup("INSERT INTO dcops_t12c_ignore (id, note) VALUES (1, 'origin')");
                    ctx.Setup("CREATE TABLE dcops_t12c_update (id INTEGER NOT NULL PRIMARY KEY, note TEXT NULL)");
                    ctx.Setup("INSERT INTO dcops_t12c_update (id, note) VALUES (1, 'origin')");
                },
                Verify = ctx =>
                {
                    ctx.Equal("12.1", "IGNORE 冲突时跳过，原值保留", "origin",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t12c_ignore WHERE id = 1")));
                    ctx.Equal("12.1", "IGNORE 未产生新行", 1L, ctx.Probe.RowCount("dcops_t12c_ignore"));
                    ctx.Equal("12.1", "UPDATE 冲突时覆盖为 updated", "updated",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t12c_update WHERE id = 1")));
                    ctx.Equal("12.1", "UPDATE 未产生新行", 1L, ctx.Probe.RowCount("dcops_t12c_update"));
                }
            };
        }

        private static OperationTestCase T13Advanced()
        {
            return new OperationTestCase
            {
                CaseId = "T13",
                Title = "第 13 类 高级对象（同义词/分区用 rawSql 模拟；引擎/字符集/表空间/权限无对应机制）",
                FileName = "20261028_120114_c13013_advanced.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v13_synonym");
                    ctx.Execute("DROP VIEW IF EXISTS dcops_t13_part_all");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t13_part_a");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t13_part_b");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t13_adv");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t13_adv (id INTEGER NOT NULL PRIMARY KEY, payload TEXT NULL)");
                },
                Verify = ctx =>
                {
                    ctx.Check("13.6", "同义词（视图模拟）存在", ctx.Probe.ViewExists("dcops_v13_synonym"));
                    ctx.Check("13.4", "分区模拟：分片表 a/b 存在",
                        ctx.Probe.TableExists("dcops_t13_part_a") && ctx.Probe.TableExists("dcops_t13_part_b"));
                    ctx.Check("13.4", "分区模拟：UNION ALL 汇总视图存在", ctx.Probe.ViewExists("dcops_t13_part_all"));
                    ctx.Skip("13.1/13.2/13.3", "字符集/存储引擎/表空间", "SQLite 无这些机制（表选项编译报 E047）");
                    ctx.Skip("13.5", "权限", "SQLite 无账号权限模型（GRANT/REVOKE 不可用）");
                }
            };
        }

        private static OperationTestCase T15bTypeLengthRequired()
        {
            return new OperationTestCase
            {
                CaseId = "T15b",
                Title = "第 2 类边界 String 缺 length（→ E018）",
                FileName = "20261028_120124_c15002_type_length_required.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E018"
            };
        }

        private static OperationTestCase T15Types()
        {
            return new OperationTestCase
            {
                CaseId = "T15",
                Title = "第 2 类 数据类型映射（20 种逻辑类型全覆盖）",
                FileName = "20261028_120123_c15001_types.xml",
                ExpectedWarnings = new[] { "W004", "W005" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t15_types");
                },
                Verify = ctx =>
                {
                    var expected = new (string Column, string Type)[]
                    {
                        ("c_bool", "integer"),
                        ("c_int8", "integer"),
                        ("c_int16", "integer"),
                        ("c_int32", "integer"),
                        ("c_int64", "integer"),
                        ("c_float32", "real"),
                        ("c_float64", "real"),
                        ("c_decimal", "numeric"),
                        ("c_fixed", "text"),
                        ("c_string", "text"),
                        ("c_text", "text"),
                        ("c_json", "text"),
                        ("c_xml", "text"),
                        ("c_datetime", "text"),
                        ("c_dto", "text"),
                        ("c_date", "text"),
                        ("c_time", "text"),
                        ("c_binary", "blob"),
                        ("c_blob", "blob"),
                        ("c_blob_len", "blob"),
                        ("c_uuid", "text")
                    };

                    var index = 0;
                    foreach (var item in expected)
                    {
                        index++;
                        var info = ctx.Probe.Column("dcops_t15_types", item.Column);
                        var raw = info == null ? "" : (info.RawType ?? "").ToLowerInvariant();
                        var baseType = System.Text.RegularExpressions.Regex.Replace(raw, @"\(\d+\)$", "");
                        bool matched;
                        if (item.Type.Contains("("))
                        {
                            matched = raw == item.Type;
                        }
                        else
                        {
                            matched = baseType == item.Type;
                            if (item.Column == "c_int8" && raw == "tinyint(1)")
                            {
                                matched = false;
                            }
                        }

                        ctx.Check("2.5-" + index, item.Column + " → " + item.Type,
                            matched, "实际=" + (raw.Length == 0 ? "<列不存在>" : raw));
                    }

                    ctx.Skip("2.6/2.7", "长度与精度", "SQLite 类型系统不保存长度/精度（TEXT/NUMERIC 无修饰）");
                }
            };
        }

        private static OperationTestCase T14RawSql()
        {
            return new OperationTestCase
            {
                CaseId = "T14",
                Title = "通用能力 rawSql（直接文本/CDATA 多语句/分号与转义/注释/any 回退/缺方言 W032）",
                FileName = "20261028_120118_c14001_raw_sql.xml",
                ExpectedWarnings = new[] { "W032" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t14_raw");
                },
                Verify = ctx =>
                {
                    ctx.Check("R1", "直接文本 rawSql 多语句执行（建表 + 插入）",
                        ctx.Probe.TableExists("dcops_t14_raw"));
                    ctx.Equal("R2", "字符串内的分号未被当作语句分隔", "a;b",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t14_raw WHERE id = 1")));
                    ctx.Equal("R3", "单引号转义 '' 正确", "it's ok",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t14_raw WHERE id = 2")));
                    ctx.Equal("R4", "行注释内的分号/关键字不影响拆分，注释后的语句仍执行", "after-comment",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t14_raw WHERE id = 3")));
                    ctx.Equal("R5", "第二个 rawSql（直接文本 → any 回退）已执行", "any-ok",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT note FROM dcops_t14_raw WHERE id = 4")));
                    ctx.Equal("R6", "缺当前方言的 rawSql 被跳过（W032），未改动数据", 4L,
                        ctx.Probe.RowCount("dcops_t14_raw"));
                }
            };
        }

        private static OperationTestCase T14dRawSqlSplitFailed()
        {
            return new OperationTestCase
            {
                CaseId = "T14d",
                Title = "rawSql 边界：拆分失败（未闭合单引号）→ E042",
                FileName = "20261028_120121_c14004_raw_sql_split_failed.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E042"
            };
        }

        private static OperationTestCase T14eRawSqlStageOrder()
        {
            return new OperationTestCase
            {
                CaseId = "T14e",
                Title = "rawSql 边界：stage=before 排在 CREATE TABLE 之前（编译期顺序断言）",
                FileName = "20261028_120122_c14005_raw_sql_stage_order.xml",
                CompileOnly = true,
                Verify = ctx =>
                {
                    var sql = ctx.CompiledSql ?? "";
                    var marker = sql.IndexOf("BEFORE_MARKER", StringComparison.Ordinal);
                    var create = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);
                    ctx.Check("R9", "before 的 rawSql 语句出现在 CREATE TABLE 之前",
                        marker >= 0 && create >= 0 && marker < create,
                        "marker=" + marker + "，create=" + create);
                }
            };
        }

        private static OperationTestCase T14cRawSqlEmptySqlNode()
        {
            return new OperationTestCase
            {
                CaseId = "T14c",
                Title = "rawSql 边界：空 <sql> 子节点（当前静默跳过）",
                FileName = "20261028_120120_c14003_raw_sql_empty_node.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E051"
            };
        }

        private static OperationTestCase T14bRawSqlEmpty()
        {
            return new OperationTestCase
            {
                CaseId = "T14b",
                Title = "rawSql 边界：内容为空 → E051",
                FileName = "20261028_120119_c14002_raw_sql_empty.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E051"
            };
        }

        private static OperationTestCase T13cUnknownAction()
        {
            return new OperationTestCase
            {
                CaseId = "T13c",
                Title = "第 13 类边界 SYNONYM 写成 table action（→ E012，提示改用 rawSql）",
                FileName = "20261028_120117_c13015_unknown_action.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E012"
            };
        }

        private static OperationTestCase T13bTableOption()
        {
            return new OperationTestCase
            {
                CaseId = "T13b",
                Title = "第 13 类边界 ENGINE/CHARSET（SQLite → E047）",
                FileName = "20261028_120115_c13014_table_option.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E047"
            };
        }
    }
}
