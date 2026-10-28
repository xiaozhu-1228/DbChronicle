# 13 类操作逻辑模型测试

对方案 §4.1 的 13 类数据库结构操作（共 73 个操作项）做端到端验证：
每个 changeUnit 独立**编译 → 在真实库执行 → 用元数据断言结果**，不做“只看 SQL 文本”的浅验证。

## 运行

```powershell
cd Test\DbChronicle.Demo
dotnet run -- ops --dialect=mysql        # 或直接 F5（默认取配置里的方言）
dotnet run -- ops --dialect=postgresql
dotnet run -- ops --dialect=sqlite
dotnet run -- ops --dialect=postgresql --only=T05,T06
dotnet run -- ops --dialect=mysql --keep              # 保留临时目录（含每个用例的 SQL）
```

无参数直接启动（含 VS 里 F5）即跑 13 类测试；`demo` 参数才跑原来的 DbUpgrade 演示场景。

> 中文路径下 `dotnet run` 偶尔会报 `Invalid runtimeconfig.json`（hostfxr 路径截断），
> 可改用 `dotnet .\bin\Debug\net8.0\DbChronicle.Demo.dll ops --dialect=...`，或 `chcp 65001`。

### 环境

| 方言 | 连接串来源 | 默认值 |
| --- | --- | --- |
| MySQL / MariaDB | `DBCHRONICLE_DEMO_MYSQL` | `127.0.0.1:3806 / snibemiddlewaredb / root / sa` |
| PostgreSQL | `DBCHRONICLE_DEMO_PG` | `127.0.0.1:5432 / snibemiddlewaredb / postgres / sa` |
| SQLite | 无需配置 | 临时文件 |

程序会建**专用测试库**（`dbchronicle_ops_mysql` / `dbchronicle_ops_pg`），不碰业务库；建库失败才回退原库，
所有对象 `dcops_` 前缀且每个用例执行前清理。

## 目录

```
OperationTests/
├─ OperationTestRunner.cs   # 清理 → 预置 → 编译 → 执行 → 断言 → 报告
├─ SchemaProbe.cs           # 三方言元数据探针
├─ OpsTestContext.cs        # PASS/FAIL/WARN/SKIP/GAP 断言记录
├─ OperationTestCase.cs     # 用例定义（基线、预置、预期错误、增量模式）
├─ TestDatabase.cs          # 专用库与版本探测
└─ Cases/                   # 三方言用例与断言
LogicModels/Ops/{mysql,postgresql,sqlite}/   # 24 / 27 / 26 个 changeUnit
```

## 覆盖

| 类 | 操作项 | MySQL | PostgreSQL | SQLite |
| --- | --- | --- | --- | --- |
| 1 表级 | 1.1–1.8 | 全部执行（1.7 建表空间→移入→移回→删） | 1.1–1.4、1.7、1.8；1.5/1.6 → E047 | 1.1–1.4、1.8；1.5–1.7 → E047 |
| 2 列级 | 2.1–2.17 | 全部执行（2.4 的 before 仅告警） | 除 2.4（E044）、2.13（E052）外执行 | ADD/DROP/RENAME/MODIFY(rebuild) + 2.13/2.16/2.17；2.4 → E044 |
| 3 主键 | 3.1–3.3 | 执行 | 执行 | rebuild |
| 4 索引 | 4.1–4.8 | 执行（5.7 的 4.6 缺口） | 执行（4.8 降级 W017） | 执行（4.5 不支持 W028） |
| 5 外键 | 5.1–5.3 | 执行 | 执行 | rebuild 内联 |
| 6 约束 | 6.1–6.6 | 8.0 全执行；5.7 的 6.3–6.6 缺口 | 执行 | 唯一走索引；检查/默认值 rebuild |
| 7 注释 | 7.1–7.4 | 表列执行；索引/约束 W031 | 四种全部执行 | 全部跳过（W006/W007/W031） |
| 8 视图 | CREATE/REPLACE/DROP + columns + `<sql>` 透传 | 执行 | 执行 | 执行 |
| 9 序列 | CREATE/MODIFY/DROP | E046 | 执行（start/increment/min/max/cycle/cache） | E046 |
| 10 触发器 | CREATE/REPLACE/DROP | rawSql | rawSql + 真触发 | rawSql |
| 11 过程/函数 | PROCEDURE + FUNCTION | 两类执行 | 两类执行 | 无例程，rawSql 等价视图 |
| 12 数据 | SEED(4 策略)/UPDATE/DELETE/CLEAN/MIGRATE/BACKFILL + stage | 4 策略全测（DEFAULT/ERROR 见 T12b，IGNORE/UPDATE 见 T12c） | 同左（UPDATE 走 `ON CONFLICT DO UPDATE`，需基线主键） | 同左（`INSERT OR IGNORE` / `ON CONFLICT DO UPDATE`，需 3.24+） |
| 13 高级对象 | 13.1–13.6 | 全部执行（13.5 临时账号） | 13.3–13.6；13.1/13.2 → E047 | 13.4/13.6 rawSql 模拟 |
| T15 类型映射 | 20 种 `LogicalType` 各建一列 + Blob 带 length | 全部映射（`tinyint(1)`/`decimal(12,3)`/`json`/`longblob`；Xml→W004、DTO→W003） | 全部映射（`jsonb`/`xml`/`bytea`/`uuid`；Int8/Int16 都→smallint） | 归并为 integer/real/numeric/text/blob 五族，Json/Xml 有 W004 |
| T15b 类型校验 | String/FixedString/Binary 缺 length | → `E018` | → `E018` | → `E018`；Blob 带 length 三方言均 `W005` |

