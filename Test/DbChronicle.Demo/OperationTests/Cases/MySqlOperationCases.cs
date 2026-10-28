using System.Collections.Generic;
using System.Linq;
using System;

namespace DbChronicle.Demo.OperationTests.Cases
{
    internal static class MySqlOperationCases
    {
        public static IReadOnlyList<OperationTestCase> Create()
        {
            return new List<OperationTestCase>
            {
                T01TableLevel(),
                T02ColumnLevel(),
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
                Title = "第 1 类 表级操作（CREATE/DROP/RENAME/COMMENT/ENGINE/CHARSET/REBUILD）",
                FileName = "20261028_100101_a01001_table_level.xml",
                ExpectedWarnings = new[] { "W018" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t01_base, dcops_t01_renamed, dcops_t01_dropped");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t01_dropped (id BIGINT NOT NULL PRIMARY KEY) ENGINE=InnoDB");
                },
                Verify = ctx =>
                {
                    ctx.Check("1.1", "CREATE TABLE 建出的表改名后存在", ctx.Probe.TableExists("dcops_t01_renamed"));
                    ctx.Check("1.3", "RENAME TABLE 后旧表名不存在", !ctx.Probe.TableExists("dcops_t01_base"));
                    ctx.Equal("1.4", "表注释已更新", "表级操作-已改注释", ctx.Probe.TableComment("dcops_t01_renamed"));
                    ctx.Equal("1.5", "存储引擎为 InnoDB", "InnoDB", ctx.Probe.TableEngine("dcops_t01_renamed"));
                    ctx.Contains("1.6", "字符集/排序规则为 utf8mb4", "utf8mb4", ctx.Probe.TableCollation("dcops_t01_renamed"));
                    ctx.Equal("1.2", "DROP TABLE 后表不存在", false, ctx.Probe.TableExists("dcops_t01_dropped"));

                    var id = ctx.Probe.Column("dcops_t01_renamed", "id");
                    ctx.Check("1.1", "主键列 id 存在且自增", id != null
                        && (id.Extra ?? "").ToLowerInvariant().Contains("auto_increment"),
                        id == null ? "列不存在" : "EXTRA=" + id.Extra);
                    ctx.Equal("1.1", "PRIMARY KEY 为 (id)", "id", string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t01_renamed")));
                }
            };
        }

