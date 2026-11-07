# DbChronicle 管道协议（分隔符文本）

编译结果不落盘，由 `db-chronicle compile` 直接写进管道。协议刻意做成「纯文本 + 分隔符」：
上层拿到一整段字符串，按 `0x1E` 切开就是所有记录，**没有长度的 SQL 记录就是可直接执行的 SQL**。

## 1. 为什么不用文件 / 不用长度前缀

| 方案 | 问题 |
| --- | --- |
| 写 `*.sql` 文件 | 运行时产物落盘，会被终端加密/防泄漏软件加密或锁定，读回失败；还要清理残留 |
| 行分隔（`\n`） | SQL 本身可含换行（字符串字面量、rawSql），会被撞 |
| 长度前缀二进制帧 | 二进制安全，但上层要写状态机，读 SQL 要跳头部、算长度 |
| **分隔符文本（本协议）** | 上层 `Split` 即可；SQL 是裸文本；分隔符选 ASCII 控制字符，SQL/XML 都不可能产出 |

## 2. 分隔符

| 字符 | 值 | 用途 |
| --- | --- | --- |
| RS（记录分隔符） | `0x1E` | 每条记录以它结尾 |
| US（字段分隔符） | `0x1F` | 控制记录内部的字段分隔 |
| SOH（控制前缀） | `0x01` | 控制记录首字节；SQL 记录没有 |

选控制字符而不是 `\n`、`\t`、`|`、`\0`：前几者都可能出现在 SQL 文本里；而 `0x01/0x1E/0x1F` 是 XML 1.0 明确禁止的字符，生成器不会产出。**因此不需要任何转义**。若内容里真的出现（例如绕过 XML 直接调 API），写入方直接报错中止，不会静默产生歧义。

## 3. 记录格式

- **SQL 记录**：SQL 原文（UTF-8），无前缀、无包装，可含换行与引号。
- **控制记录**：`0x01` + 类型字母 + (`0x1F` + 字段)*

| 控制记录 | 字节形态 | 含义 |
| --- | --- | --- |
| 版本 | `0x01 V 0x1F 2` | 必须是第一条记录 |
| 单元开始 | `0x01 U 0x1F <unitId> 0x1F <release>` | 后续 SQL 属于该单元 |
| 单元结束 | `0x01 E` | 此处提交该单元事务 |
| 诊断 | `0x01 D 0x1F <severity> 0x1F <code> 0x1F <message>` | 编译错误/告警 |
| 流结束 | `0x01 Z` | 完整结束标记 |

顺序：`V → U → (D* | SQL*) → E → … → Z`。

## 4. 上层怎么读

最小实现（任何语言）：把整段输出按 `0x1E` 切分，逐条判断首字符：

```text
首字符 != 0x01  → 就是一条 SQL，直接执行
首字符 == 0x01  → 控制记录，按 0x1F 再切字段
```

只想拿所有 SQL 时，过滤掉 `0x01` 开头的项即可。

### C++（本仓库实现）

见 `SnibeMiddlewareLauncher/GlobalFunction.cpp` 的 `RunProcessPipe`：边读边在缓冲里找 `0x1E` 切记录（单记录内存，不整段缓存），每条按严格 UTF-8 解码，`0x01 Z` 视为完整结束。业务回调见 `DBCtrl.cpp` 的 `HandleCompileFrame`。

```cpp
// 逐记录回调里的 SQL 分支（iType == 0）
vecSql.push_back(tagFrame.strText);
```

### C#

```csharp
using (var stream = /* stdout 或 NamedPipeClientStream */)
using (var reader = new DbChronicle.Piping.PipeRecordReader(stream))
{
    PipeRecord record;
    while ((record = reader.Read()) != null)
    {
        if (record.IsStatement) { /* record.Sql 直接执行 */ }
        else if (record.Type == PipeRecordType.UnitBegin) { /* record.UnitId / record.Release */ }
        else if (record.Type == PipeRecordType.UnitEnd) { /* 提交事务 */ }
        else if (record.Type == PipeRecordType.Diagnostic) { /* record.Severity/Code/Message */ }
        else if (record.Type == PipeRecordType.StreamEnd) { /* 完整结束 */ }
    }
}
```

核心库 `DbChronicle.Piping` 已提供 `PipeRecordReader` / `PipeRecordWriter` / `PipeProtocol`，直接复用即可。

### Java

```java
String text = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
for (String record : text.split("\u001E", -1)) {
    if (record.isEmpty()) continue;
    if (record.charAt(0) != '\u0001') {
        // 一条 SQL
        execute(record);
        continue;
    }
    String[] parts = record.substring(1).split("\u001F", -1);
    switch (parts[0]) {
        case "V": /* parts[1] 应为 "2" */ break;
        case "U": /* parts[1]=unitId, parts[2]=release */ break;
        case "E": /* 提交事务 */ break;
        case "D": /* parts[1]=severity, parts[2]=code, parts[3]=message */ break;
        case "Z": /* 完整结束 */ break;
    }
}
```

Java 版为了直观整段读入内存；语句量很大时可换成按 `0x1E` 的流式切分。

## 5. 命令行契约

```
db-chronicle compile --no-config --migrations <dir> --dialect <name> --allow-destructive --emit pipe
db-chronicle compile --no-config --migrations <dir> --dialect <name> --allow-destructive --pipe <pipeName>
```