rawSql 专项（T14 系列）：直接文本/CDATA、多语句、字符串内分号、`''` 转义、行注释、`BEGIN…END`、`$$`、
三种标识符引用、`any` 回退、缺方言 W032/W033、空内容 E051、拆分失败 E042、`stage` 排序（T14e 编译期断言）。

## 本轮实测（本机）

| 方言 | 版本 | 用例 | 检查项 | 失败 | 警告 | 缺口 | 跳过 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MySQL | 5.7.18 | 24/24 | 152 | 0 | 12 | 5 | 0 |
| PostgreSQL | 18.4 | 27/27 | 147 | 0 | 7 | 0 | 4 |
| SQLite | 3.41.2 | 26/26 | 160 | 0 | 28 | 1 | 12 |

## 剩余缺口（非本次修复范围）

1. **MySQL 5.7 版本能力**：函数索引（8.0.13+）、`CHECK` 不生效、`DROP CHECK` 语法、`SET/DROP DEFAULT`
   —— 8.0 上全部可用，5.7 属服务器限制。
2. **SQLite 类型系统**：不保存长度/精度，无注释机制；`MODIFY`/主键/CHECK 只能 rebuild，
   rebuild 依赖基线模型（`BaselineXml`）且不恢复历史索引（W030）。

## 已修复（本轮）

| 问题 | 修复 |
| --- | --- |
| 列级 `unique` 被静默忽略 | 四方言列定义输出 `UNIQUE`；PG 的 MODIFY 额外生成 `ADD CONSTRAINT uk_<t>_<c> UNIQUE`（`unique="false"` 时 `DROP CONSTRAINT IF EXISTS`） |
| PG `MODIFY autoIncrement` 生成空语句 | 报 `E052`，提示改用 `<rawSql>` 重建列 |
| `<rawSql stage="before">` 不参与排序 | `SchemaCompiler.Rank` 读取 `Stage`，before 落在数据前置段（T14e 编译期顺序断言） |
| 空 `<sql>` 子节点静默跳过 | `ReadDialectSql` 跳过空文本，rawSql/trigger/sequence 缺内容统一报 `E051` |
| full 模式下每条 MODIFY 都告警 W008 | 仅“增量模式且原列存在默认值、本次未声明”时告警（把丢默认值的真实风险留下） |
| 测试侧弱断言 | PG `DROP DEFAULT` 改查 `column_default` 为空；GRANT/REVOKE 用临时角色 `dcops_grantee` 验证“授权为真、回收为假”；Npgsql 加 `IncludeErrorDetail=true`（错误明细不再被抹掉） |
| PG TABLESPACE 只验证语句可执行 | setup 在 `data_directory/dcops_ts` 建目录并 `CREATE TABLESPACE`，断言 `pg_class.reltablespace` 真落到 `dcops_ts`，结束时 `ALTER … SET TABLESPACE pg_default` + `DROP TABLESPACE`（目录由 PG 清空后保留，供下次复用；不要手工删该目录，否则会干扰 PG 后台清理） |
| T13b 同一错误打印两遍 | ENGINE 与 CHARSET 合并为一个节点，`E047` 只报一次 |
| 视图定义带换行影响阅读 | 断言前把 `pg_get_viewdef` / `SHOW CREATE VIEW` 压成单行 |
| `REORDER before` 被静默丢弃 | 解析期报 `E053`（"请改用 after/FIRST"），三方言统一；T02d 覆盖，MySQL 缺口从 6 降到 5 |
| MySQL TABLESPACE 只验证语句可执行 | 独立表 `dcops_t13_ts_tbl` 移入 `dcops_ts`，用 `INNODB_SYS_TABLES` JOIN `INNODB_SYS_TABLESPACES` 断言归属；分区表因 5.7 不支持放入通用表空间，已拆到另一张表 |
| MySQL GRANT 只验证账号被删 | 查 `information_schema.TABLE_PRIVILEGES`：授权后 `Y` → REVOKE 后 `N` → 清理账号 |
| 7.3 PASS / 7.4 SKIP 口径不一致 | 统一为 PASS（两者都断言 W031 已产生） |
| `Program.Main` 固定 `return 0` | 两个 Scenario 的 `Run()` 改为返回 `bool`，`Main` 据此返回 0/1，可进 CI |
| 中文路径下 `dotnet run` 报 `Invalid runtimeconfig.json` | 新增 `run-ops.cmd`（`chcp 65001` + 直接跑 dll），双击可用、支持 `run-ops.cmd mysql` |
| 预期失败只判断版本 | 新增 `ExpectedExecutionErrorContains`，T04/T06 必须命中 `(LOWER` / `CHECK`，否则判 FATAL |