        private static OperationTestCase T02ColumnLevel()
        {
            return new OperationTestCase
            {
                CaseId = "T02",
                Title = "第 2 类 列级操作（ADD/DROP/RENAME/REORDER/MODIFY 13 项/COMMENT）",
                FileName = "20261028_100102_a02002_column_level.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t02_cols, dcops_t02_rename, dcops_t02_ai");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t02_cols ("
                        + " id BIGINT NOT NULL,"
                        + " c_type INT NULL,"
                        + " c_len VARCHAR(32) NULL,"
                        + " c_prec DECIMAL(10,2) NULL,"
                        + " c_null INT NOT NULL,"
                        + " c_def VARCHAR(32) NULL DEFAULT 'old',"
                        + " c_defnone INT NULL DEFAULT 7,"
                        + " c_comment VARCHAR(32) NULL,"
                        + " c_charset VARCHAR(32) NULL,"
                        + " c_unsigned INT NULL,"
                        + " c_onupdate DATETIME NULL,"
                        + " c_unique VARCHAR(32) NULL,"
                        + " c_reorder INT NULL,"
                        + " c_reorder_before INT NULL,"
                        + " legacy_col INT NULL,"
                        + " PRIMARY KEY (id)"
                        + ") ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t02_rename (id BIGINT NOT NULL PRIMARY KEY, c_old VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t02_ai (id BIGINT NOT NULL PRIMARY KEY, note VARCHAR(20) NULL) ENGINE=InnoDB");
                },
                Verify = ctx =>
                {
                    var added = ctx.Probe.Column("dcops_t02_cols", "c_added");
                    ctx.Check("2.1", "ADD COLUMN 新列存在且长度 40",
                        added != null && added.Length == 40,
                        added == null ? "列不存在" : "type=" + added.RawType + " length=" + added.Length);

                    ctx.Check("2.2", "DROP COLUMN 旧列已删除", !ctx.Probe.ColumnExists("dcops_t02_cols", "legacy_col"));

                    var type = ctx.Probe.Column("dcops_t02_cols", "c_type");
                    ctx.StartsWith("2.5", "MODIFY 类型 INT→BIGINT", "bigint", type == null ? "<null>" : type.RawType);

                    var length = ctx.Probe.Column("dcops_t02_cols", "c_len");
                    ctx.Equal("2.6", "MODIFY 长度 32→128", 128L, length == null ? null : length.Length);

                    var precision = ctx.Probe.Column("dcops_t02_cols", "c_prec");
                    ctx.Check("2.7", "MODIFY 精度 10→14",
                        precision != null && precision.Precision == 14,
                        precision == null ? "列不存在" : "precision=" + precision.Precision);
                    ctx.Check("2.8", "MODIFY 小数位 2→4",
                        precision != null && precision.Scale == 4,
                        precision == null ? "列不存在" : "scale=" + precision.Scale);

                    var nullable = ctx.Probe.Column("dcops_t02_cols", "c_null");
                    ctx.Check("2.9", "MODIFY 可空 NOT NULL→NULL", nullable != null && nullable.Nullable,
                        nullable == null ? "列不存在" : "nullable=" + nullable.Nullable);

                    var def = ctx.Probe.Column("dcops_t02_cols", "c_def");
                    ctx.Equal("2.10", "MODIFY 默认值 old→new", "new", def == null ? null : def.Default);

                    var defNone = ctx.Probe.Column("dcops_t02_cols", "c_defnone");
                    ctx.Check("2.11", "MODIFY default=none 删除默认值",
                        defNone != null && string.IsNullOrEmpty(defNone.Default),
                        defNone == null ? "列不存在" : "default=" + (defNone.Default ?? "<null>"));

                    var comment = ctx.Probe.Column("dcops_t02_cols", "c_comment");
                    ctx.Equal("2.12", "COMMENT 列注释已更新", "列注释已改", comment == null ? null : comment.Comment);

                    var charset = ctx.Probe.Column("dcops_t02_cols", "c_charset");
                    ctx.Equal("2.14", "MODIFY 列级排序规则为 utf8mb4_bin", "utf8mb4_bin", charset == null ? null : charset.Collation);

                    var unsigned = ctx.Probe.Column("dcops_t02_cols", "c_unsigned");
                    ctx.Check("2.16", "MODIFY unsigned=true 生效", unsigned != null && unsigned.Unsigned,
                        unsigned == null ? "列不存在" : "type=" + unsigned.RawType);

                    var onUpdate = ctx.Probe.Column("dcops_t02_cols", "c_onupdate");
                    ctx.Check("2.17", "MODIFY onUpdate 生效",
                        onUpdate != null && (onUpdate.Extra ?? "").ToLowerInvariant().Contains("on update"),
                        onUpdate == null ? "列不存在" : "EXTRA=" + onUpdate.Extra);

                    var order = ctx.Probe.ColumnNames("dcops_t02_cols");
                    var typeIndex = order.FindIndex(x => string.Equals(x, "c_type", StringComparison.OrdinalIgnoreCase));
                    var reorderIndex = order.FindIndex(x => string.Equals(x, "c_reorder", StringComparison.OrdinalIgnoreCase));
                    ctx.Check("2.4", "REORDER after=c_type 生效",
                        typeIndex >= 0 && reorderIndex == typeIndex + 1,
                        "列顺序=" + string.Join(",", order));

                    var renamed = ctx.Probe.Column("dcops_t02_rename", "c_new");
                    ctx.Check("2.3", "RENAME COLUMN 新列存在", renamed != null, renamed == null ? "列不存在" : null);
                    ctx.Check("2.3", "RENAME COLUMN 旧列已不存在", !ctx.Probe.ColumnExists("dcops_t02_rename", "c_old"));

                    var ai = ctx.Probe.Column("dcops_t02_ai", "id");
                    ctx.Check("2.13", "MODIFY autoIncrement 生效",
                        ai != null && (ai.Extra ?? "").ToLowerInvariant().Contains("auto_increment"),
                        ai == null ? "列不存在" : "EXTRA=" + ai.Extra);

                    var uniqueIndex = ctx.Probe.Index("dcops_t02_cols", "c_unique");
                    ctx.Check("2.15", "MODIFY unique=true 生成唯一索引（列名即索引名）",
                        uniqueIndex != null && uniqueIndex.Unique,
                        uniqueIndex == null ? "未找到 c_unique 索引" : "unique=" + uniqueIndex.Unique);
                }
            };
        }

        private static OperationTestCase T02dReorderBefore()
        {
            return new OperationTestCase
            {
                CaseId = "T02d",
                Title = "第 2 类边界 REORDER before（→ E053，改用 after/FIRST）",
                FileName = "20261028_100124_a02005_reorder_before.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E053"
            };
        }

        private static OperationTestCase T03PrimaryKey()
        {
            return new OperationTestCase
            {
                CaseId = "T03",
                Title = "第 3 类 主键操作（ADD/DROP/REPLACE）",
                FileName = "20261028_100103_a03003_primary_key.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t03_add, dcops_t03_drop, dcops_t03_replace");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t03_add (id BIGINT NOT NULL, code VARCHAR(20) NOT NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t03_drop (id BIGINT NOT NULL, PRIMARY KEY (id)) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t03_replace (id BIGINT NOT NULL, code VARCHAR(20) NOT NULL, PRIMARY KEY (id)) ENGINE=InnoDB");
                },
                Verify = ctx =>
                {
                    ctx.Equal("3.1", "ADD PRIMARY KEY (id)", "id", string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_add")));
                    ctx.Equal("3.2", "DROP PRIMARY KEY 后无主键", "", string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_drop")));
                    ctx.Equal("3.3", "REPLACE PRIMARY KEY 改为 (code)", "code", string.Join(",", ctx.Probe.PrimaryKeyColumns("dcops_t03_replace")));
                }
            };
        }

        private static OperationTestCase T04Index()
        {
            return new OperationTestCase
            {
                CaseId = "T04",
                Title = "第 4 类 索引操作（普通/唯一/复合/DROP/RENAME/函数/部分/全文）",
                FileName = "20261028_100104_a04004_index.xml",
                ExpectedWarnings = new[] { "W027" },
                ExpectedExecutionErrorVersionBelow = "8.0",
                ExpectedExecutionErrorContains = "(LOWER",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t04_idx");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t04_idx (id BIGINT NOT NULL PRIMARY KEY, a INT NULL, b INT NULL,"
                        + " c INT NULL, email VARCHAR(64) NULL, body TEXT NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE INDEX idx_t04_legacy ON dcops_t04_idx (a)");
                    ctx.Setup("CREATE INDEX idx_t04_old_name ON dcops_t04_idx (b)");
                },
                Verify = ctx =>
                {
                    var normal = ctx.Probe.Index("dcops_t04_idx", "idx_t04_normal");
                    ctx.Check("4.1", "普通索引存在且列为 (a)",
                        normal != null && !normal.Unique && string.Join(",", normal.Columns) == "a",
                        normal == null ? "索引不存在" : "unique=" + normal.Unique + " columns=" + string.Join(",", normal.Columns));

                    var unique = ctx.Probe.Index("dcops_t04_idx", "idx_t04_unique");
                    ctx.Check("4.2", "唯一索引存在且 unique=true",
                        unique != null && unique.Unique,
                        unique == null ? "索引不存在" : "unique=" + unique.Unique);

                    var composite = ctx.Probe.Index("dcops_t04_idx", "idx_t04_composite");
                    ctx.Equal("4.3", "复合索引列为 (b,c)", "b,c",
                        composite == null ? "<null>" : string.Join(",", composite.Columns));

                    if (ctx.VersionBelow("8.0"))
                    {
                        ctx.Gap("4.6", "函数索引", "MySQL 5.7 不支持 CREATE INDEX ... ((表达式))，需 8.0.13+；该语句在 5.7 上按预期失败");
                    }
                    else
                    {
                        var function = ctx.Probe.Index("dcops_t04_idx", "idx_t04_func");
                        ctx.Contains("4.6", "函数索引含 LOWER(email)", "lower",
                            function == null ? "" : (function.Definition ?? ""));
                    }

                    var partial = ctx.Probe.Index("dcops_t04_idx", "idx_t04_partial");
                    ctx.Check("4.7", "部分索引已生成（MySQL 忽略 where 并告警 W027）", partial != null,
                        partial == null ? "索引不存在" : "MySQL 无部分索引，where 被忽略");

                    var fulltext = ctx.Probe.Index("dcops_t04_idx", "idx_t04_fulltext");
                    ctx.Contains("4.8", "全文索引类型为 FULLTEXT", "fulltext",
                        fulltext == null ? "" : (fulltext.Type ?? fulltext.Definition ?? ""));

                    ctx.Check("4.4", "DROP INDEX 后旧索引不存在", !ctx.Probe.IndexExists("dcops_t04_idx", "idx_t04_legacy"));

                    var renamed = ctx.Probe.Index("dcops_t04_idx", "idx_t04_new_name");
                    ctx.Check("4.5", "RENAME INDEX 新名存在", renamed != null, renamed == null ? "索引不存在" : null);
                    ctx.Check("4.5", "RENAME INDEX 旧名不存在", !ctx.Probe.IndexExists("dcops_t04_idx", "idx_t04_old_name"));
                }
            };
        }

        private static OperationTestCase T05ForeignKey()
        {
            return new OperationTestCase
            {
                CaseId = "T05",
                Title = "第 5 类 外键操作（ADD/DROP/MODIFY 级联规则）",
                FileName = "20261028_100105_a05005_foreign_key.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_child");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t05_parent");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t05_parent (id BIGINT NOT NULL PRIMARY KEY) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t05_child (id BIGINT NOT NULL PRIMARY KEY, parent_id BIGINT NULL,"
                        + " legacy_parent_id BIGINT NULL) ENGINE=InnoDB");
                    ctx.Setup("ALTER TABLE dcops_t05_child ADD CONSTRAINT fk_t05_legacy"
                        + " FOREIGN KEY (legacy_parent_id) REFERENCES dcops_t05_parent (id)");
                },
                Verify = ctx =>
                {
                    var fk = ctx.Probe.ForeignKey("dcops_t05_child", "fk_t05_child_parent");
                    ctx.Check("5.1", "ADD FOREIGN KEY 存在且列/引用正确",
                        fk != null && string.Join(",", fk.Columns) == "parent_id"
                            && string.Equals(fk.RefTable, "dcops_t05_parent", StringComparison.OrdinalIgnoreCase),
                        fk == null ? "外键不存在" : "columns=" + string.Join(",", fk.Columns) + " ref=" + fk.RefTable);
                    ctx.Equal("5.3", "MODIFY 级联规则 ON DELETE=SET NULL", "SET NULL", fk == null ? null : fk.OnDelete);
                    ctx.Equal("5.3", "MODIFY 级联规则 ON UPDATE=CASCADE", "CASCADE", fk == null ? null : fk.OnUpdate);
                    ctx.Check("5.2", "DROP FOREIGN KEY 后旧外键不存在",
                        ctx.Probe.ForeignKey("dcops_t05_child", "fk_t05_legacy") == null);
                }
            };
        }

        private static OperationTestCase T06Constraint()
        {
            return new OperationTestCase
            {
                CaseId = "T06",
                Title = "第 6 类 约束操作（唯一/检查/默认值 各 ADD+DROP）",
                FileName = "20261028_100106_a06006_constraint.xml",
                ExpectedExecutionErrorVersionBelow = "8.0",
                ExpectedExecutionErrorContains = "CHECK",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_cons");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t06_check");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t06_cons (id BIGINT NOT NULL PRIMARY KEY, code VARCHAR(32) NOT NULL,"
                        + " age INT NULL, status INT NULL) ENGINE=InnoDB");
                    ctx.Setup("ALTER TABLE dcops_t06_cons ADD CONSTRAINT uk_t06_legacy UNIQUE (code)");
                    ctx.Setup("CREATE TABLE dcops_t06_check (id BIGINT NOT NULL PRIMARY KEY,"
                        + " age INT NULL) ENGINE=InnoDB");
                    ctx.Setup("ALTER TABLE dcops_t06_check ADD CONSTRAINT ck_t06_legacy CHECK (age IS NULL OR age >= 0)");
                },
                Verify = ctx =>
                {
                    ctx.Check("6.1", "ADD UNIQUE CONSTRAINT 存在",
                        ctx.Probe.ConstraintExists("dcops_t06_cons", "uk_t06_new", "UNIQUE"));
                    ctx.Equal("6.1", "唯一约束列为 (code,age)", "code,age",
                        string.Join(",", ctx.Probe.ConstraintDefinition("dcops_t06_cons", "uk_t06_new") ?? "").Trim(','));
                    ctx.Check("6.2", "DROP UNIQUE CONSTRAINT 后旧约束不存在",
                        !ctx.Probe.ConstraintExists("dcops_t06_cons", "uk_t06_legacy", "UNIQUE"));

                    var status = ctx.Probe.Column("dcops_t06_cons", "status");
                    if (ctx.VersionBelow("8.0"))
                    {
                        ctx.Gap("6.3", "ADD CHECK CONSTRAINT", "MySQL 5.7 接受 CHECK 语法但不生效（W023）");
                        ctx.Gap("6.4", "DROP CHECK CONSTRAINT", "MySQL 5.7 不支持 DROP CHECK 语法；该语句在 5.7 上按预期失败");
                        ctx.Gap("6.5", "ADD DEFAULT CONSTRAINT", "MySQL 5.7 不支持 ALTER COLUMN SET DEFAULT（W024）");
                        ctx.Gap("6.6", "DROP DEFAULT CONSTRAINT", "MySQL 5.7 不支持 ALTER COLUMN DROP DEFAULT（W024）");
                        ctx.Check("6.4", "CHECK 语法失败被隔离在 dcops_t06_check 表组，未影响唯一约束断言",
                            ctx.Probe.TableExists("dcops_t06_check"),
                            "5.7 不支持 DROP CHECK，失败只影响该表组；5.7 也不记录 CHECK 约束元数据");
                    }
                    else
                    {
                        var check = ctx.Probe.ConstraintDefinition("dcops_t06_cons", "ck_t06_age");
                        ctx.Check("6.3", "ADD CHECK CONSTRAINT 存在且含 age",
                            !string.IsNullOrEmpty(check) && check.ToLowerInvariant().Contains("age"),
                            "定义=" + (check ?? "<null>"));
                        ctx.Check("6.4", "DROP CHECK CONSTRAINT 后旧约束不存在",
                            !ctx.Probe.ConstraintExists("dcops_t06_check", "ck_t06_legacy", "CHECK"));
                        ctx.Equal("6.5", "ADD DEFAULT CONSTRAINT 后默认值为 0", "0", status == null ? null : status.Default);
                        ctx.Check("6.6", "DROP DEFAULT CONSTRAINT 已执行（不报错）", status != null,
                            status == null ? "列不存在" : null);
                    }
                }
            };
        }

        private static OperationTestCase T07Comment()
        {
            return new OperationTestCase
            {
                CaseId = "T07",
                Title = "第 7 类 注释操作（表/列/索引/约束）",
                FileName = "20261028_100107_a07007_comment.xml",
                ExpectedWarnings = new[] { "W031" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t07_comment");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t07_comment (id BIGINT NOT NULL PRIMARY KEY,"
                        + " phone VARCHAR(20) NULL COMMENT '旧手机号注释') ENGINE=InnoDB COMMENT='旧表注释'");
                    ctx.Setup("CREATE INDEX idx_t07_phone ON dcops_t07_comment (phone)");
                    ctx.Setup("ALTER TABLE dcops_t07_comment ADD CONSTRAINT ck_t07_phone"
                        + " CHECK (phone IS NULL OR CHAR_LENGTH(phone) <= 20)");
                },
                Verify = ctx =>
                {
                    ctx.Equal("7.1", "表注释已更新", "新表注释", ctx.Probe.TableComment("dcops_t07_comment"));
                    var phone = ctx.Probe.Column("dcops_t07_comment", "phone");
                    ctx.Equal("7.2", "列注释已更新", "新手机号注释", phone == null ? null : phone.Comment);
                    ctx.Check("7.3", "索引注释在 MySQL 上跳过（无原生机制）",
                        ctx.Probe.IndexExists("dcops_t07_comment", "idx_t07_phone"),
                        "已产生 W031");
                    ctx.Pass("7.4", "约束注释在 MySQL 上跳过（无原生机制，已产生 W031）");
                }
            };
        }

        private static OperationTestCase T08View()
        {
            return new OperationTestCase
            {
                CaseId = "T08",
                Title = "第 8 类 视图操作（CREATE/REPLACE/DROP）",
                FileName = "20261028_100108_a08008_view.xml",
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
                    ctx.Setup("CREATE TABLE dcops_t08_src (id BIGINT NOT NULL PRIMARY KEY, name VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE VIEW dcops_v08_legacy AS SELECT id FROM dcops_t08_src");
                },
                Verify = ctx =>
                {
                    ctx.Check("8.1", "CREATE VIEW 后视图存在", ctx.Probe.ViewExists("dcops_v08_summary"));
                    var definition = ctx.Probe.ViewDefinition("dcops_v08_summary") ?? "";
                    var flat = System.Text.RegularExpressions.Regex.Replace(definition, "\\s+", " ").Trim();
                    ctx.Contains("8.2", "REPLACE 后定义已替换为 SELECT id", "select", flat);
                    ctx.Check("8.2", "REPLACE 后不再包含 name 列", !flat.ToLowerInvariant().Contains("name"),
                        flat);
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
                Title = "第 9 类 序列操作（MySQL 无原生序列 → E046）",
                FileName = "20261028_100109_a09009_sequence.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E046"
            };
        }

        private static OperationTestCase T10Trigger()
        {
            return new OperationTestCase
            {
                CaseId = "T10",
                Title = "第 10 类 触发器操作（CREATE/DROP）",
                FileName = "20261028_100110_a10010_trigger.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t10_log");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t10_src");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t10_src (id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,"
                        + " name VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t10_log (id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,"
                        + " msg VARCHAR(64) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TRIGGER trg_t10_legacy BEFORE INSERT ON dcops_t10_src"
                        + " FOR EACH ROW SET NEW.name = NEW.name");
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
                Title = "第 11 类 存储过程/函数（CREATE/DROP + 调用）",
                FileName = "20261028_100111_a11011_routine.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP PROCEDURE IF EXISTS dcops_p11_echo");
                    ctx.Execute("DROP PROCEDURE IF EXISTS dcops_p11_legacy");
                    ctx.Execute("DROP FUNCTION IF EXISTS dcops_f11_echo");
                    ctx.Execute("DROP FUNCTION IF EXISTS dcops_f11_legacy");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE PROCEDURE dcops_p11_legacy() SELECT 1");
                    ctx.Setup("CREATE FUNCTION dcops_f11_legacy() RETURNS INT DETERMINISTIC RETURN 1");
                },
                Verify = ctx =>
                {
                    ctx.Check("11.1", "CREATE PROCEDURE 后例程存在", ctx.Probe.RoutineExists("dcops_p11_echo"));
                    ctx.Check("11.1", "CREATE FUNCTION 后例程存在", ctx.Probe.RoutineExists("dcops_f11_echo"));
                    ctx.Check("11.2", "DROP PROCEDURE 后旧例程不存在", !ctx.Probe.RoutineExists("dcops_p11_legacy"));
                    ctx.Check("11.2", "DROP FUNCTION 后旧例程不存在", !ctx.Probe.RoutineExists("dcops_f11_legacy"));

                    ctx.Execute("CALL dcops_p11_echo(21, @r)");
                    var value = ctx.Probe.Scalar("SELECT @r");
                    ctx.Equal("11.1", "过程调用返回 21*2", "42", OpsTestContext.ToText(value));

                    var functionValue = ctx.Probe.Scalar("SELECT dcops_f11_echo(21)");
                    ctx.Equal("11.1", "函数调用返回 21*2", "42", OpsTestContext.ToText(functionValue));
                    ctx.Pass("11.1", "REPLACE 语义（DROP + CREATE）覆盖重定义场景",
                        "MySQL 无 CREATE OR REPLACE FUNCTION，用 rawSql 先删后建");
                }
            };
        }

        private static OperationTestCase T12Data()
        {
            return new OperationTestCase
            {
                CaseId = "T12",
                Title = "第 12 类 数据操作（SEED/UPDATE/DELETE/CLEAN/MIGRATE/BACKFILL + stage）",
                FileName = "20261028_100112_a12012_data.xml",
                ExpectedWarnings = new[] { "W033" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12_data");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t12_data (id BIGINT NOT NULL PRIMARY KEY, name VARCHAR(32) NULL,"
                        + " email VARCHAR(64) NULL, status INT NULL, score INT NULL) ENGINE=InnoDB");
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
                    ctx.Equal("12.2", "UPDATE 只影响 status=1 的行（id=1 score 11→16）", "16",
                        OpsTestContext.ToText(ctx.Probe.Scalar("SELECT score FROM dcops_t12_data WHERE id = 1")));
                }
            };
        }

        private static OperationTestCase T12bSeedConflict()
        {
            return new OperationTestCase
            {
                CaseId = "T12b",
                Title = "第 12 类边界 SEED 冲突策略（DEFAULT 普通插入 / ERROR 冲突中止）",
                FileName = "20261028_100114_a12013_seed_conflict.xml",
                ExpectExecutionError = true,
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12b_plain");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t12b_data");
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t12b_plain (id BIGINT NOT NULL PRIMARY KEY, name VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t12b_data (id BIGINT NOT NULL PRIMARY KEY, name VARCHAR(32) NULL) ENGINE=InnoDB");
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

        private static OperationTestCase T15bTypeLengthRequired()
        {
            return new OperationTestCase
            {
                CaseId = "T15b",
                Title = "第 2 类边界 String 缺 length（→ E018）",
                FileName = "20261028_100122_a15002_type_length_required.xml",
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
                FileName = "20261028_100121_a15001_types.xml",
                ExpectedWarnings = new[] { "W003", "W004", "W005" },
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t15_types");
                },
                Verify = ctx =>
                {
                    var expected = new (string Column, string Type)[]
                    {
                        ("c_bool", "tinyint(1)"),
                        ("c_int8", "tinyint"),
                        ("c_int16", "smallint"),
                        ("c_int32", "int"),
                        ("c_int64", "bigint"),
                        ("c_float32", "float"),
                        ("c_float64", "double"),
                        ("c_decimal", "decimal(12,3)"),
                        ("c_fixed", "char(8)"),
                        ("c_string", "varchar(32)"),
                        ("c_text", "longtext"),
                        ("c_json", "json"),
                        ("c_xml", "longtext"),
                        ("c_datetime", "datetime"),
                        ("c_dto", "timestamp"),
                        ("c_date", "date"),
                        ("c_time", "time"),
                        ("c_binary", "varbinary(16)"),
                        ("c_blob", "longblob"),
                        ("c_blob_len", "longblob"),
                        ("c_uuid", "char(36)")
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

                    var decimalColumn = ctx.Probe.Column("dcops_t15_types", "c_decimal");
                    ctx.Check("2.7", "Decimal 精度/小数位 12,3",
                        decimalColumn != null && decimalColumn.Precision == 12 && decimalColumn.Scale == 3,
                        decimalColumn == null ? "列不存在" : "precision=" + decimalColumn.Precision + " scale=" + decimalColumn.Scale);

                    var stringColumn = ctx.Probe.Column("dcops_t15_types", "c_string");
                    ctx.Equal("2.6", "String 长度 32", 32L, stringColumn == null ? null : stringColumn.Length);

                    var fixedColumn = ctx.Probe.Column("dcops_t15_types", "c_fixed");
                    ctx.Equal("2.6", "FixedString 长度 8", 8L, fixedColumn == null ? null : fixedColumn.Length);
                }
            };
        }

        private static OperationTestCase T14RawSql()
        {
            return new OperationTestCase
            {
                CaseId = "T14",
                Title = "通用能力 rawSql（直接文本/CDATA 多语句/分号与转义/注释/any 回退/缺方言 W032）",
                FileName = "20261028_100116_a14001_raw_sql.xml",
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
                FileName = "20261028_100119_a14004_raw_sql_split_failed.xml",
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
                FileName = "20261028_100120_a14005_raw_sql_stage_order.xml",
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
                FileName = "20261028_100118_a14003_raw_sql_empty_node.xml",
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
                FileName = "20261028_100117_a14002_raw_sql_empty.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E051"
            };
        }

        private static OperationTestCase T13cUnknownAction()
        {
            return new OperationTestCase
            {
                CaseId = "T13c",
                Title = "第 13 类边界 PARTITION 写成 table action（→ E012，提示改用 rawSql）",
                FileName = "20261028_100115_a13015_unknown_action.xml",
                ExpectCompileError = true,
                ExpectedErrorCode = "E012"
            };
        }

        private static OperationTestCase T12cSeedStrategy()
        {
            return new OperationTestCase
            {
                CaseId = "T12c",
                Title = "第 12 类 SEED 冲突策略（IGNORE 跳过 / UPDATE 覆盖）",
                FileName = "20261028_100123_a12014_seed_strategy.xml",
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
                    ctx.Setup("CREATE TABLE dcops_t12c_ignore (id BIGINT NOT NULL PRIMARY KEY, note VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("INSERT INTO dcops_t12c_ignore (id, note) VALUES (1, 'origin')");
                    ctx.Setup("CREATE TABLE dcops_t12c_update (id BIGINT NOT NULL PRIMARY KEY, note VARCHAR(32) NULL) ENGINE=InnoDB");
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
                Title = "第 13 类 高级对象（字符集/引擎/分区/权限/同义词；表空间见报告说明）",
                FileName = "20261028_100113_a13013_advanced.xml",
                Cleanup = ctx =>
                {
                    ctx.Execute("DROP VIEW IF EXISTS dcops_v13_synonym");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t13_adv");
                    ctx.Execute("DROP TABLE IF EXISTS dcops_t13_ts_tbl");
                    try
                    {
                        ctx.Execute("DROP USER IF EXISTS 'dcops_reader'@'%'");
                    }
                    catch (Exception)
                    {
                    }

                    try
                    {
                        ctx.Execute("DROP TABLESPACE dcops_ts");
                    }
                    catch (Exception)
                    {
                    }
                },
                Setup = ctx =>
                {
                    ctx.Setup("CREATE TABLE dcops_t13_adv (id BIGINT NOT NULL PRIMARY KEY,"
                        + " payload VARCHAR(32) NULL) ENGINE=InnoDB");
                    ctx.Setup("CREATE TABLE dcops_t13_ts_tbl (id BIGINT NOT NULL PRIMARY KEY) ENGINE=InnoDB");
                },
                Verify = ctx =>
                {
                    ctx.Contains("13.1", "CHARSET 生效（utf8mb4_unicode_ci）", "utf8mb4_unicode_ci",
                        ctx.Probe.TableCollation("dcops_t13_adv"));
                    ctx.Equal("13.2", "ENGINE 生效（InnoDB）", "InnoDB", ctx.Probe.TableEngine("dcops_t13_adv"));
                    ctx.Equal("13.4", "分区（rawSql 透传）创建 4 个分区", 4L, ctx.Probe.PartitionCount("dcops_t13_adv"));
                    ctx.Check("13.6", "同义词（MySQL 用视图模拟）存在", ctx.Probe.ViewExists("dcops_v13_synonym"));
                    var privilegeSql = "SELECT IF(COUNT(*) > 0, 'Y', 'N') FROM information_schema.TABLE_PRIVILEGES"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dcops_t13_adv'"
                        + " AND GRANTEE = '''dcops_reader''@''%''' AND PRIVILEGE_TYPE = 'SELECT'";
                    ctx.Equal("13.5", "GRANT SELECT 后临时账号获得表级查询权限", "Y",
                        OpsTestContext.ToText(ctx.Probe.Scalar(privilegeSql)));

                    ctx.Execute("REVOKE SELECT ON dcops_t13_adv FROM 'dcops_reader'@'%'");
                    ctx.Equal("13.5", "REVOKE 后权限被收回", "N",
                        OpsTestContext.ToText(ctx.Probe.Scalar(privilegeSql)));

                    ctx.Execute("DROP USER IF EXISTS 'dcops_reader'@'%'");
                    ctx.Equal("13.5", "清理后临时账号不存在", 0L,
                        ctx.Probe.Count("SELECT COUNT(*) FROM mysql.user WHERE user = 'dcops_reader'"));

                    ctx.Equal("13.3", "表进入通用表空间 dcops_ts（真实归属）", "dcops_ts",
                        OpsTestContext.ToText(ctx.Probe.Scalar(
                            "SELECT ts.NAME FROM information_schema.INNODB_SYS_TABLES t"
                            + " JOIN information_schema.INNODB_SYS_TABLESPACES ts ON ts.SPACE = t.SPACE"
                            + " WHERE t.NAME = CONCAT(DATABASE(), '/dcops_t13_ts_tbl')")));

                    ctx.Execute("ALTER TABLE dcops_t13_ts_tbl TABLESPACE innodb_file_per_table");
                    ctx.Execute("DROP TABLESPACE dcops_ts");
                }
            };
        }
    }
}