- **`--no-config` 即「零配置模式」**：完全不读、也不生成 `DbchronicleConfig.xml`，所有取值来自命令行 + 代码默认值。适合嵌入式宿主（宿主目录里不需要任何 DbChronicle 配置文件，`config\` 目录也不会出现明文 XML）。
- 传 `--migrations` 而不传 `--no-config` 时同样进入零配置模式（兼容旧脚本）；推荐显式写 `--no-config`。
- `--dialect` 接受别名：`postgres`/`pg`/`pgsql` → `postgresql`，`mssql` → `sqlserver`，宿主可直接传数据库类型原值。
- `--emit pipe` 与 `--pipe` 互斥，且与 `--split` / `--output-dir` / `--out` / `--all-dialects` 互斥。
- 支持 `--pending` / `--change-id`（`--pending` 需要状态文件，嵌入式链路不用）。
- **不创建任何文件**（不建目录、不写 `*.sql`、不写 `_units.txt`、不写 `version_log`、不生成配置）。
- 人类可读诊断与进度一律走 stderr；stdout 只有协议内容。
- 退出码：`0` 成功；`3` 有编译错误；`1` 参数错误。

### 配置项 ↔ 命令行参数

配置文件里的每一项都有对应参数，`--no-config` 下全部由参数（或代码默认值）决定：

| 配置项 | 参数 | 默认 |
| --- | --- | --- |
| `database.dialect` | `--dialect <name>` | `mysql` |
| `database.version` | `--db-version <ver>` | 方言内置版本 |
| `environment` | `--environment <name>` | `dev` |
| `paths.migrations` | `--migrations <dir>` | `./migrations` |
| `paths.versionLog` | `--version-log <dir>` | `./version_log/${environment}` |
| `paths.output` | `--output-dir <dir>` | `./output` |
| `baseline.file` | `--baseline <file>` | 空 |
| `baseline.frozen` | `--baseline-frozen` / `--no-baseline-frozen` | `false` |
| `baseline.frozenAt` | `--baseline-frozen-at <value>` | 空 |
| `options.quoteIdentifiers` | `--quote-identifiers` / `--no-quote-identifiers` | `true` |
| `options.includeComments` | `--sql-comments` / `--no-sql-comments` | `true` |
| `options.includeRollback` | `--include-rollback` / `--no-include-rollback` | `false` |
| `options.statementDelimiter` | `--statement-delimiter <s>` | `;` |
| `options.allowDestructive` | `--allow-destructive` | `false` |
| `options.lockTimeoutSeconds` | `--lock-timeout <sec>` | `10` |
| `options.incrementalModify` | `--incremental-modify` / `--no-incremental-modify` | `true` |
| `options.commitScope` | `--commit-scope <unit\|table\|batch>` | `unit` |
| `options.commitBatchSize` | `--commit-batch-size <n>` | `10` |

## 6. 语义

- 每条 SQL 已去掉首尾空白与结尾分号，**保留原始换行与内部注释**（压平会破坏字符串字面量里的换行）；按单条执行，不要再按 `;` 拆分。
- 每个单元先 `U` 后 `E`，调用方应在 `E` 处提交事务，与旧 `--split` 的「逐单元」语义一致。
- **版本戳约定**：每个单元的语句中必须包含 `UPDATE nor_dbversion SET dbversion = '<changeUnit id>'`，且必须是**完整 id**（`yyyyMMdd_HHmmss[_后缀]`）。调用方按 id 字典序判断“是否已应用”，并把库里的 8 位日期版本视为当天 `00:00:00` 的单元（`20260625` ≡ `20260625_000000`）。若写缩短形式（只写 `yyyyMMdd`），同一天发布的第 2 个及后续单元会在回读校验时报 `did not stamp version`，也会被永久跳过。
- 某单元编译失败：该单元不发 SQL，只发 `D`，随后 `E`；整条流仍以 `Z` 收尾，退出码为 `3`。
- **必须收到 `Z`** 才认为流完整；没有 `Z` 代表截断（进程崩溃 / 管道提前关闭），判失败。

## 7. 限额

| 项 | 限额 | 处理 |
| --- | --- | --- |
| 单条记录 | 16 MB | 写方报错退出，读方也需拒收 |
| 单流总量 | 256 MB | 写方/读方都报错 |
| stderr | 1 MB（建议） | 超出截断，仅作日志 |
| 子进程时长 | 建议 120 s | 超时终止并判失败 |

## 8. 与文件模式的关系

`compile` 不带 `--emit` / `--pipe` 时仍写 `output/<dialect>.sql` 或 `--split` 的逐语句文件（人工查看 / CI 归档用）。中间件升级链路只用管道模式，因此升级过程零 SQL 落盘。

## 9. 已知边界

- 只保证「编译产物不落盘」。输入侧的 `migrations` 目录若被终端加密成 `.nii`，由**宿主**（SnibeMiddlewareLauncher）在调用前用 NIICrypt 的 COM 接口（`ICrypto` / 容器 `ORC4C_AND_NII`）批量解密到系统临时目录 `%TEMP%\dbchronicle-<pid>-<tick>\`，把该临时目录作为 `--migrations` 传入，进程结束（成功或失败）立即删除。CLI 自身不处理 `.nii`，也不依赖任何加密组件。
- 宿主解密后会做一次内容自检（文件首字节必须是 `<`），避免“解密产物又被加密”时静默把密文喂给编译器；自检失败按启动失败处理并写日志。
- 因为零配置模式（`--no-config`）下不需要 `config\dbchronicle.xml`，宿主目录可以只保留 `cli\` 与 `migrations\` 两项。
- 命名管道需要调用方先创建服务端（`NamedPipeServerStream(PipeDirection.In)`），CLI 作为客户端连接，连接超时 10 秒。
- 协议版本由第一条 `V` 记录的字段给出；读到不认识的版本直接失败。
