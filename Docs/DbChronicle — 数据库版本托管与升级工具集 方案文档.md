# DbChronicle — 数据库版本托管与升级工具集 方案文档

> 版本：v1.0  
> 定位：配置驱动的数据库版本托管与升级工具集  
> 组成：核心库（DbChronicle）+ 命令行工具（DbChronicle.Cli）+ 反向工程模块（Scanner）+ 执行器模块（Executor）  
> 核心能力：逻辑模型 → 方言 SQL 的编译  
> 技术栈：C# / .NET Standard 2.0（Core）+ .NET 8（Cli）  
> 合规要求：所有第三方依赖均为开源免费、允许商用的许可证  
> 一期数据库：MySQL、MariaDB、PostgreSQL、SQL Server、SQLite

---

## 一、背景与目标

### 1.1 现状问题

现有项目通过手写 SQL 的 XML 文件维护数据库版本升级，存在以下问题：

| 问题       | 表现                              |
| ---------- | --------------------------------- |
| 数据库绑定 | SQL 写死 MySQL 方言，换 PG 需重写 |
| 并行冲突   | 多人同一版本号，文件互相覆盖      |
| 无幂等     | 重复执行报错                      |
| 无跨库能力 | 类型、语法、注释全绑 MySQL        |
| 无审计     | 无版本记录、无 checksum           |
| 手写易漏   | 合并时漏 SQL                      |
| 补丁无防护 | 已应用文件被修改，静默失效        |

### 1.2 目标

构建一套工具集，包含核心库和 CLI 工具，实现：

1. **逻辑模型配置化**：用与数据库无关的逻辑类型描述表结构变更。

2. **方言自动转换**：同一份逻辑模型，编译为多种目标数据库的 SQL。

3. **一期数据库支持**：首期覆盖 MySQL、MariaDB、PostgreSQL、SQL Server、SQLite 五种数据库，按支持深度分三层：

   **完整支持：**

   | 数据库     | 版本要求   | 说明                                                                    |
   | ---------- | ---------- | ----------------------------------------------------------------------- |
   | MySQL      | 5.7 / 8.0+ | 5.7 与 8.0 的 `RENAME COLUMN` 语法差异由版本配置区分                    |
   | PostgreSQL | 12+        | 方言与 MySQL 差异典型（类型、独立 `COMMENT ON`、`ALTER COLUMN` 拆多条） |
   | SQL Server | 2016+      | 方言差异集中在默认值约束命名、`sp_rename`、`NVARCHAR`                   |

   **兼容复用：**

   | 数据库  | 版本要求 | 说明                                                                                                          |
   | ------- | -------- | ------------------------------------------------------------------------------------------------------------- |
   | MariaDB | 10.2.7+  | 复用 MySQL 方言类；`JSON` 类型因 MariaDB 无原生 JSON，映射为 `LONGTEXT` 并附带 `CHECK (JSON_VALID(col))` 约束 |

   **受限支持：**

   | 数据库 | 版本要求 | 说明                                                                                                                                                                            |
   | ------ | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
   | SQLite | 3.35.0+  | 采用 rebuild 为主的策略：`ALTER COLUMN` 不支持，自动走「建新表→导数据→删旧→重命名」；`ADD COLUMN` 受限；注释不支持；类型映射为 SQLite 存储类；外键需 `PRAGMA foreign_keys = ON` |

4. **版本托管与迭代**：版本号由逻辑模型声明（`release="yyyyMMdd"`），数据库记录权威状态，`version_log/` 文件夹留档 SQL 审计日志。

5. **零 ORM 依赖**：核心库不引用任何 ORM，执行层可插拔。

6. **库 + 工具双形态**：核心能力做成 NuGet 库，入口做成 CLI 工具；C# 项目引用库进程内调用，其他语言 exec CLI 跨进程调用。

7. **许可证合规**：所有第三方依赖均采用 MIT / Apache 2.0 / PostgreSQL / Public Domain 等宽松许可证，允许商业使用。

8. **文件唯一性四层防护**：生成层 `FileMode.CreateNew`、版本控制层 Git 冲突保护、执行层 checksum 强校验、CI 层 `check` + `verify` 门禁，彻底规避文件覆盖。

### 1.3 非目标

- 不做全量快照 diff（采用增量变更集）。
- 不做运行时自动迁移（迁移离线执行）。
- 不做独立服务（HTTP/守护进程）。
- 不做分区、表空间、权限等特殊对象（按方言写原生 SQL）。
- 一期不做无符号整数逻辑类型（MySQL 通过 `unsigned="true"` 属性表达，其他方言忽略）。
- 一期不实现代码生成（XML → 实体类），二期按需。
- 一期不支持按 release 自动回滚，二期按需。

---

## 二、整体架构

### 2.1 架构总览

DbChronicle 是一套工具集，按**语言边界**分为三层：语言无关层（Core、Cli、Scanner.Database）、语言相关层（Scanner.Xxx、CodeGen.Xxx）。整体架构如下：

```
┌─────────────────────────────────────────────────────────────────┐
│  DbChronicle 工具集                                              │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  语言无关层                                                │ │
│  │                                                           │ │
│  │  ┌─────────────────────────────────────────────────────┐ │ │
│  │  │  DbChronicle（核心库，NuGet）                   │ │ │
│  │  │  正向编译：XML → SQL                                │ │ │
│  │  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐ │ │ │
│  │  │  │ Config │ │ Parser │ │Compiler│ │   Dialect    │ │ │ │
│  │  │  │ 加载配置│ │解析模型 │ │编译 SQL│ │ 方言(5种)     │ │ │ │
│  │  │  └────────┘ └────────┘ └────────┘ └──────────────┘ │ │ │
│  │  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐ │ │ │
│  │  │  │ Naming │ │Operation│ │Executor│ │  TypeMapper  │ │ │ │
│  │  │  │ 文件名  │ │操作模型 │ │执行接口 │ │ 类型映射      │ │ │ │
│  │  │  └────────┘ └────────┘ └────────┘ └──────────────┘ │ │ │
│  │  └─────────────────────────────────────────────────────┘ │ │
│  │                                                           │ │
│  │  ┌─────────────────────┐  ┌────────────────────────────┐ │ │
│  │  │ DbChronicle.Cli     │  │ Scanner.Database（二期）    │ │ │
│  │  │ 命令行入口           │  │ 数据库 → XML（语言无关）     │ │ │
│  │  └─────────────────────┘  └────────────────────────────┘ │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  语言相关层（Scanner 按语言独立实现）                       │ │
│  │                                                           │ │
│  │  ┌──────────────────┐  ┌──────────────────┐              │ │
│  │  │ Scanner.CSharp   │  │ Scanner.Java     │              │ │
│  │  │ C# 实体 → XML    │  │ Java 实体 → XML  │              │ │
│  │  │ （一期可选）      │  │ （二期）          │              │ │
│  │  └──────────────────┘  └──────────────────┘              │ │
│  │  ┌──────────────────┐  ┌──────────────────┐              │ │
│  │  │ Scanner.Go       │  │ Scanner.TypeScript│             │ │
│  │  │ （二期）          │  │ （二期）           │             │ │
│  │  └──────────────────┘  └──────────────────┘              │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  代码生成层（按语言独立实现，二期）                          │ │
│  │  CodeGen.CSharp / CodeGen.Java / CodeGen.Go / CodeGen.TS  │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 模块职责

| 模块                                                           | 形态               | 一期   | 语言相关性    | 宿主语言                | 职责                            |
| -------------------------------------------------------------- | ------------------ | ------ | ------------- | ----------------------- | ------------------------------- |
| `DbChronicle`                                                  | 库（NuGet）        | ✅      | 语言无关      | C# / .NET               | 正向编译：XML → SQL，进程内调用 |
| `DbChronicle.Cli`                                              | 工具（可执行文件） | ✅      | 语言无关      | 任何语言（exec 子进程） | 命令行入口                      |
| `DbChronicle.Scanner.CSharp`（命名空间，内置于 `DbChronicle`） | 内置               | ⚠️ 可选 | **C# 相关**   | C# / .NET               | C# 实体 → XML                   |
| `DbChronicle.Scanner.Java`                                     | 库                 | ❌ 二期 | **Java 相关** | Java                    | Java 实体 → XML                 |
| `DbChronicle.Scanner.Go`                                       | 库                 | ❌ 二期 | **Go 相关**   | Go                      | Go 实体 → XML                   |
| `DbChronicle.Scanner.TypeScript`                               | 库                 | ❌ 二期 | **TS 相关**   | TypeScript              | TS 实体 → XML                   |
| `DbChronicle.Scanner.Database`                                 | 库                 | ❌ 二期 | 语言无关      | 任何语言（CLI 调用）    | 数据库 → XML                    |
| `DbChronicle.CodeGen.CSharp`                                   | 库                 | ❌ 二期 | **C# 相关**   | C# / .NET               | XML → C# 实体                   |
| `DbChronicle.CodeGen.Java`                                     | 库                 | ❌ 二期 | **Java 相关** | Java                    | XML → Java 实体                 |
| `DbChronicle.Executor.AdoNet`（命名空间，内置于 CLI）          | 内置               | ✅      | 语言无关      | C# / .NET               | ADO.NET 执行器（CLI 默认）      |
| `DbChronicle.Executor.EfCore`                                  | 库（NuGet）        | ❌ 二期 | 语言无关      | C# / .NET               | EF Core 执行器（应用内嵌）      |
| `Resources/dialects/*.types.xml`                               | 嵌入资源           | ✅      | 语言无关      | —                       | 五种数据库的类型映射表          |

### 2.3 数据流

**正向编译（一期核心）：**

```
逻辑模型 XML（migrations/*.xml）
      │
      │ Parser
      ▼
ChangeUnit 对象
      │
      │ TypeMapper + ISqlDialect
      ▼
方言 SQL（output/{dialect}/*.sql）
      │
      │ IDbExecutor（可选）
      ▼
数据库 + version_log 表 + version_log/{release}_dbupdate.md
```

**反向工程（一期可选）：**

```
C# 实体类（.cs / 程序集）
      │
      │ Scanner.CSharp
      ▼
种子逻辑模型 XML（migrations/00000000_000000_baseline.xml）
      │
      │ 人工确认、修正
      ▼
基线冻结
```

**代码生成（二期）：**

```
逻辑模型 XML
      │
      │ CodeGen.CSharp / Java / Go / TS
      ▼
各语言实体类
```

### 2.4 核心原则

- **Core 零数据库驱动依赖，零运行时依赖。** 只依赖 `YamlDotNet`，不引用任何数据库驱动、ORM。
- **方言是唯一按数据库扩展的点。** 新增数据库 = 一份 YAML + 一个方言类，Core 主流程不动。
- **执行是可选出口，不属于 Core。** `IDbExecutor` 接口注入，ADO.NET / EF Core / 自定义实现均可。
- **语言映射服务于反向工程与代码生成，Core 正向编译不依赖。** 这是职责边界。
- **库 + 工具双形态。** Core 是库，可被 C# 项目引用；Cli 是工具，任何语言可 exec。
- **XML 是语言中立的契约。** 任何语言产出的 XML，Core 都能编译。
- **版本号由逻辑模型声明，数据库是权威，日志文件夹是审计。** 三层职责分离。

### 2.5 交付形态

| 交付物                           | 形态               | 说明                                                     |
| -------------------------------- | ------------------ | -------------------------------------------------------- |
| `DbChronicle`                    | 库（NuGet）        | 被 C# 项目引用，无入口                                   |
| `DbChronicle.Cli`                | 工具（可执行文件） | 独立运行，有入口，任何语言可 exec                        |
| `DbChronicle`                    | 库（NuGet）        | 核心库（编译 / 执行 / 宿主集成 / C# 反向扫描，单一 dll） |
| `DbChronicle.Executor.EfCore`    | 库（NuGet）        | 可选执行器（二期）                                       |
| `DbChronicle.Scanner.Database`   | 库（NuGet）        | 可选，数据库反向工程（二期）                             |
| `DbChronicle.CodeGen.CSharp`     | 库（NuGet）        | 可选，代码生成（二期）                                   |
| `Resources/dialects/*.types.xml` | 嵌入资源           | 类型映射表                                               |
| 整体                             | 工具集             | 多个交付物的组合                                         |

### 2.6 部署形态

```
形态一：纯 CLI（推荐默认，跨语言通用）
┌──────────────────────────────────────┐
│  db-chronicle.exe（自包含）           │
│  DbchronicleConfig.xml             │
│  migrations/                         │
│  version_log/                        │
│  output/                             │
└──────────────────────────────────────┘

形态二：应用内嵌（C# 项目）
┌──────────────────────────────────────┐
│  C# 应用进程                          │
│  ├── DbChronicle（引用 NuGet，含执行器与宿主集成） │
│  └── 业务代码（ORM 只做 CRUD）        │
└──────────────────────────────────────┘

形态三：CI/CD
┌──────────────────────────────────────┐
│  流水线                               │
│  ├── compile 步骤 → 生成 SQL 脚本     │
│  ├── 人工审核 SQL                     │
│  └── apply 步骤 → 执行到数据库        │
└──────────────────────────────────────┘
```

### 2.7 多语言兼容性

DbChronicle 的核心库和 CLI 是语言无关的，任何语言的项目都能使用。

**两种集成方式：**

| 宿主语言                           | 集成方式                           | 调用形态            | 一期 |
| ---------------------------------- | ---------------------------------- | ------------------- | ---- |
| C# / .NET                          | 引用 `DbChronicle` NuGet 包        | 进程内，直接调 API  | ✅    |
| Java / Go / TypeScript / Python 等 | 调用 `db-chronicle` CLI 可执行文件 | 跨进程，exec 子进程 | ✅    |

**C# 项目的使用方式：**

```csharp
// 引用 DbChronicle NuGet 包
var compiler = new SchemaCompiler(dialect, options);
var sql = compiler.Compile(unit);
```

**非 C# 项目的使用方式（exec CLI）：**

```java
// Java
ProcessBuilder pb = new ProcessBuilder("db-chronicle", "apply",
    "--config", "DbchronicleConfig.xml");
pb.environment().put("DBCHRONICLE_CONN", connectionString);
pb.inheritIO();
int exitCode = pb.start().waitFor();
```

```go
// Go
cmd := exec.Command("db-chronicle", "apply", "--config", "DbchronicleConfig.xml")
cmd.Env = append(os.Environ(), "DBCHRONICLE_CONN="+connStr)
cmd.Stdout = os.Stdout
cmd.Stderr = os.Stderr
if err := cmd.Run(); err != nil {
    log.Fatalf("迁移失败: %v", err)
}
```

```typescript
// TypeScript
import { execFile } from 'child_process';
import { promisify } from 'util';

const execFileAsync = promisify(execFile);
await execFileAsync('db-chronicle', ['apply', '--config', 'DbchronicleConfig.xml'], {
    env: { ...process.env, DBCHRONICLE_CONN: connStr }
});
```

```python
# Python
import subprocess, os
subprocess.run(
    ['db-chronicle', 'apply', '--config', 'DbchronicleConfig.xml'],
    env={**os.environ, 'DBCHRONICLE_CONN': conn_str},
    check=True
)
```

**CLI 的部署形态：**

| 形态                                            | 依赖             | 适用                       |
| ----------------------------------------------- | ---------------- | -------------------------- |
| 自包含 exe（`dotnet publish --self-contained`） | 无 .NET 运行时   | 服务器、Docker、跨语言调用 |
| 框架依赖（`dotnet tool install -g`）            | 需 .NET 8 运行时 | 开发机、C# 项目            |

**跨语言调用的标准做法：** Flyway、Liquibase 都是「Java 项目用 jar，非 Java 项目用 CLI」的模式。CLI 是跨语言调用的标准边界，不是妥协。

**二期可选：** 为常用语言（Java/Go/TypeScript）提供薄 SDK 包装，内部仍 exec CLI，对外暴露更友好的 API：

```java
// Java SDK 示例（二期可选）
DbChronicle.apply("DbchronicleConfig.xml", connectionString);
```

**非 C# 项目的基线生成：**

| 方式                                    | 适用场景              | 一期可用 |
| --------------------------------------- | --------------------- | -------- |
| 手写基线 XML                            | 实体不多（< 20 张表） | ✅        |
| `Scanner.Database` 从现有数据库反向生成 | 数据库已存在          | ❌ 二期   |
| 对应语言的 Scanner（如 `Scanner.Java`） | 实体多，长期维护      | ❌ 二期   |

XML 格式是语言中立的契约，Core 不关心它从哪来。

### 2.8 跨语言集成方式

DbChronicle 提供两种集成方式，按宿主语言区分：

| 宿主语言                           | 集成方式                           | 调用形态            | 一期 |
| ---------------------------------- | ---------------------------------- | ------------------- | ---- |
| C# / .NET                          | 引用 `DbChronicle` NuGet 包        | 进程内，直接调 API  | ✅    |
| Java / Go / TypeScript / Python 等 | 调用 `db-chronicle` CLI 可执行文件 | 跨进程，exec 子进程 | ✅    |

**一期推荐：** C# 项目引用 NuGet 包，其他语言直接 exec CLI。简单、可靠、零额外维护。

**二期可选：** 为常用语言（Java/Go/TypeScript）提供薄 SDK 包装，内部仍 exec CLI，对外暴露更友好的 API。

---

## 三、配置设计

### 3.1 工具集配置（DbchronicleConfig.xml）

CLI 启动或核心库初始化时加载，决定方言、版本、路径、基线、选项。首次运行自动生成默认配置。

**连接信息不在此文件中**，见 3.2。**版本号不在此文件中**，由逻辑模型的 `release` 属性声明，由数据库记录。

```xml
<?xml version="1.0" encoding="utf-8"?>
<dbchronicle>
  <component name="DbChronicle" version="1.0.0" />

  <!-- dialect: mysql / mariadb / postgresql / sqlserver / sqlite -->
  <database dialect="mysql" version="8.0" />

  <!-- dev / test / prod，用于 version_log 分目录 -->
  <environment>prod</environment>

  <!-- migrations: 逻辑模型目录；versionLog: 审计日志目录；output: 编译产物目录 -->
  <paths migrations="./migrations"
         versionLog="./version_log/${environment}"
         output="./output" />

  <!-- frozen=true 表示基线已冻结，禁止修改 -->
  <baseline file="./migrations/00000000_000000_baseline.xml"
            frozen="true"
            frozenAt="2026-10-22" />

  <!-- lockTimeoutSeconds: 库级互斥锁等待上限（0=立即失败，上限 300） -->
  <options quoteIdentifiers="true"
           includeComments="true"
           includeRollback="true"
           statementDelimiter=";"
           allowDestructive="false"
           lockTimeoutSeconds="10"
           incrementalModify="true" />
</dbchronicle>
```

**说明：** 配置格式为 XML（与逻辑模型统一），核心库因此不依赖 YamlDotNet。`paths.versionLog` 即审计日志目录（按 `${environment}` 分目录），`paths.migrations` 即逻辑模型目录。

**`incrementalModify` 说明：**

| 取值           | 语义                                                          | 适用场景                       |
| -------------- | ------------------------------------------------------------- | ------------------------------ |
| `true`（默认） | `MODIFY` 只写变化项，未写属性保持不变，由累积模型（5.10）补全 | 默认；符合"改哪项写哪项"的直觉 |
| `false`        | `MODIFY` 必须声明完整目标状态，未写属性视为"目标没有"         | 需要刻意清空属性、或无基线时   |

该开关只影响 `MODIFY` 的编译结果，`ADD` / `DROP` / `RENAME` 不受影响。

**默认开启的连带影响（必须知道）：**

1. 编译会尝试构建累积模型（5.10）补全未写属性；**没有基线**时无法补全，该列按你写的内容生成，未写的属性**不会从历史继承**，并输出 W014 警告——不阻断编译。
2. 表或列不在累积模型中（外部系统建的表、临时表）同样按列退化为"用你写的"，每列一条 W014。
3. 写全的历史文件不受影响：显式属性会完整覆盖基底，结果与原语义一致。
4. 需要"不写就是清空"的旧行为时（例如刻意去掉默认值），把开关置 `false`，或改用显式三态 `defaultValue="none"` / `comment=""`。

**加载流程：**

```
compile / new / init / check 命令
  → 读 DbchronicleConfig.xml
  → 不存在则生成默认配置并提示
  → 确定 dialect + version
  → 加载对应方言
  → 不涉及任何连接信息

apply / status / verify / scan-db 命令
  → 读 DbchronicleConfig.xml
  → 确定 dialect + version
  → 加载对应方言
  → 按 3.2 获取连接信息（环境变量 > 交互式输入）
```

**核心库不感知连接信息的存在。** `compile` 是纯离线字符串生成，`apply` / `status` 才需要连接，由 CLI 层解析后通过 `IDbExecutorFactory.Create(connectionString)` 传入执行器。

**版本号说明：**

- 版本号不在 config 中维护。
- 每个变更单元的 `<changeUnit release="20261022">` 属性声明所属版本。
- `version_log/DbchronicleState.xml` 记录权威状态（本地文件，不落库）。
- `version_log/{release}_dbupdate.md` 留档 SQL 审计日志。

### 3.2 连接信息获取

**连接信息不写入任何文件。** 需要连接数据库的命令（`apply` / `status` / `verify` / `scan-db`）通过以下方式获取连接信息，按优先级从高到低：

| 优先级 | 来源                        | 场景                    |
| ------ | --------------------------- | ----------------------- |
| 1      | 环境变量 `DBCHRONICLE_CONN` | CI/CD、容器等非交互环境 |
| 2      | CLI 交互式输入              | 开发机、DBA 手动执行    |

#### 3.2.1 CLI 交互式输入（默认方式）

开发机和 DBA 手动执行时，CLI 引导用户输入连接信息：

```
$ db-chronicle apply --config DbchronicleConfig.xml

数据库类型：mysql

请输入连接信息：
  主机 (localhost):
  端口 (3306):
  数据库名: mydb
  用户名: root
  密码: ****

正在连接 localhost:3306/mydb ...
连接成功。
```

**特性：**

- **默认值**：主机 `localhost`、端口按方言默认（MySQL 3306 / PG 5432 / SQL Server 1433）、用户名按方言默认（MySQL `root` / PG `postgres` / SQL Server `sa`），回车即用。
- **密码掩码**：`Console.ReadKey(intercept: true)` 实现，不回显、不进 shell 历史、不进进程列表。
- **必填校验**：数据库名不能为空，空输入时重新提示。
- **不落盘**：连接信息仅存在于当前进程内存，进程结束即消失。

#### 3.2.2 环境变量（非交互环境）

CI/CD、容器等无 TTY 环境通过环境变量提供连接串：

```yaml
# GitHub Actions
- name: Apply migrations
  env:
    DBCHRONICLE_CONN: ${{ secrets.DB_CONNECTION }}
  run: db-chronicle apply --config DbchronicleConfig.xml
```

```powershell
# 本地脚本
$env:DBCHRONICLE_CONN = "Server=localhost;Database=mydb;Uid=root;Pwd=***;"
db-chronicle apply --config DbchronicleConfig.xml
```

#### 3.2.3 加载逻辑

```csharp
public static string ResolveConnectionString(DatabaseConfig dbConfig)
{
    // 1. 环境变量（CI/CD、容器）
    var env = Environment.GetEnvironmentVariable("DBCHRONICLE_CONN");
    if (!string.IsNullOrEmpty(env)) return env;

    // 2. 交互式输入（开发机、DBA 手动执行）
    if (!Console.IsInputRedirected)
        return PromptConnection(dbConfig);

    // 3. 非交互环境且无环境变量，报错
    throw new InvalidOperationException(
        "非交互环境未找到连接信息。请设置 DBCHRONICLE_CONN 环境变量。");
}
```

`Console.IsInputRedirected` 为 `true` 时（管道、CI/CD、重定向），跳过交互，直接检查环境变量，避免卡死。

#### 3.2.4 不提供的能力

| 能力                                  | 不提供的原因                             |
| ------------------------------------- | ---------------------------------------- |
| 本地连接文件 `dbchronicle.local.yaml` | 交互式输入已覆盖开发机场景，避免密码落盘 |
| `--conn` 命令行参数                   | 连接串含密码，会进 shell 历史和进程列表  |
| `--conn-file` 命令行参数              | 同上                                     |
| `--password` 命令行参数               | 明文密码，极不安全                       |
| 密码持久化                            | 连接信息不落盘，进程结束即消失           |

#### 3.2.5 应用内嵌时的处理

应用引用 `DbChronicle` 库时，连接信息由**宿主应用管理**。核心库不读取环境变量，也不触发交互式输入：

```csharp
var connectionString = configuration.GetConnectionString("Default");
var executor = new SchemaExecutor(new AdoNetExecutorFactory(), dialect);
executor.ApplyPending(units, connectionString);
```

**核心库只接收 `connectionString` 字符串，获取方式由调用方决定。**

---

## 四、逻辑模型设计

逻辑模型是 DbChronicle 的**唯一事实来源**。它用与数据库无关的方式描述"要改什么"，不出现任何物理类型或方言语法。编译器负责将其翻译为目标数据库的 DDL。

### 4.1 操作全集

数据库结构迭代涉及的操作按对象类型分为 13 大类。

#### 4.1.1 操作分类总览

```
数据库结构操作
├── 1. 表级操作
├── 2. 列级操作（含类型、长度、精度、可空、默认值、注释、自增、唯一、无符号、onUpdate）
├── 3. 主键操作
├── 4. 索引操作
├── 5. 外键操作
├── 6. 约束操作（唯一、检查、默认值、非空）
├── 7. 注释操作（表、列、索引、约束）
├── 8. 视图操作
├── 9. 序列操作
├── 10. 触发器操作
├── 11. 存储过程 / 函数操作
├── 12. 数据操作（种子、更新、删除、清洗、迁移、回填）
└── 13. 高级对象（分区、字符集、存储引擎、表空间、权限、同义词）
```

#### 4.1.2 表级操作

| #   | 操作         | `action`     | 关键属性                     | 跨库难度 | 破坏性 |
| --- | ------------ | ------------ | ---------------------------- | -------- | ------ |
| 1.1 | 建表         | `CREATE`     | `name`、`comment`、列定义    | 低       | 否     |
| 1.2 | 删表         | `DROP`       | `name`                       | 低       | **是** |
| 1.3 | 重命名表     | `RENAME`     | `name`、`newName`            | 中       | 否     |
| 1.4 | 修改表注释   | `COMMENT`    | `name`、`comment`            | 低       | 否     |
| 1.5 | 修改存储引擎 | `ENGINE`     | `name`、`engine`             | 极高     | 否     |
| 1.6 | 修改字符集   | `CHARSET`    | `name`、`charset`、`collate` | 高       | 否     |
| 1.7 | 修改表空间   | `TABLESPACE` | `name`、`tablespace`         | 极高     | 否     |
| 1.8 | 重建表       | `REBUILD`    | `name`、完整列定义           | 中       | **是** |

**1.5 ~ 1.7（`ENGINE` / `CHARSET` / `TABLESPACE`）与 `PARTITION` / `GRANT` / `SYNONYM` 统一走 `<rawSql>`（见 4.1.11）**：这些 action 会报 E012 并提示改用 `<rawSql>`，不生成方言专用语法。`REBUILD` 仅做破坏性校验并告警 W018（SQLite 自动 rebuild，其他方言请用 `<rawSql>`）。

#### 4.1.3 列级操作

**列的存在性操作：**

| #   | 操作       | `action`  | 关键属性                 | 跨库难度 | 破坏性 |
| --- | ---------- | --------- | ------------------------ | -------- | ------ |
| 2.1 | 加列       | `ADD`     | `name`、`type`、全部属性 | 低       | 否     |
| 2.2 | 删列       | `DROP`    | `name`                   | 低       | **是** |
| 2.3 | 重命名列   | `RENAME`  | `name`、`renamedFrom`    | 中       | 否     |
| 2.4 | 调整列顺序 | `REORDER` | `name`、`after`/`before` | 高       | 否     |

**列属性修改：**

| #    | 操作          | `action`  | 关键属性                              | 跨库难度 | 破坏性   |
| ---- | ------------- | --------- | ------------------------------------- | -------- | -------- |
| 2.5  | 修改类型      | `MODIFY`  | `type`                                | 中       | **可能** |
| 2.6  | 修改长度      | `MODIFY`  | `length`                              | 低       | **可能** |
| 2.7  | 修改精度      | `MODIFY`  | `precision`                           | 低       | **可能** |
| 2.8  | 修改小数位    | `MODIFY`  | `scale`                               | 低       | **可能** |
| 2.9  | 修改可空性    | `MODIFY`  | `nullable`                            | 低       | **可能** |
| 2.10 | 修改默认值    | `MODIFY`  | `defaultValue`                        | 中       | 否       |
| 2.11 | 删除默认值    | `MODIFY`  | `defaultValue` 缺省（= 目标无默认值） | 中       | 否       |
| 2.12 | 修改列注释    | `COMMENT` | `comment`                             | 低       | 否       |
| 2.13 | 修改自增      | `MODIFY`  | `autoIncrement`                       | 高       | **是**   |
| 2.14 | 修改字符集    | `MODIFY`  | `charset`、`collate`                  | 高       | 否       |
| 2.15 | 修改列级唯一  | `MODIFY`  | `unique`                              | 中       | 否       |
| 2.16 | 修改无符号    | `MODIFY`  | `unsigned`                            | 中       | 否       |
| 2.17 | 修改 onUpdate | `MODIFY`  | `onUpdate`                            | 中       | 否       |

#### 4.1.4 主键操作

| #   | 操作   | `action`  | 关键属性                  | 跨库难度 | 破坏性 |
| --- | ------ | --------- | ------------------------- | -------- | ------ |
| 3.1 | 加主键 | `ADD`     | `columns`、`name`（可选） | 低       | 否     |
| 3.2 | 删主键 | `DROP`    | `name`                    | 中       | **是** |
| 3.3 | 换主键 | `REPLACE` | 新 `columns`              | 中       | **是** |

#### 4.1.5 索引操作

| #   | 操作       | `action` | 关键属性          | 跨库难度 | 破坏性 |
| --- | ---------- | -------- | ----------------- | -------- | ------ |
| 4.1 | 建普通索引 | `ADD`    | `name`、`columns` | 低       | 否     |
| 4.2 | 建唯一索引 | `ADD`    | `unique=true`     | 低       | 否     |
| 4.3 | 建复合索引 | `ADD`    | 多列              | 低       | 否     |
| 4.4 | 删索引     | `DROP`   | `name`            | 低       | 否     |
| 4.5 | 重命名索引 | `RENAME` | `name`、`newName` | 中       | 否     |
| 4.6 | 建函数索引 | `ADD`    | `expression`      | 高       | 否     |
| 4.7 | 建部分索引 | `ADD`    | `where`           | 高       | 否     |
| 4.8 | 建全文索引 | `ADD`    | `type=FULLTEXT`   | 高       | 否     |

#### 4.1.6 外键操作

| #   | 操作         | `action` | 关键属性                                                            | 跨库难度 | 破坏性 |
| --- | ------------ | -------- | ------------------------------------------------------------------- | -------- | ------ |
| 5.1 | 加外键       | `ADD`    | `name`、`columns`、`refTable`、`refColumns`、`onDelete`、`onUpdate` | 低       | 否     |
| 5.2 | 删外键       | `DROP`   | `name`                                                              | 低       | 否     |
| 5.3 | 修改级联规则 | `MODIFY` | `onDelete`、`onUpdate`                                              | 低       | 否     |

#### 4.1.7 约束操作

| #   | 操作         | `action` | 关键属性                  | 跨库难度 | 破坏性 |
| --- | ------------ | -------- | ------------------------- | -------- | ------ |
| 6.1 | 加唯一约束   | `ADD`    | `name`、`columns`         | 低       | 否     |
| 6.2 | 删唯一约束   | `DROP`   | `name`                    | 中       | 否     |
| 6.3 | 加检查约束   | `ADD`    | `name`、`expression`      | 低       | 否     |
| 6.4 | 删检查约束   | `DROP`   | `name`                    | 低       | 否     |
| 6.5 | 加默认值约束 | `ADD`    | `name`、`column`、`value` | 中       | 否     |
| 6.6 | 删默认值约束 | `DROP`   | `name`                    | 中       | 否     |

#### 4.1.8 注释操作

| #   | 操作   | `action`  | 关键属性         | 跨库难度 | 破坏性 |
| --- | ------ | --------- | ---------------- | -------- | ------ |
| 7.1 | 表注释 | `COMMENT` | `table.comment`  | 低       | 否     |
| 7.2 | 列注释 | `COMMENT` | `column.comment` | 低       | 否     |

#### 4.1.9 视图 / 序列 / 触发器 / 存储过程

这些对象跨库差异极大，**按方言写 `RawSqlOperation`**。

| #    | 操作                                    | `action` | 跨库难度 |
| ---- | --------------------------------------- | -------- | -------- |
| 8.x  | 视图 CREATE / REPLACE / DROP            | —        | 中       |
| 9.x  | 序列 CREATE / MODIFY / DROP             | —        | 高       |
| 10.x | 触发器 CREATE / REPLACE / DROP          | —        | 极高     |
| 11.x | 存储过程 / 函数 CREATE / REPLACE / DROP | —        | 极高     |

#### 4.1.10 数据操作

| #    | 操作         | `action`   | 关键属性                    | 跨库难度 | 破坏性   |
| ---- | ------------ | ---------- | --------------------------- | -------- | -------- |
| 12.1 | 种子数据插入 | `SEED`     | `table`、`rows`、`conflict` | 中       | 否       |
| 12.2 | 数据更新     | `UPDATE`   | `table`、`set`、`where`     | 中       | **可能** |
| 12.3 | 数据删除     | `DELETE`   | `table`、`where`            | 中       | **是**   |
| 12.4 | 数据清洗     | `CLEAN`    | `table`、`rules`            | 中       | **可能** |
| 12.5 | 数据迁移     | `MIGRATE`  | 源/目标列、转换规则         | 高       | **可能** |
| 12.6 | 数据回填     | `BACKFILL` | `table`、`column`、`value`  | 中       | 否       |

**数据操作的 XML 写法：**

```xml
<update   table="nor_user" set="status = 1" where="created_at &lt; '2020-01-01'"/>
<delete   table="nor_user" where="status = 0"/>
<clean    table="nor_user" rules="email = NULL; phone = NULL" where="email = ''"/>
<backfill table="nor_user" column="email" value="''" where="email IS NULL"/>
```

| 节点       | 必填               | 生成 SQL                               | 约束                                    |
| ---------- | ------------------ | -------------------------------------- | --------------------------------------- |
| `update`   | `set`              | `UPDATE t SET {set} [WHERE {where}]`   | `set` 缺失报 E022                       |
| `delete`   | —                  | `DELETE FROM t [WHERE {where}]`        | 需 `allowDestructive: true`             |
| `clean`    | `rules`            | `UPDATE t SET {rules} [WHERE {where}]` | `;` 分隔多条赋值，转成逗号；缺失报 E023 |
| `backfill` | `column` + `value` | `UPDATE t SET c = {value} [WHERE ...]` | 缺失报 E024                             |

`stage` 取值同 4.5.7，缺省 `after`；`stage="before"` 的数据操作会排在所有结构变更之前（4.2 第一段）。

#### 4.1.11 高级对象

| #    | 操作            | `action`           | 跨库难度   |
| ---- | --------------- | ------------------ | ---------- |
| 13.1 | 分区            | `PARTITION`        | 极高       |
| 13.2 | 字符集/排序规则 | `CHARSET`          | 高         |
| 13.3 | 存储引擎        | `ENGINE`           | MySQL 特有 |
| 13.4 | 表空间          | `TABLESPACE`       | 极高       |
| 13.5 | 权限            | `GRANT` / `REVOKE` | 中         |
| 13.6 | 同义词          | `SYNONYM`          | 高         |

全部用 `RawSqlOperation`，按方言写 SQL。

### 4.2 操作依赖顺序（编译器排序依据）

ChangeUnit 内的操作按「数据前置 → 拆依赖 → 结构变更 → 数据后置」四段排序：段间严格有序，段内保持声明顺序（稳定排序，编译器不重排用户书写次序）。

**第一段：数据前置（`stage="before"`）**

```
① DATA_PREPARE            数据回填 / 清洗（为满足随后收紧的 NOT NULL / CHECK / UNIQUE）
```

**第二段：拆依赖（纯删除，先解依赖再删对象）**

```
② DROP_FOREIGN_KEY        删外键
③ DROP_INDEX              删索引
④ DROP_UNIQUE_CONSTRAINT  删唯一约束
⑤ DROP_CHECK_CONSTRAINT   删检查约束
⑥ DROP_DEFAULT_CONSTRAINT 删默认值约束
⑦ DROP_PRIMARY_KEY        删主键
⑧ DROP_COLUMN             删列
⑨ DROP_TABLE              删表（最晚，且需 allowDestructive）
```

**第三段：结构变更（建表最先，其后才是对该表的改名 / 改列 / 加列 / 加约束）**

```
⑩ CREATE_TABLE            建表
⑪ RENAME_TABLE            重命名表
⑫ RENAME_COLUMN           重命名列
⑬ MODIFY_COLUMN           改列定义（写法见 4.5.5，仅 MySQL / MariaDB 可能需补全）
⑭ ADD_COLUMN              加列
⑮ ADD_PRIMARY_KEY         加主键
⑯ ADD_UNIQUE_CONSTRAINT   加唯一约束
⑰ ADD_CHECK_CONSTRAINT    加检查约束
⑱ ADD_DEFAULT_CONSTRAINT  加默认值约束
⑲ CREATE_INDEX            建索引
⑳ ADD_FOREIGN_KEY         加外键（依赖列、主键/唯一约束、索引三者就绪）
```

**第四段：数据后置（`stage="after"`，缺省）**

```
㉑ DATA_MIGRATION          数据迁移 / 种子 / 回填
㉒ RAW_SQL                 原生 SQL（按声明顺序）
```

**排序规则：**

| 规则          | 说明                                                                                                               |
| ------------- | ------------------------------------------------------------------------------------------------------------------ |
| 建表必须最先  | `CREATE TABLE` 先于指向同一张表的任何 `ALTER`，否则「建表 + 加索引 / 外键」必然失败                                |
| 改名先于改列  | `RENAME_TABLE` / `RENAME_COLUMN` 之后，同单元内后续操作必须引用**新名字**；引用旧名由 `check` 报错                 |
| 加外键最后    | 外键依赖被引用表的列、主键或唯一约束、以及可用的索引                                                               |
| 数据前后两段  | 收紧约束（NOT NULL / CHECK / UNIQUE）前通常要先回填，故数据操作按 `stage` 分 `before` / `after` 两段，缺省 `after` |
| 原生 SQL 最后 | `RawSql` 内部顺序由声明顺序决定，编译器不做语义分析                                                                |
| 同表连续      | 同一张表的操作连续排列，编译器用 `_processedTables` 去重，每表只生成一次表级判断                                   |
| 跨单元不排序  | 跨 ChangeUnit 只按 `(release, change_id)` 决定先后；`<depends>` 只做存在性与顺序校验，不改变物理顺序               |

**跨单元依赖的反例（`check` 报错）：**

- 本单元内 `ADD FOREIGN KEY` 引用的表，由另一个**更晚**的 changeUnit 创建。
- 本单元内 `DROP TABLE` 的表，被另一个更晚的 changeUnit 引用。

**`stage="before"` 的引用边界：** 数据前置段在所有结构变更之前执行，因此只能引用**变更前已存在**的表和列；给本次新增的列回填数据必须用 `stage="after"`（缺省），否则语句会引用不存在的列。

**删表的反向依赖（离线不可知）：**

编译器不连数据库，无法探测库中其它表对目标表的外键引用。`DROP TABLE` 前必须显式声明删除这些外键（同单元或更早单元内的 `<foreignKey action="DROP">`），否则执行期由数据库报错中止。MySQL / MariaDB 无事务 DDL，中止后可能留下半态，处置见 6.7。

### 4.3 跨库难度分级

| 难度     | 操作                                                                                           | 处理方式                                   |
| -------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------ |
| **低**   | 加列、删列、加索引、删索引、加外键、删外键、加唯一约束、加检查约束、表/列注释                  | 结构化操作，编译器自动生成                 |
| **中**   | 改列类型、改可空、改默认值、重命名列、重命名表、删主键、删唯一约束、种子数据、无符号、onUpdate | 结构化操作，方言类内处理差异               |
| **高**   | 改自增、字符集、序列、视图、部分索引、函数索引、数据迁移                                       | 按方言写 `RawSqlOperation`                 |
| **极高** | 触发器、存储过程、分区、表空间、全文索引                                                       | 按方言写 `RawSqlOperation`，或单独工具处理 |

### 4.4 破坏性分级

| 破坏性   | 操作                                                             | 策略                                 |
| -------- | ---------------------------------------------------------------- | ------------------------------------ |
| **是**   | 删表、删列、删主键、改自增、改类型（不兼容）、缩短长度、重建表   | 默认禁止或二次确认，执行前备份       |
| **可能** | 改类型、改长度、改精度、改可空、数据删除、数据清洗、数据迁移     | 检查数据兼容性，提供 `dataMigration` |
| **否**   | 加列、加索引、加外键、加约束、改注释、改默认值、重命名、种子数据 | 直接执行                             |

### 4.5 逻辑模型 XML 节点设计

#### 4.5.1 文件组织与命名

每个逻辑变更单元（ChangeUnit）对应一个 XML 文件，存放在 `migrations/` 目录。

**文件命名规则：** `{yyyyMMdd_HHmmss}_{6位随机码}_{语义描述}.xml`

示例：

```
migrations/
  00000000_000000_baseline.xml                    # 基线（存量项目接入）
  20261021_100000_a1b2c3_add_phone_to_user.xml    # 增量变更
  20261022_100000_p1x2y3_add_index.xml            # 增量变更
```

**唯一推荐生成方式：`db-chronicle new` 命令。**

```powershell
db-chronicle new --desc "add phone to nor_user"
# 输出：migrations/20261021_100000_a1b2c3_add_phone_to_nor_user.xml
```

开发者**只填描述**，文件名、ID、作者、release 全部自动生成：

- 时间戳：当前时间，到秒。
- 随机码：6 位十六进制，`RandomNumberGenerator` 生成。
- 描述：`Slugify` 后的语义描述。
- 作者：从 Git 配置、环境变量或系统用户名读取。
- release：默认取当前日期 `yyyyMMdd`，可通过 `--release` 覆盖。

**手动添加是支持的，但必须符合规范。**

| 方式                                  | 推荐度               | 唯一性保证                                           |
| ------------------------------------- | -------------------- | ---------------------------------------------------- |
| CLI `new` 命令                        | ⭐⭐⭐ 推荐             | 自动生成随机码 + `FileMode.CreateNew` + `check` 校验 |
| 手动创建                              | ⭐⭐ 允许              | 手动填随机码 + `check` 格式校验 + Git 冲突保护       |
| Core API `ChangeUnitFactory.Create()` | ⭐⭐⭐ 推荐（编程场景） | 同 CLI                                               |

**手动添加的合法场景：**

- 从其他系统迁移存量变更。
- 批量导入（脚本直接写文件）。
- 离线环境（无 CLI）。
- 脚本自动化生成。
- 紧急修正（生产热修复）。
- 代码审查后微调。

**手动添加的约束：**

1. 文件名必须匹配正则：`^(\d{8}_\d{6})_([0-9a-f]{6})_([a-z0-9_]+)\.xml$`
2. 随机码手动填 6 位十六进制（如 `a1b2c3`）。
3. `id` 属性必须与文件名前缀一致。
4. 时间戳必须真实（误差不超过 24 小时）。
5. 内容必须符合逻辑模型格式。
6. 必须通过 `db-chronicle check` 校验。

**四层防护：**

| 层级       | 机制                                                | 说明                           |
| ---------- | --------------------------------------------------- | ------------------------------ |
| 生成层     | 时间戳 + 6 位随机码 + `FileMode.CreateNew`          | 文件已存在时抛异常，绝不覆盖   |
| 版本控制层 | Git 同名冲突保护 + 禁止 force push 到 `migrations/` | 同名文件合并时冲突，不静默覆盖 |
| 执行层     | checksum 强校验                                     | 已应用文件被修改时拒绝执行     |
| CI 层      | `check` + `verify` 门禁                             | 冲突或 checksum 不一致阻止合并 |

**`new` 命令生成逻辑：**

```csharp
public static string GenerateUniqueFileName(string migrationsDir, string desc)
{
    for (int i = 0; i < 10; i++)
    {
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{GenerateRandomCode(6)}_{Slugify(desc)}.xml";
        var path = Path.Combine(migrationsDir, fileName);

        if (File.Exists(path)) continue;

        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew);  // 绝不覆盖
            return fileName;
        }
        catch (IOException) when (File.Exists(path))
        {
            continue;  // 竞态，重试
        }
    }
    throw new InvalidOperationException("连续 10 次生成文件名均失败，请检查 migrations 目录");
}
```

- 基线文件固定命名 `00000000_000000_baseline.xml`，`action` 全部为 `CREATE`。
- CLI `new` 命令自动生成文件名，开发者只填语义描述。
- **已存在的文件绝不覆盖，生成失败时重试，连续 10 次失败报错。**

**强制使用 CLI 的机制：**

| 机制                 | 位置         | 作用                            |
| -------------------- | ------------ | ------------------------------- |
| 文件名正则校验       | `check` 命令 | CI 门禁，不符规范的文件阻止合并 |
| Pre-commit 钩子      | 本地         | 提交前拦截手动命名的文件        |
| IDE 模板             | 开发环境     | 模板明确提示"请勿手动命名"      |
| Git 服务器保护       | 远程         | 分支保护规则，CI 状态检查       |
| `FileMode.CreateNew` | `new` 命令   | 文件已存在时抛异常，绝不覆盖    |

#### 4.5.2 编程式生成 API

Core 库公开 `ChangeUnitFactory` API，供宿主应用编程式生成变更单元：

```csharp
using DbChronicle;

var path = ChangeUnitFactory.Create(
    outputDir: "./migrations",
    description: "add phone to nor_user",
    release: "20261022",
    author: "zhangsan");
// 返回：./migrations/20261022_100000_a1b2c3_add_phone_to_nor_user.xml
```

API 与 CLI `new` 命令共享同一套命名逻辑，保证文件名规范一致。

#### 4.5.3 根节点：`<changeUnit>`

```xml
<?xml version="1.0" encoding="utf-8"?>
<changeUnit id="20261022_100000_a1b2c3" author="zhangsan" release="20261022">
  <description>用户表增加手机号，调整邮箱长度</description>
  <depends on="20261021_100000_x9y8z7"/>
  <table ... />
</changeUnit>
```

| 属性/节点       | 必填   | 说明                              |
| --------------- | ------ | --------------------------------- |
| `id`            | 是     | 全局唯一，与文件名前缀一致        |
| `author`        | 是     | 变更作者                          |
| **`release`**   | **是** | **所属发布版本，格式 `yyyyMMdd`** |
| `<description>` | 是     | 人类可读的变更说明                |
| `<depends>`     | 否     | 显式声明依赖的变更单元 id         |

**`release` 约束：**

| 规则                                          | 校验     |
| --------------------------------------------- | -------- |
| `release` 必填                                | 解析失败 |
| `release` 格式 `yyyyMMdd`                     | 解析失败 |
| `release` 不得晚于当前日期 + 1 天             | 解析失败 |
| 基线 `release` 固定 `00000000`                | 解析失败 |
| `release` 与 `change_id` 日期不一致时输出警告 | 警告     |

#### 4.5.4 表节点：`<table>`

```xml
<table name="nor_user" action="ALTER" rebuild="false" comment="用户表">
  <column .../>
  <index .../>
  <foreignKey .../>
  <primaryKey .../>
  <uniqueConstraint .../>
  <checkConstraint .../>
  <defaultConstraint .../>
  <dataMigration .../>
  <seed .../>
</table>
```

| 属性      | 取值                                                           | 说明                                             |
| --------- | -------------------------------------------------------------- | ------------------------------------------------ |
| `name`    | 表名                                                           | 必填                                             |
| `action`  | `CREATE` / `ALTER` / `DROP` / `RENAME` / `COMMENT` / `REBUILD` | 表级操作                                         |
| `rebuild` | `true` / `false`                                               | 是否走重建策略                                   |
| `comment` | 表注释                                                         | 可选                                             |
| `newName` | 新表名                                                         | 仅 `RENAME` 时必填                               |
| `tables`  | 逗号分隔的表名清单                                             | 仅 `name="*"` 时有意义；没有累积模型时的显式清单 |

**通配符表名（`name="*"`）：** 表示"库中所有表"，**必须连库执行**（在 `apply` 阶段展开），适合"给所有表统一加审计字段"：

```xml
<table name="*" action="ALTER">
  <column name="created_by" action="ADD" type="String" length="32" nullable="true" comment="创建人"/>
</table>
```

| 阶段              | 行为                                                                                                                            |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `compile`（离线） | 无法确定表清单 → 语句按**占位符**输出（如 `ALTER TABLE "*" ADD COLUMN ...`），并告警 **W022**；产物只用于预览，**不能直接执行** |
| `apply`（连库）   | 连接后查询 `information_schema` / `sqlite_master`，取全部业务表，把占位符替换成逐表语句                                         |
| 执行顺序          | 通配符语句**优先执行**，排在该 changeUnit 其他语句之前；其余语句随后按 4.2 的顺序执行                                           |
| 表清单刷新        | 每个 changeUnit 编译前重新查询一次，因此"同批次先建表、后通配加字段"也能覆盖到新表                                              |
| 显式清单          | `tables="t1,t2"` 覆盖库表清单（用于只改指定表），此时离线也能展开                                                               |
| 展开提示          | 输出 **W021**，打印实际作用的表清单                                                                                             |
| 不支持            | `CREATE` / `DROP` / `RENAME` / `REBUILD` / `<rawSql>` / `<dataMigration>` → 报 **E041**                                         |
| 表已有该列        | 执行期报"列已存在"；需要逐表判断时请拆成显式表节点                                                                              |

> 风险边界：表清单在 `apply` 运行时确定，`output/{dialect}.sql` 里的 `"*"` 只是占位符，不能直接执行；实际执行什么以 SQL 快照与审计日志为准（记录的是展开后的真实语句）。库中**所有业务表**都会被作用（含历史遗留表），需要限定范围时务必用 `tables="..."`。

#### 4.5.5 列节点：`<column>`

```xml
<column name="phone" action="ADD"
        type="String" length="20" nullable="true"
        defaultValue="null" comment="手机号"
        primaryKey="false" autoIncrement="false" unique="false"
        unsigned="false" onUpdate="" foreignKey="" renamedFrom="" after=""/>
```

| 属性                  | 取值                                                         | 说明                                                                                                                                                                             |
| --------------------- | ------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `name`                | 列名                                                         | 必填                                                                                                                                                                             |
| `action`              | `ADD` / `MODIFY` / `DROP` / `RENAME` / `COMMENT` / `REORDER` | 列级操作                                                                                                                                                                         |
| `type`                | **逻辑类型**                                                 | Boolean / Int8 / Int16 / Int32 / Int64 / Float32 / Float64 / Decimal / FixedString / String / Text / Json / Xml / DateTime / DateTimeOffset / Date / Time / Binary / Blob / Uuid |
| `length`              | 整数                                                         | `FixedString` / `String` / `Binary` 必填；`Blob` 忽略                                                                                                                            |
| `precision` / `scale` | 整数                                                         | Decimal 精度和小数位                                                                                                                                                             |
| `nullable`            | `true` / `false`                                             | 是否可空                                                                                                                                                                         |
| `defaultValue`        | 字符串                                                       | 默认值；`null` = 显式 NULL；`''` = 空字符串字面量；`none` = 无默认值；**省略的含义由 `options.incrementalModify` 决定**（见下）                                                  |
| `comment`             | 字符串                                                       | 列注释                                                                                                                                                                           |
| `primaryKey`          | `true` / `false`                                             | 是否主键                                                                                                                                                                         |
| `autoIncrement`       | `true` / `false`                                             | 是否自增                                                                                                                                                                         |
| `unique`              | `true` / `false`                                             | 是否唯一                                                                                                                                                                         |
| `unsigned`            | `true` / `false`                                             | 是否无符号，仅 MySQL/MariaDB 生效                                                                                                                                                |
| `onUpdate`            | 字符串                                                       | 更新时刷新值，仅 MySQL/MariaDB 生效                                                                                                                                              |
| `foreignKey`          | `表名.列名`                                                  | 外键引用                                                                                                                                                                         |
| `renamedFrom`         | 字符串                                                       | 重命名前的列名，仅 `RENAME` 时必填                                                                                                                                               |
| `after`               | 字符串                                                       | 列顺序调整，仅 `REORDER` 时使用；取值 `列名` 或 `FIRST`，仅 MySQL / MariaDB 生效，其他方言忽略并警告（W010）                                                                     |

**关键约束：`type` 必须是逻辑类型，绝不出现 `VARCHAR(64)`、`TINYINT(1)` 等物理类型。**

**`action` 的语义边界：**

| action    | 语义                                              | 必须声明                                         | 方言差异                                                                            |
| --------- | ------------------------------------------------- | ------------------------------------------------ | ----------------------------------------------------------------------------------- |
| `ADD`     | 新增列                                            | `type` + 长度/精度                               | SQLite 不允许带 `primaryKey` / `unique`，需走 rebuild                               |
| `MODIFY`  | 改列；**语义由 `options.incrementalModify` 决定** | full：`type` + `nullable`；incremental：至少一项 | MySQL 一条 `MODIFY COLUMN`；PG 拆多条；SQL Server 先删默认值约束；SQLite 走 rebuild |
| `DROP`    | 删列                                              | —                                                | SQLite 3.35.0+ 原生，更低版本 rebuild；需 `allowDestructive`                        |
| `RENAME`  | 改列名                                            | `renamedFrom`                                    | MySQL 5.7 需 `CHANGE` 并重述类型；SQL Server 用 `sp_rename`；SQLite 3.25.0+ 原生    |
| `COMMENT` | 只改注释，不动类型/可空/默认值                    | `comment`                                        | PG 独立 `COMMENT ON`；SQL Server / SQLite 跳过并警告（W007）                        |
| `REORDER` | 调整列顺序                                        | `after`（列名或 `FIRST`）                        | 仅 MySQL / MariaDB 生效，其他方言忽略并警告（W010）                                 |

**`MODIFY` 的两档语义：**

| 模式                    | 配置                       | XML 写法         | 未写的属性                              | 风险                                              |
| ----------------------- | -------------------------- | ---------------- | --------------------------------------- | ------------------------------------------------- |
| **incremental**（默认） | `incrementalModify: true`  | 只写变化项       | **保持不变**（由累积模型补全，见 5.10） | 变更链缺失 / 表不在链中 → 解析失败，绝不猜        |
| **full**                | `incrementalModify: false` | 声明目标完整状态 | 视为"目标没有"                          | 漏写 `defaultValue` 会清空默认值，`check` 给 W008 |

**incremental 模式的补全规则：**

1. 取该 changeUnit **之前**的结构状态（离线累积模型，见 5.10）中「该表 · 该列」的定义作为基底。
2. 用 XML 中**显式出现**的属性覆盖基底，未出现的属性保持原值。
3. 补全后的完整定义再交给方言生成 DDL：MySQL 仍是一条完整的 `MODIFY COLUMN`（引擎要求），PG / SQL Server 只生成实际变化的那几条语句。
4. 只对 `MODIFY` 生效；`ADD` 没有历史可补，`RENAME` / `DROP` 不涉及属性。

**三态表达（同一条 XML 在两种模式下的差异）：**

| 写法                  | `incrementalModify: true`（默认） | `incrementalModify: false` |
| --------------------- | --------------------------------- | -------------------------- |
| 不写 `nullable`       | 保持不变                          | 解析失败                   |
| 不写 `defaultValue`   | 保持不变                          | 目标无默认值（W008）       |
| `defaultValue="none"` | 删除默认值                        | 删除默认值                 |
| `defaultValue="''"`   | 设为空字符串                      | 设为空字符串               |
| 不写 `comment`        | 保持不变                          | 目标无注释                 |
| `comment=""`          | 清空注释                          | 清空注释                   |

**为什么默认是 incremental：** `MODIFY` 的直觉语义就是"只写变化项"，用户不该为了改一个长度去抄一遍整列定义。历史文件不受影响：按全量写的文件，显式属性会完整覆盖基底，结果与原语义一致；只有"故意省略某属性以清空它"这种写法会被补回原值，而这类意图现在有显式表达（`defaultValue="none"` / `comment=""`），确需旧行为时把开关置 `false`。

**各方言对增量模式的实际需要：**

| 方言            | 不补全会怎样                                                          | 结论                            |
| --------------- | --------------------------------------------------------------------- | ------------------------------- |
| MySQL / MariaDB | `MODIFY COLUMN` 用给定定义替换整列，未写的属性被清空                  | **必须补全**                    |
| PostgreSQL      | `ALTER COLUMN` 分条目，未写的属性不生成语句 = 保持不变                | 不需要补全，补全也不改变输出    |
| SQL Server      | 改类型只需 `ALTER COLUMN {type} {null}`，默认值约束按可预测的名字处理 | 不需要补全                      |
| SQLite          | rebuild 需要完整表结构，未写的属性在重建后丢失                        | **需要补全**（同 MySQL 的处理） |

即补全逻辑（累积模型）与方言无关，放在 Core；方言只决定"是否必须补全"与"是否告警"。PG / SQL Server 上该开关打开也不改变 SQL 输出。

**实现要点：** Parser 必须保留每个属性"是否显式出现"（has-flag），不能用"值等于默认值"判断——否则 `nullable="false"` 与缺省无法区分，补全会失效。

**incremental 模式的边界（补不到就退化为当前声明，逐列告警）：**

| 情况                                         | 处置                                                                    |
| -------------------------------------------- | ----------------------------------------------------------------------- |
| 基线文件缺失                                 | **退化**：所有 `MODIFY` 按 XML 声明处理，输出 W014                      |
| 表不在累积模型中（基线未包含、外部系统建的） | 该表的 `MODIFY` 逐列退化 + W014                                         |
| 列不在累积模型中（历史链从未 `ADD` 过该列）  | 该列退化 + W014                                                         |
| 该列历史上有 `RENAME`                        | 按最后一次改名后的名字解析（能补全则正常补全）                          |
| 该列曾被 `DROP` 后重新 `ADD`                 | 用最后一次 `ADD` 的定义                                                 |
| 累积模型与库实际不一致（库被手工改过）       | `compile` 无法感知；由 `apply` 的 introspect 复核（v1.1）报告"模型漂移" |
| 历史文件被修改                               | 模型基于文件当前内容，与库的一致性由 `verify` 的 checksum 校验保证      |

**退化的准确含义：**

| 项               | 说明                                                                                              |
| ---------------- | ------------------------------------------------------------------------------------------------- |
| 触发             | 某个 `MODIFY` 在累积模型中找不到基底（缺基线 / 表不在链 / 列不在链）                              |
| 行为             | 该列按 XML 中**已写**的属性生成，**未写的属性不会从历史继承**（等价于这一列走了 full 模式）       |
| 粒度             | **按列退化**：同一文件里能补全的列仍正常补全                                                      |
| 告警             | W014，每列一条，含表名、列名、缺失原因与建议（补基线 / 写全该列 / 置 `incrementalModify: false`） |
| 不阻断           | 编译与执行继续——用户声明即目标，缺历史不代表无法生成 SQL                                          |
| 与"链断裂"的区别 | `depends` 指向不存在的单元属于文件完整性问题，`check` 仍报错（4.6.4），不做退化                   |

**为什么允许退化：** 未接基线的新项目、临时表、外部系统建的表都需要能编译。只要把"未写属性不会继承"讲清楚（W014 + SQL 注释头标注），风险可控，比强迫写全或直接罢工更实用。

#### 4.5.6 主键 / 索引 / 外键 / 约束节点

```xml
<!-- 主键 -->
<primaryKey action="ADD" name="pk_user" columns="id"/>

<!-- 索引 -->
<index name="idx_user_phone" action="ADD" unique="true" type="NORMAL">
  <columnRef name="phone"/>
</index>

<!-- 外键 -->
<foreignKey name="fk_user_dept" action="ADD"
            columns="dept_id" refTable="nor_dept" refColumns="id"
            onDelete="RESTRICT" onUpdate="CASCADE"/>

<!-- 唯一约束 -->
<uniqueConstraint name="uk_user_email" action="ADD" columns="email"/>

<!-- 检查约束 -->
<checkConstraint name="ck_user_age" action="ADD" expression="age &gt;= 0 AND age &lt;= 150"/>

<!-- 默认值约束（SQL Server 用） -->
<defaultConstraint name="df_user_status" action="ADD" column="status" value="0"/>
```

#### 4.5.7 数据迁移 / 种子数据节点

```xml
<!-- 数据迁移（按方言） -->
<dataMigration>
  <sql dialect="mysql">
    INSERT INTO nor_errorcode (Id, Message) VALUES (8, '余量不足:{0}')
    ON DUPLICATE KEY UPDATE Message = VALUES(Message);
  </sql>
  <sql dialect="postgresql">
    INSERT INTO nor_errorcode (id, message) VALUES (8, '余量不足:{0}')
    ON CONFLICT (id) DO UPDATE SET message = EXCLUDED.message;
  </sql>
</dataMigration>

<!-- 种子数据（结构化） -->
<seed table="nor_errorcode" conflict="UPDATE"
      columns="Id,ModuleType,Language,ErrorCode,Message">
  <row values="8,10,0,'001000','余量不足:{0}'"/>
  <row values="9,10,0,'001001','{0}'"/>
</seed>
```

**`stage` 属性（数据操作的执行时机）：**

| 取值            | 执行时机                       | 典型用途                         |
| --------------- | ------------------------------ | -------------------------------- |
| `before`        | 所有结构变更之前（4.2 第一段） | 回填空值、清洗脏数据、拆分旧列   |
| `after`（缺省） | 结构变更之后（4.2 第四段）     | 数据搬迁、种子数据、按新结构更新 |

适用节点：`<dataMigration>`、`<seed>`、`<update>`、`<delete>`、`<clean>`、`<backfill>`。同一 changeUnit 内允许多个 `stage="before"` 节点，按声明顺序执行。

典型场景：`ALTER COLUMN ... SET NOT NULL` 之前必须把 NULL 回填，否则语句顺序颠倒、执行期报错——这就要写 `<dataMigration stage="before">`。

#### 4.5.8 视图 / 序列 / 触发器 / 存储过程

用 `<rawSql>` 节点按方言写完整 SQL。

#### 4.5.9 完整示例

```xml
<?xml version="1.0" encoding="utf-8"?>
<changeUnit id="20261022_100000_a1b2c3" author="zhangsan" release="20261022">
  <description>用户表增加手机号，调整邮箱长度，新增索引和外键</description>

  <table name="nor_user" action="ALTER" comment="用户表">

    <column name="phone" action="ADD"
            type="String" length="20" nullable="true"
            comment="手机号" defaultValue="null"/>

    <column name="email" action="MODIFY"
            type="String" length="128" nullable="false"/>

    <column name="old_flag" action="DROP"/>

    <column name="user_name" action="RENAME" renamedFrom="name"/>

    <column name="view_count" action="ADD"
            type="Int32" unsigned="true" nullable="false" defaultValue="0"/>

    <column name="updated_at" action="ADD"
            type="DateTime" nullable="true"
            defaultValue="CURRENT_TIMESTAMP" onUpdate="CURRENT_TIMESTAMP"/>

    <index name="idx_user_phone" action="ADD" unique="true">
      <columnRef name="phone"/>
    </index>

    <foreignKey name="fk_user_dept" action="ADD"
                columns="dept_id" refTable="nor_dept" refColumns="id"
                onDelete="RESTRICT" onUpdate="CASCADE"/>

    <uniqueConstraint name="uk_user_email" action="ADD" columns="email"/>

    <checkConstraint name="ck_user_age" action="ADD"
                     expression="age &gt;= 0 AND age &lt;= 150"/>

    <dataMigration>
      <sql dialect="mysql">UPDATE nor_user SET phone = '' WHERE phone IS NULL;</sql>
      <sql dialect="postgresql">UPDATE nor_user SET phone = '' WHERE phone IS NULL;</sql>
    </dataMigration>
  </table>

  <table name="nor_errorcode" action="ALTER">
    <seed table="nor_errorcode" conflict="UPDATE"
          columns="Id,ModuleType,Language,ErrorCode,Message">
      <row values="8,10,0,'001000','余量不足:{0}'"/>
      <row values="9,10,0,'001001','{0}'"/>
    </seed>
  </table>

  <view name="v_user_summary" action="CREATE">
    <sql dialect="mysql">
      CREATE OR REPLACE VIEW v_user_summary AS
      SELECT u.id, u.name, COUNT(o.id) AS order_count
      FROM nor_user u LEFT JOIN nor_order o ON o.user_id = u.id
      GROUP BY u.id, u.name;
    </sql>
    <sql dialect="postgresql">
      CREATE OR REPLACE VIEW v_user_summary AS
      SELECT u.id, u.name, COUNT(o.id) AS order_count
      FROM nor_user u LEFT JOIN nor_order o ON o.user_id = u.id
      GROUP BY u.id, u.name;
    </sql>
  </view>
</changeUnit>
```

#### 4.5.10 校验规则（解析时强制）

| 规则                                                                     | 错误处理                            |
| ------------------------------------------------------------------------ | ----------------------------------- |
| `id` 必须与文件名前缀一致                                                | 解析失败                            |
| `release` 必填，格式 `yyyyMMdd`，不得晚于当前 + 1 天                     | 解析失败                            |
| `type` 必须在 `LogicalType` 枚举内                                       | 解析失败                            |
| `FixedString` / `String` / `Binary` 必须显式提供 `length`                | 解析失败                            |
| `Blob` 的 `length` 属性被忽略，输出警告                                  | 警告                                |
| `RENAME` 必须提供 `renamedFrom`                                          | 解析失败                            |
| `RENAME` 之后，同单元内仍引用旧列名 / 旧表名                             | `check` 命令报错                    |
| `MODIFY` 在 full 模式下必须声明 `type` 与 `nullable`（见 4.5.5）         | 解析失败                            |
| `MODIFY` 在 incremental 模式（默认）下至少声明一项                       | 解析失败                            |
| 增量补全基底缺失（无基线 / 表列不在累积模型）                            | 警告（W014）并按已写内容退化        |
| `MODIFY` 在 full 模式下未声明 `defaultValue`（= 目标无默认值）           | 警告（W008）                        |
| `foreignKey` 格式必须为 `表名.列名`                                      | 解析失败                            |
| 同一 `changeUnit` 内，同一表同一列不得重复出现                           | 解析失败                            |
| `RawSql` 的 `dialect` 必须与当前方言匹配                                 | 编译时跳过不匹配项                  |
| `DROP_TABLE` / `DROP_COLUMN` 必须有显式 `allowDestructive` 配置          | 解析失败                            |
| `unsigned=true` 仅对整数类型有效                                         | 解析失败                            |
| `onUpdate` 仅对 `DateTime` / `DateTimeOffset` 类型有效                   | 解析失败                            |
| 数据操作节点的 `stage` 仅允许 `before` / `after`                         | 解析失败                            |
| `DateTimeOffset` 映射到 MySQL/MariaDB 时输出警告                         | 警告（W003）                        |
| 文件含 CRLF 或 UTF-8 BOM（会导致 checksum 漂移）                         | 警告                                |
| 文件名必须匹配 `{yyyyMMdd_HHmmss}_{6位随机码}_{描述}.xml`                | `check` 命令报错                    |
| 跨变更单元检测：重复 ADD / MODIFY 冲突 / DROP 不存在列 / RENAME 目标冲突 | `check` 命令报错                    |
| 补丁文件必须用 `depends` 声明依赖的原变更单元                            | `check` 命令报错                    |
| 已应用的 `change_id`，checksum 必须与 `version_log` 一致                 | `verify` 命令报错，`apply` 拒绝执行 |

#### 4.5.11 MVP 范围建议

**第一版（MVP）：**

- 表：`CREATE`、`ALTER`、`DROP`
- 列：`ADD`、`MODIFY`、`DROP`、`RENAME`
- 索引：`ADD`、`DROP`
- 外键：`ADD`、`DROP`
- 主键：`ADD`、`DROP`
- 唯一约束：`ADD`、`DROP`
- 数据迁移：`RawSqlOperation`

**第二版：**

- 表：`RENAME`、`COMMENT`、`REBUILD`
- 列：`COMMENT`、`REORDER`、`unsigned`、`onUpdate`
- 索引：`RENAME`
- 检查约束：`ADD`、`DROP`
- 默认值约束：`ADD`、`DROP`
- 种子数据：`SEED`

**第三版（按需）：**

- 视图、序列
- 触发器、存储过程
- 函数索引、部分索引、全文索引
- 分区、字符集、表空间、权限

### 4.6 文件唯一性与补丁规范

#### 4.6.1 文件唯一性保障

变更单元文件的生命周期：

```
生成 → 提交 → 审查 → 合并 → 应用 → 冻结
                              │
                              ▼
                        写入 version_log
                        文件不可再修改
```

**四层防护：**

1. **生成层**：`new` 命令使用 `FileMode.CreateNew`，文件已存在时抛异常，绝不覆盖。
2. **版本控制层**：Git 对同名文件有冲突保护，禁止 `force push` 到 `migrations/`。
3. **执行层**：已应用的 `change_id`，checksum 不一致时拒绝执行。
4. **CI 层**：`check` 检测语义冲突，`verify` 检测 checksum，任何冲突阻止合并。

#### 4.6.2 补丁规范

**核心原则：已应用的变更单元文件不可修改，只能新增。**

| 场景                                       | 处理                               |
| ------------------------------------------ | ---------------------------------- |
| 原始变更单元尚未在任何环境应用             | 可直接修改文件                     |
| 原始变更单元已应用，发现错误               | **禁止修改文件**，新增补丁变更单元 |
| 补丁必须用 `depends` 声明依赖的原变更单元  | 强制                               |
| 补丁文件命名建议带 `fix_` 或 `patch_` 前缀 | 推荐                               |
| 补丁使用新的 `release`（如跨天补丁）       | 推荐                               |

**补丁示例：**

原文件保持不变：

```xml
<!-- 20261021_100000_a1b2_add_boxQcUsed.xml（不可修改） -->
<changeUnit id="20261021_100000_a1b2" author="zhangsan" release="20261021">
  <table name="nor_autoqc" action="ALTER">
    <column name="boxQcUsed" action="ADD" type="Boolean" nullable="true"/>
  </table>
</changeUnit>
```

新增补丁文件：

```xml
<!-- 20261023_100000_p1x2y3_fix_boxQcUsed_type.xml（新增） -->
<changeUnit id="20261023_100000_p1x2y3" author="zhangsan" release="20261023">
  <description>补丁：修正 boxQcUsed 类型</description>
  <depends on="20261021_100000_a1b2"/>
  <table name="nor_autoqc" action="ALTER">
    <column name="boxQcUsed" action="MODIFY" type="String" length="20"/>
  </table>
</changeUnit>
```

#### 4.6.3 checksum 校验

**`checksum = SHA-256(XML 文件原始字节)`。**

`apply` 执行时的校验逻辑：

| 情况                                | 处理                                                  |
| ----------------------------------- | ----------------------------------------------------- |
| `change_id` 未应用                  | 正常执行，执行成功后写入 `version_log`（含 checksum） |
| `change_id` 已应用，checksum 一致   | 跳过（幂等）                                          |
| `change_id` 已应用，checksum 不一致 | **报错拒绝执行**                                      |
| `change_id` 已应用，但文件不存在    | 报错"已应用的变更单元文件丢失"                        |

**错误信息必须包含：**

- 已应用的 checksum
- 当前文件的 checksum
- 补丁规范提示（新增文件 + `depends`）

**字节口径与工程约定：**

`checksum` 对文件**原始字节**计算，不做任何规范化——BOM、换行符、缩进、属性顺序、尾随空白全部参与哈希。因此"内容没改、只是换行或编码变了"同样会让 checksum 变化。

| 约定                                          | 原因                             | 落地方式                                                            |
| --------------------------------------------- | -------------------------------- | ------------------------------------------------------------------- |
| `migrations/*.xml` 统一 UTF-8 无 BOM、LF 换行 | BOM 与 CRLF 都会改变字节         | `new` 命令按此格式生成；编辑器保存策略统一                          |
| Git 不做换行 / 编码转换                       | 避免不同平台 checkout 后字节变化 | `.gitattributes` 写 `migrations/*.xml -text`                        |
| 禁止对 `migrations/` 执行格式化               | 属性重排、缩进调整都会改变字节   | `.editorconfig` 固定缩进与 `insert_final_newline`，团队约定显式禁止 |
| `check` 检测 CRLF / BOM                       | 提前暴露"将导致 checksum 漂移"   | 输出警告，不阻止合并                                                |

**`verify` 的二次判定（区分"实质修改"与"字节漂移"）：**

| 判定                  | 条件                                  | 结论与处置                                                                               |
| --------------------- | ------------------------------------- | ---------------------------------------------------------------------------------------- |
| 内容被修改            | 规范化（去 BOM、CRLF→LF）后哈希仍不同 | 报错：`apply` 拒绝执行，按补丁规范新增变更单元                                           |
| 疑似仅换行 / 编码变动 | 规范化后哈希相同                      | 报告"疑似仅换行或编码变动"，给出两条处置：还原文件字节；或确认无实质修改后手工更新版本表 |
| 文件丢失              | 版本表有记录、文件不存在              | 报错"已应用的变更单元文件丢失"                                                           |

**不提供自动更新 checksum 的命令**：审计的真实性依赖"记录的哈希 = 当时执行的字节"，自动更新会让"内容被改"与"换行变化"无法区分。

#### 4.6.4 语义冲突检测

**`check` 命令检测跨变更单元的语义冲突：**

| 冲突类型             | 示例                                     | 处理 |
| -------------------- | ---------------------------------------- | ---- |
| 重复 ADD             | A、B 都写 `add phone`                    | 报错 |
| MODIFY 目标冲突      | A 改 `email` 为 128，B 改 `email` 为 256 | 报错 |
| ADD 与 DROP 顺序矛盾 | A 写 `add phone`，B 写 `drop phone`      | 报错 |
| 补丁缺少 depends     | 补丁修改某列，但未声明依赖原变更         | 报错 |
| 文件名格式不符       | `add_phone.xml`（无时间戳/随机码）       | 报错 |

**CI 门禁：任何语义冲突阻止合并。**

#### 4.6.5 文件头注释

`new` 命令生成的 XML 文件，头部自动加注释：

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  本变更单元一旦应用（写入 version_log），文件即不可修改。
  修正错误请新增变更单元文件，并用 <depends on="本文件 id"/> 声明依赖。
  详见文档 4.6 节「文件唯一性与补丁规范」。
-->
<changeUnit id="20261022_100000_a1b2c3" author="zhangsan" release="20261022">
  ...
</changeUnit>
```

基线文件头部加更强的提示：

```xml
<?xml version="1.0" encoding="utf-8"?>
<!--
  【基线文件】已冻结，禁止修改。
  此文件由 Scanner 生成或手工编写，冻结后不可变更。
  后续所有结构变更请新增增量变更单元文件。
-->
<changeUnit id="00000000_000000_baseline" author="scanner" release="00000000">
  ...
</changeUnit>
```

---

## 五、逻辑模型转为 SQL

本章是 DbChronicle 的核心引擎。输入是解析后的逻辑模型（`ChangeUnit` 对象），输出是目标方言的 SQL 字符串。

一期覆盖五种数据库：MySQL、MariaDB、PostgreSQL、SQL Server、SQLite。

### 5.1 转换总览

**正向编译（Core 职责，一期实现）：**

```
ChangeUnit（逻辑模型）
      │
      ▼
① Parser：XML → List<SchemaOperation>
      │
      ▼
② 类型映射：逻辑类型 → 物理类型（TypeMapper）
      │
      ▼
③ 操作转换：Operation → DDL 语法（ISqlDialect）
      │
      ▼
④ 属性转换：NULL / DEFAULT / COMMENT / AUTO_INCREMENT / 时区 ...
      │
      ▼
⑤ SQL 拼接：标识符引用、转义、分隔
      │
      ▼
⑥ 语句拆分：按方言切分为执行单元（见 6.5）
      │
      ▼
CompileResult（SQL 文本 + Statements + Diagnostics）
```

**反向工程（Scanner 模块职责，一期可选）：**

```
C# 实体类
      │
      ▼
① 元数据提取：反射 / Roslyn
      │
      ▼
② 类型映射：C# 类型 → 逻辑类型
      │
      ▼
③ 实体识别、列提取、约束推断
      │
      ▼
种子逻辑模型 XML（草案）
```

**两个方向共用同一套逻辑类型定义，但映射表不同：**

| 映射方向            | 依赖表            | 服务模块            |
| ------------------- | ----------------- | ------------------- |
| 逻辑类型 → 物理类型 | 方言类型映射 YAML | Core（正向编译）    |
| C# 类型 → 逻辑类型  | 语言类型映射表    | Scanner（反向工程） |

**Core 的正向编译不依赖语言类型映射，这是职责边界。**

### 5.2 逻辑模型中的数据类型

#### 5.2.1 类型命名原则

**逻辑模型中的 `type` 使用语言中立的逻辑类型名，不绑定任何编程语言。**

**命名规范：**

| 分类       | 命名风格   | 示例                                         |
| ---------- | ---------- | -------------------------------------------- |
| 数值       | 位数明确   | `Int8`、`Int16`、`Int32`、`Int64`            |
| 浮点       | 位数明确   | `Float32`、`Float64`                         |
| 布尔       | 语义名     | `Boolean`                                    |
| 字符串     | 语义名     | `FixedString`、`String`、`Text`、`Json`      |
| 结构化文本 | 语义名     | `Xml`                                        |
| 时间       | 语义名     | `DateTime`、`DateTimeOffset`、`Date`、`Time` |
| 二进制     | 语义名     | `Binary`、`Blob`                             |
| 标识       | 通用标准名 | `Uuid`                                       |

**关键决策：用位数命名数值类型，不用语言类型名。** `Int32` 语义明确，映射到 MySQL `INT`、Java `int`、Go `int32`、TypeScript `number`。

#### 5.2.2 逻辑类型全集

```csharp
public enum LogicalType
{
    Boolean,
    Int8, Int16, Int32, Int64,
    Float32, Float64,
    Decimal,
    FixedString, String, Text, Json,
    Xml,
    DateTime, DateTimeOffset, Date, Time,
    Binary, Blob,
    Uuid
}
```

**共 20 种逻辑类型。**

**解析器将字符串转为 `LogicalType` 枚举：**

```csharp
public static LogicalType ParseType(string typeStr) => typeStr switch
{
    "Boolean"        => LogicalType.Boolean,
    "Int8"           => LogicalType.Int8,
    "Int16"          => LogicalType.Int16,
    "Int32"          => LogicalType.Int32,
    "Int64"          => LogicalType.Int64,
    "Float32"        => LogicalType.Float32,
    "Float64"        => LogicalType.Float64,
    "Decimal"        => LogicalType.Decimal,
    "FixedString"    => LogicalType.FixedString,
    "String"         => LogicalType.String,
    "Text"           => LogicalType.Text,
    "Json"           => LogicalType.Json,
    "Xml"            => LogicalType.Xml,
    "DateTime"       => LogicalType.DateTime,
    "DateTimeOffset" => LogicalType.DateTimeOffset,
    "Date"           => LogicalType.Date,
    "Time"           => LogicalType.Time,
    "Binary"         => LogicalType.Binary,
    "Blob"           => LogicalType.Blob,
    "Uuid"           => LogicalType.Uuid,
    _ => throw new FormatException($"未知逻辑类型：{typeStr}")
};
```

#### 5.2.3 逻辑类型 ↔ 各语言原生类型映射

**本映射表服务于工具集的反向工程与代码生成模块，Core 的正向编译不依赖此表。**

| 逻辑类型         | C#                        | Java                  | Go                | TypeScript   |
| ---------------- | ------------------------- | --------------------- | ----------------- | ------------ |
| `Boolean`        | `bool`                    | `boolean`             | `bool`            | `boolean`    |
| `Int8`           | `sbyte`                   | `byte`                | `int8`            | `number`     |
| `Int16`          | `short`                   | `short`               | `int16`           | `number`     |
| `Int32`          | `int`                     | `int`                 | `int32`           | `number`     |
| `Int64`          | `long`                    | `long`                | `int64`           | `bigint`     |
| `Float32`        | `float`                   | `float`               | `float32`         | `number`     |
| `Float64`        | `double`                  | `double`              | `float64`         | `number`     |
| `Decimal`        | `decimal`                 | `BigDecimal`          | `decimal.Decimal` | `string`     |
| `FixedString`    | `string`                  | `String`              | `string`          | `string`     |
| `String`         | `string`                  | `String`              | `string`          | `string`     |
| `Text`           | `string`                  | `String`              | `string`          | `string`     |
| `Json`           | `string` / `JsonDocument` | `String` / `JsonNode` | `json.RawMessage` | `unknown`    |
| `Xml`            | `string` / `XDocument`    | `String` / `Document` | `string`          | `string`     |
| `DateTime`       | `DateTime`                | `LocalDateTime`       | `time.Time`       | `Date`       |
| `DateTimeOffset` | `DateTimeOffset`          | `OffsetDateTime`      | `time.Time`       | `Date`       |
| `Date`           | `DateOnly`                | `LocalDate`           | `time.Time`       | `Date`       |
| `Time`           | `TimeOnly`                | `LocalTime`           | `time.Time`       | `Date`       |
| `Binary`         | `byte[]`                  | `byte[]`              | `[]byte`          | `Uint8Array` |
| `Blob`           | `byte[]`                  | `byte[]`              | `[]byte`          | `Uint8Array` |
| `Uuid`           | `Guid`                    | `UUID`                | `uuid.UUID`       | `string`     |

#### 5.2.4 逻辑类型 → 各数据库物理类型映射

| 逻辑类型         | MySQL          | MariaDB            | PostgreSQL         | SQL Server         | SQLite    |
| ---------------- | -------------- | ------------------ | ------------------ | ------------------ | --------- |
| `Boolean`        | `TINYINT(1)`   | `TINYINT(1)`       | `BOOLEAN`          | `BIT`              | `INTEGER` |
| `Int8`           | `TINYINT`      | `TINYINT`          | `SMALLINT`         | `TINYINT`          | `INTEGER` |
| `Int16`          | `SMALLINT`     | `SMALLINT`         | `SMALLINT`         | `SMALLINT`         | `INTEGER` |
| `Int32`          | `INT`          | `INT`              | `INTEGER`          | `INT`              | `INTEGER` |
| `Int64`          | `BIGINT`       | `BIGINT`           | `BIGINT`           | `BIGINT`           | `INTEGER` |
| `Float32`        | `FLOAT`        | `FLOAT`            | `REAL`             | `REAL`             | `REAL`    |
| `Float64`        | `DOUBLE`       | `DOUBLE`           | `DOUBLE PRECISION` | `FLOAT`            | `REAL`    |
| `Decimal(p,s)`   | `DECIMAL(p,s)` | `DECIMAL(p,s)`     | `NUMERIC(p,s)`     | `DECIMAL(p,s)`     | `NUMERIC` |
| `FixedString(n)` | `CHAR(n)`      | `CHAR(n)`          | `CHAR(n)`          | `NCHAR(n)`         | `TEXT`    |
| `String(n)`      | `VARCHAR(n)`   | `VARCHAR(n)`       | `VARCHAR(n)`       | `NVARCHAR(n)`      | `TEXT`    |
| `Text`           | `LONGTEXT`     | `LONGTEXT`         | `TEXT`             | `NVARCHAR(MAX)`    | `TEXT`    |
| `Json`           | `JSON`         | `LONGTEXT` + CHECK | `JSONB`            | `NVARCHAR(MAX)`    | `TEXT`    |
| `Xml`            | `LONGTEXT`     | `LONGTEXT`         | `XML`              | `XML`              | `TEXT`    |
| `DateTime`       | `DATETIME`     | `DATETIME`         | `TIMESTAMP`        | `DATETIME2`        | `TEXT`    |
| `DateTimeOffset` | `TIMESTAMP` ⚠️  | `TIMESTAMP` ⚠️      | `TIMESTAMPTZ`      | `DATETIMEOFFSET`   | `TEXT`    |
| `Date`           | `DATE`         | `DATE`             | `DATE`             | `DATE`             | `TEXT`    |
| `Time`           | `TIME`         | `TIME`             | `TIME`             | `TIME`             | `TEXT`    |
| `Binary(n)`      | `VARBINARY(n)` | `VARBINARY(n)`     | `BYTEA`            | `VARBINARY(n)`     | `BLOB`    |
| `Blob`           | `LONGBLOB`     | `LONGBLOB`         | `BYTEA`            | `VARBINARY(MAX)`   | `BLOB`    |
| `Uuid`           | `CHAR(36)`     | `CHAR(36)`         | `UUID`             | `UNIQUEIDENTIFIER` | `TEXT`    |

**MariaDB `Json` 特殊处理：**

MariaDB 无原生 JSON 类型，映射为 `LONGTEXT`，方言类自动追加 `CHECK (JSON_VALID(col))` 约束：

```sql
ALTER TABLE `nor_user` ADD COLUMN `profile` LONGTEXT NULL;
ALTER TABLE `nor_user` ADD CONSTRAINT `ck_nor_user_profile_json`
  CHECK (`profile` IS NULL OR JSON_VALID(`profile`));
```

**关于 `Uuid`：**

- MySQL/MariaDB 用 `CHAR(36)`，SQLite 用 `TEXT`，PG 用 `UUID`，SQL Server 用 `UNIQUEIDENTIFIER`。
- 一期不生成 `DEFAULT UUID()` 函数，UUID 由应用层生成写入。

**关于 `Xml` 降级：**

- PG/SQL Server 原生 `XML`，MySQL/MariaDB/SQLite 降级 `LONGTEXT`/`TEXT`。
- 编译器在 `Xml` 降级时输出信息性注释。

**关于无符号整数：**

不引入独立无符号逻辑类型。MySQL/MariaDB 原生支持 `UNSIGNED`，其他数据库不支持。通过 `unsigned="true"` 属性表达，仅 MySQL/MariaDB 生效，其他方言忽略并输出警告：

```sql
-- MySQL
`view_count` INT UNSIGNED NOT NULL

-- PostgreSQL（忽略 unsigned，输出警告）
-- WARNING: unsigned=true ignored by PostgreSQL. Use CHECK (view_count >= 0) instead.
"view_count" INTEGER NOT NULL
```

#### 5.2.5 时间类型与时区处理

**逻辑类型语义：**

| 逻辑类型         | 语义                          | 是否带时区 |
| ---------------- | ----------------------------- | ---------- |
| `DateTime`       | 日期 + 时间，不存储时区偏移量 | 否         |
| `DateTimeOffset` | 日期 + 时间 + 时区偏移量      | 是         |
| `Date`           | 只有日期                      | 否         |
| `Time`           | 只有时间                      | 否         |

**时间类型映射：**

| 逻辑类型         | MySQL         | MariaDB       | PostgreSQL    | SQL Server       | SQLite |
| ---------------- | ------------- | ------------- | ------------- | ---------------- | ------ |
| `DateTime`       | `DATETIME`    | `DATETIME`    | `TIMESTAMP`   | `DATETIME2`      | `TEXT` |
| `DateTimeOffset` | `TIMESTAMP` ⚠️ | `TIMESTAMP` ⚠️ | `TIMESTAMPTZ` | `DATETIMEOFFSET` | `TEXT` |
| `Date`           | `DATE`        | `DATE`        | `DATE`        | `DATE`           | `TEXT` |
| `Time`           | `TIME`        | `TIME`        | `TIME`        | `TIME`           | `TEXT` |

**各方言语义差异：**

| 数据库          | 关键问题                                                                 | 建议                                                                                                                                       |
| --------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------ |
| MySQL / MariaDB | `TIMESTAMP` 存 UTC、按会话时区转换，**不存储原始偏移量**，2038 问题      | `DateTime` → `DATETIME`；`DateTimeOffset` → `TIMESTAMP` + 输出警告                                                                         |
| PostgreSQL      | `TIMESTAMPTZ` 存 UTC、按会话时区转换，**不存储原始偏移量**，无 2038 问题 | `DateTime` → `TIMESTAMP`；`DateTimeOffset` → `TIMESTAMPTZ`（近似映射）                                                                     |
| SQL Server      | `DATETIMEOFFSET` **真正存储偏移量**，与 `DateTimeOffset` 语义完全对齐    | `DateTime` → `DATETIME2`；`DateTimeOffset` → `DATETIMEOFFSET`（完美映射）                                                                  |
| SQLite          | 无原生时间类型，一期用 `TEXT` 存 ISO8601                                 | `DateTime` → `TEXT`（无时区）；`DateTimeOffset` → `TEXT`（带偏移）；**格式一致性由应用层负责**，编译器在列注释中标注格式约定，DDL 层不强制 |

**编译器的警告机制：**

当 `DateTimeOffset` 映射到 MySQL/MariaDB 的 `TIMESTAMP` 时，编译器输出警告：

```sql
-- WARNING: DateTimeOffset mapped to MySQL TIMESTAMP.
-- MySQL TIMESTAMP stores UTC and converts by session timezone;
-- it does NOT store the original offset. Range limited to 2038-01-19.
-- Consider using DateTime with application-level UTC convention instead.
ALTER TABLE `nor_user` ADD COLUMN `updated_at` TIMESTAMP NULL;
```

**默认值中的时间函数：**

| 默认值              | MySQL                  | PostgreSQL                             | SQL Server         | SQLite            |
| ------------------- | ---------------------- | -------------------------------------- | ------------------ | ----------------- |
| `CURRENT_TIMESTAMP` | 支持                   | 支持                                   | 支持               | 支持              |
| `CURRENT_DATE`      | 支持                   | 支持                                   | 支持               | 支持              |
| `CURRENT_TIME`      | 支持                   | 支持                                   | 支持               | 支持              |
| 带精度              | `CURRENT_TIMESTAMP(3)` | `CURRENT_TIMESTAMP(3)`                 | `SYSUTCDATETIME()` | 不支持精度        |
| UTC 时间            | `UTC_TIMESTAMP()`      | `CURRENT_TIMESTAMP AT TIME ZONE 'UTC'` | `SYSUTCDATETIME()` | `datetime('now')` |

**`onUpdate` 的特殊性：**

- MySQL 支持 `ON UPDATE CURRENT_TIMESTAMP`，原生实现"更新时自动刷新"。
- PG/SQL Server/SQLite **没有原生等价物**，需触发器实现。
- 一期策略：`onUpdate` 仅在 MySQL/MariaDB 生成原生语法，其他方言**忽略并输出警告**。

#### 5.2.6 类型映射配置化

每个方言维护一份 YAML 映射表，key 是逻辑类型名，value 是物理类型模板。

```yaml
# DbChronicle/Resources/dialects/mysql.types.xml
Boolean:          TINYINT(1)
Int8:             TINYINT
Int16:            SMALLINT
Int32:            INT
Int64:            BIGINT
Float32:          FLOAT
Float64:          DOUBLE
Decimal:          DECIMAL({precision},{scale})
FixedString:      CHAR({length})
String:           VARCHAR({length})
Text:             LONGTEXT
Json:             JSON
Xml:              LONGTEXT
DateTime:         DATETIME
DateTimeOffset:   TIMESTAMP                  # ⚠️ 有语义偏差，编译器输出警告
Date:             DATE
Time:             TIME
Binary:           VARBINARY({length})
Blob:             LONGBLOB
Uuid:             CHAR(36)
```

```yaml
# DbChronicle/Resources/dialects/mariadb.types.xml —— 独立文件，不完全复用 mysql
Json:             LONGTEXT                  # MariaDB 无原生 JSON，用 LONGTEXT + CHECK
```

```yaml
# DbChronicle/Resources/dialects/postgresql.types.xml
Boolean:          BOOLEAN
Int8:             SMALLINT
Int16:            SMALLINT
Int32:            INTEGER
Int64:            BIGINT
Float32:          REAL
Float64:          DOUBLE PRECISION
Decimal:          NUMERIC({precision},{scale})
FixedString:      CHAR({length})
String:           VARCHAR({length})
Text:             TEXT
Json:             JSONB
Xml:              XML
DateTime:         TIMESTAMP
DateTimeOffset:   TIMESTAMPTZ
Date:             DATE
Time:             TIME
Binary:           BYTEA
Blob:             BYTEA
Uuid:             UUID
```

```yaml
# DbChronicle/Resources/dialects/sqlserver.types.xml
Boolean:          BIT
Int8:             TINYINT
Int16:            SMALLINT
Int32:            INT
Int64:            BIGINT
Float32:          REAL
Float64:          FLOAT
Decimal:          DECIMAL({precision},{scale})
FixedString:      NCHAR({length})
String:           NVARCHAR({length})
Text:             NVARCHAR(MAX)
Json:             NVARCHAR(MAX)
Xml:              XML
DateTime:         DATETIME2
DateTimeOffset:   DATETIMEOFFSET
Date:             DATE
Time:             TIME
Binary:           VARBINARY({length})
Blob:             VARBINARY(MAX)
Uuid:             UNIQUEIDENTIFIER
```

```yaml
# DbChronicle/Resources/dialects/sqlite.types.xml
Boolean:          INTEGER
Int8:             INTEGER
Int16:            INTEGER
Int32:            INTEGER
Int64:            INTEGER
Float32:          REAL
Float64:          REAL
Decimal:          NUMERIC
FixedString:      TEXT
String:           TEXT
Text:             TEXT
Json:             TEXT
Xml:              TEXT
DateTime:         TEXT
DateTimeOffset:   TEXT
Date:             TEXT
Time:             TEXT
Binary:           BLOB
Blob:             BLOB
Uuid:             TEXT
```

#### 5.2.7 类型映射器实现

```csharp
public class TypeMapper
{
    private readonly Dictionary<LogicalType, string> _map;
    private readonly string _dialectName;

    public TypeMapper(string dialectName, Dictionary<LogicalType, string> map)
    {
        _dialectName = dialectName;
        _map = map;
    }

    public string Resolve(LogicalType type, int? length, int? precision, int? scale, bool unsigned = false)
    {
        if (!_map.TryGetValue(type, out var template))
            throw new NotSupportedException($"方言 {_dialectName} 不支持类型：{type}");

        // FixedString / String / Binary 的 length 由 Parser 阶段强制校验
        // Blob 的 length 由 Parser 阶段忽略并警告，模板不含 {length}
        var result = template
            .Replace("{length}", (length ?? 0).ToString())
            .Replace("{precision}", (precision ?? 18).ToString())
            .Replace("{scale}", (scale ?? 2).ToString());

        if (unsigned && (_dialectName == "mysql" || _dialectName == "mariadb"))
            result += " UNSIGNED";

        return result;
    }
}
```

#### 5.2.8 SQLite 类型映射的特殊性

SQLite 采用**存储类（Storage Class）** 机制，只有 5 种实际类型：`NULL`、`INTEGER`、`REAL`、`TEXT`、`BLOB`。

| 逻辑类型                                   | SQLite 存储类 | 说明                                |
| ------------------------------------------ | ------------- | ----------------------------------- |
| `Boolean`                                  | `INTEGER`     | 值 0/1，应用层转换                  |
| `Int8`/`Int16`/`Int32`/`Int64`             | `INTEGER`     | 统一 64 位整数                      |
| `Float32`/`Float64`                        | `REAL`        | 8 字节浮点                          |
| `Decimal`                                  | `NUMERIC`     | SQLite 实际用浮点存储，精度可能丢失 |
| `FixedString`/`String`/`Text`/`Json`/`Xml` | `TEXT`        | 不强制长度，长度约束需应用层校验    |
| `Binary`/`Blob`                            | `BLOB`        | 无长度限制                          |
| `DateTime`/`DateTimeOffset`/`Date`/`Time`  | `TEXT`        | ISO8601 格式                        |
| `Uuid`                                     | `TEXT`        | UUID 字符串                         |

### 5.3 操作类型转换

每个操作类型在 `ISqlDialect` 接口中对应一个方法。编译器主流程只做分发，方言类负责具体语法。

#### 5.3.1 表操作

**CREATE TABLE：**

| 数据库          | SQL 模板                                                                                                  |
| --------------- | --------------------------------------------------------------------------------------------------------- |
| MySQL / MariaDB | `CREATE TABLE [IF NOT EXISTS] \`t\` (col_defs, PRIMARY KEY (...)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;` |
| PostgreSQL      | `CREATE TABLE [IF NOT EXISTS] "t" (col_defs, PRIMARY KEY (...));`                                         |
| SQL Server      | `CREATE TABLE [t] (col_defs, PRIMARY KEY (...));`                                                         |
| SQLite          | `CREATE TABLE [IF NOT EXISTS] "t" (col_defs, PRIMARY KEY (...));`                                         |

**DROP TABLE：**

| 数据库          | SQL                                  |
| --------------- | ------------------------------------ |
| MySQL / MariaDB | `DROP TABLE IF EXISTS \`t\`;`        |
| PostgreSQL      | `DROP TABLE IF EXISTS "t";`          |
| SQL Server      | `DROP TABLE IF EXISTS [t];`（2016+） |
| SQLite          | `DROP TABLE IF EXISTS "t";`          |

**RENAME TABLE：**

| 数据库          | SQL                                  |
| --------------- | ------------------------------------ |
| MySQL / MariaDB | `RENAME TABLE \`old\` TO \`new\`;`   |
| PostgreSQL      | `ALTER TABLE "old" RENAME TO "new";` |
| SQL Server      | `EXEC sp_rename 'old', 'new';`       |
| SQLite          | `ALTER TABLE "old" RENAME TO "new";` |

**表注释：**

| 数据库          | SQL                                                                                   |
| --------------- | ------------------------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` COMMENT '...';`                                                    |
| PostgreSQL      | `COMMENT ON TABLE "t" IS '...';`                                                      |
| SQL Server      | 扩展属性，一期**跳过**并警告（W006）；需要时用 `<rawSql>` 写 `sp_addextendedproperty` |
| SQLite          | **不支持**，跳过并警告（W006）                                                        |

#### 5.3.2 列操作

**ADD COLUMN：**

| 数据库          | SQL 模板                                                                                                                      |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` ADD COLUMN \`c\` {type} {null} {default} {comment} {after};`                                               |
| PostgreSQL      | `ALTER TABLE "t" ADD COLUMN "c" {type} {null} {default};`<br>`COMMENT ON COLUMN "t"."c" IS '...';`（独立语句）                |
| SQL Server      | `ALTER TABLE [t] ADD [c] {type} {null};`<br>`ALTER TABLE [t] ADD CONSTRAINT [df_name] DEFAULT ... FOR [c];`（默认值独立约束） |
| SQLite          | `ALTER TABLE "t" ADD COLUMN "c" {type} {null} {default};`<br>**限制**：不能带 `PRIMARY KEY` / `UNIQUE`；若需要，走 rebuild    |

**ADD COLUMN 的列顺序（`after` / `first`）：**

| 数据库          | 支持 | 说明                                     |
| --------------- | ---- | ---------------------------------------- |
| MySQL / MariaDB | ✅    | `ADD COLUMN c ... AFTER other` / `FIRST` |
| PostgreSQL      | ❌    | 无列序概念，忽略 + 警告（W010）          |
| SQL Server      | ❌    | 无列序概念，忽略 + 警告（W010）          |
| SQLite          | ❌    | 无列序概念，忽略 + 警告（W010）          |

**MODIFY COLUMN（差异最大）：**

`MODIFY` 的写法由 `options.incrementalModify` 决定（见 4.5.5）：full 模式下 `{type}` / `{null}` / `{default}` 全部取 XML 的声明值；incremental 模式下未写的属性由累积模型（5.10）补全。**无论哪种模式，MySQL / MariaDB 下发的都是完整列定义**——这是引擎语义决定的，补全只发生在编译器侧。

| 数据库          | SQL 模板                                                                                                                                                                                                       |
| --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` MODIFY COLUMN \`c\` {type} {null} {default} {comment};`（一条语句）                                                                                                                         |
| PostgreSQL      | 拆成多条：<br>`ALTER TABLE "t" ALTER COLUMN "c" TYPE {type};`<br>`ALTER TABLE "t" ALTER COLUMN "c" SET/DROP NOT NULL;`<br>`ALTER TABLE "t" ALTER COLUMN "c" SET/DROP DEFAULT ...;`<br>`COMMENT ON COLUMN ...;` |
| SQL Server      | `ALTER TABLE [t] ALTER COLUMN [c] {type} {null};`<br>`ALTER TABLE [t] DROP CONSTRAINT [df_old];`（删旧默认值）<br>`ALTER TABLE [t] ADD CONSTRAINT [df_new] DEFAULT ... FOR [c];`                               |
| SQLite          | **不支持** `ALTER COLUMN`，**强制走 rebuild**：建新表→导数据→删旧→重命名                                                                                                                                       |

**SQL Server 默认值约束命名规则：**

约束名格式：`DF_{table}_{column}`

- 表名、列名保留原样，不转换大小写。
- 若总长度超过 128 字符（SQL Server 约束名上限），截断并追加 8 位 SHA-256 哈希后缀：`DF_{table前40}_{column前40}_{hash8}`。
- 例：`DF_nor_user_is_active`、`DF_nor_order_created_at`。

**MODIFY 的方言顺序细节：**

| 方言            | 顺序约束                                                                                                              | 违反时的表现与处置                                                                                         |
| --------------- | --------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| SQL Server      | 必须「删默认值约束 → `ALTER COLUMN` → 重建默认值约束」，删除用 `IF EXISTS`（`OBJECT_ID('DF_t_c') IS NOT NULL`）包一层 | 顺序颠倒会报"对象依赖于列"；约束名可预测，删除是安全的                                                     |
| PostgreSQL      | `TYPE` 变更若需要数据转换，必须显式写 `USING expr`，编译器不自动推断                                                  | 直接 `TYPE` 报"cannot be cast automatically"；用 `<dataMigration stage="before">` 或 `<rawSql>` 写 `USING` |
| MySQL / MariaDB | `MODIFY` 重写整列定义，索引与自增属性需一并重述                                                                       | 漏写 `AUTO_INCREMENT` 会丢自增；full 模式要求 XML 写全，incremental 模式由累积模型补全                     |
| SQLite          | 全部走 rebuild                                                                                                        | 见 5.5                                                                                                     |

**DROP COLUMN：**

| 数据库          | SQL                                                                 |
| --------------- | ------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` DROP COLUMN \`c\`;`                              |
| PostgreSQL      | `ALTER TABLE "t" DROP COLUMN IF EXISTS "c";`                        |
| SQL Server      | `ALTER TABLE [t] DROP COLUMN [c];`                                  |
| SQLite          | 3.35.0+：`ALTER TABLE "t" DROP COLUMN "c";`<br>低于 3.35.0：rebuild |

**RENAME COLUMN：**

| 数据库          | SQL                                                                      |
| --------------- | ------------------------------------------------------------------------ |
| MySQL 8.0+      | `ALTER TABLE \`t\` RENAME COLUMN \`from\` TO \`to\`;`                    |
| MySQL 5.7       | `ALTER TABLE \`t\` CHANGE \`from\` \`to\` {type} ...;`（需重复类型定义） |
| MariaDB 10.5.2+ | `ALTER TABLE \`t\` RENAME COLUMN \`from\` TO \`to\`;`                    |
| PostgreSQL      | `ALTER TABLE "t" RENAME COLUMN "from" TO "to";`                          |
| SQL Server      | `EXEC sp_rename 't.from', 'to', 'COLUMN';`                               |
| SQLite 3.25.0+  | `ALTER TABLE "t" RENAME COLUMN "from" TO "to";`                          |
| SQLite < 3.25.0 | rebuild                                                                  |

#### 5.3.3 主键操作

**ADD PRIMARY KEY：**

| 数据库          | SQL                                                                  |
| --------------- | -------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` ADD PRIMARY KEY (\`c1\`, \`c2\`);`                |
| PostgreSQL      | `ALTER TABLE "t" ADD PRIMARY KEY ("c1", "c2");`                      |
| SQL Server      | `ALTER TABLE [t] ADD CONSTRAINT [pk_name] PRIMARY KEY ([c1], [c2]);` |
| SQLite          | **不支持** `ADD PRIMARY KEY`，**强制 rebuild**                       |

**DROP PRIMARY KEY：**

| 数据库          | SQL                                                     |
| --------------- | ------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` DROP PRIMARY KEY;`                   |
| PostgreSQL      | `ALTER TABLE "t" DROP CONSTRAINT "t_pkey";`（需约束名） |
| SQL Server      | `ALTER TABLE [t] DROP CONSTRAINT [pk_name];`            |
| SQLite          | **不支持**，**强制 rebuild**                            |

#### 5.3.4 索引操作

**CREATE INDEX：**

| 数据库          | SQL                                                        |
| --------------- | ---------------------------------------------------------- |
| MySQL / MariaDB | `CREATE [UNIQUE] INDEX \`idx\` ON \`t\` (\`c1\`, \`c2\`);` |
| PostgreSQL      | `CREATE [UNIQUE] INDEX "idx" ON "t" ("c1", "c2");`         |
| SQL Server      | `CREATE [UNIQUE] INDEX [idx] ON [t] ([c1], [c2]);`         |
| SQLite          | `CREATE [UNIQUE] INDEX "idx" ON "t" ("c1", "c2");`         |

**DROP INDEX：**

| 数据库          | SQL                            |
| --------------- | ------------------------------ |
| MySQL / MariaDB | `DROP INDEX \`idx\` ON \`t\`;` |
| PostgreSQL      | `DROP INDEX IF EXISTS "idx";`  |
| SQL Server      | `DROP INDEX [idx] ON [t];`     |
| SQLite          | `DROP INDEX IF EXISTS "idx";`  |

#### 5.3.5 外键操作

**ADD FOREIGN KEY：**

| 数据库          | SQL                                                                                                                           |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` ADD CONSTRAINT \`fk\` FOREIGN KEY (\`c\`) REFERENCES \`ref\`(\`rc\`) ON DELETE CASCADE ON UPDATE CASCADE;` |
| PostgreSQL      | `ALTER TABLE "t" ADD CONSTRAINT "fk" FOREIGN KEY ("c") REFERENCES "ref"("rc") ON DELETE CASCADE ON UPDATE CASCADE;`           |
| SQL Server      | `ALTER TABLE [t] ADD CONSTRAINT [fk] FOREIGN KEY ([c]) REFERENCES [ref]([rc]) ON DELETE CASCADE ON UPDATE CASCADE;`           |
| SQLite          | 外键**只能在建表时声明**，`ADD FOREIGN KEY` 不支持；**强制 rebuild**；运行时需 `PRAGMA foreign_keys = ON;`                    |

**DROP FOREIGN KEY：**

| 数据库          | SQL                                          |
| --------------- | -------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` DROP FOREIGN KEY \`fk\`;` |
| PostgreSQL      | `ALTER TABLE "t" DROP CONSTRAINT "fk";`      |
| SQL Server      | `ALTER TABLE [t] DROP CONSTRAINT [fk];`      |
| SQLite          | **不支持**，**强制 rebuild**                 |

#### 5.3.6 约束操作

**ADD UNIQUE CONSTRAINT：**

| 数据库          | SQL                                                                  |
| --------------- | -------------------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` ADD CONSTRAINT \`uk\` UNIQUE (\`c\`);`            |
| PostgreSQL      | `ALTER TABLE "t" ADD CONSTRAINT "uk" UNIQUE ("c");`                  |
| SQL Server      | `ALTER TABLE [t] ADD CONSTRAINT [uk] UNIQUE ([c]);`                  |
| SQLite          | **不支持** `ADD CONSTRAINT`，可退化为 `CREATE UNIQUE INDEX` 实现等效 |

**DROP UNIQUE CONSTRAINT：**

| 数据库          | SQL                                                         |
| --------------- | ----------------------------------------------------------- |
| MySQL / MariaDB | `ALTER TABLE \`t\` DROP INDEX \`uk\`;`（**用 DROP INDEX**） |
| PostgreSQL      | `ALTER TABLE "t" DROP CONSTRAINT "uk";`                     |
| SQL Server      | `ALTER TABLE [t] DROP CONSTRAINT [uk];`                     |
| SQLite          | `DROP INDEX "uk";`（若用索引实现）                          |

**ADD CHECK CONSTRAINT：**

| 数据库          | SQL                                                             |
| --------------- | --------------------------------------------------------------- |
| MySQL 8.0+      | `ALTER TABLE \`t\` ADD CONSTRAINT \`ck\` CHECK (expr);`         |
| MySQL 5.7       | 语法接受但**不生效**，警告                                      |
| MariaDB 10.2.1+ | `ALTER TABLE \`t\` ADD CONSTRAINT \`ck\` CHECK (expr);`         |
| PostgreSQL      | `ALTER TABLE "t" ADD CONSTRAINT "ck" CHECK (expr);`             |
| SQL Server      | `ALTER TABLE [t] ADD CONSTRAINT [ck] CHECK (expr);`             |
| SQLite          | **不支持** `ADD CONSTRAINT`，只能建表时声明；一期**跳过**并警告 |

**DROP CHECK CONSTRAINT：** 同 `DROP UNIQUE CONSTRAINT` 的模式，MySQL 用 `DROP CHECK`，其他用 `DROP CONSTRAINT`。

### 5.4 属性转换

#### 5.4.1 NULL / NOT NULL

五方一致：`NULL` / `NOT NULL`。

#### 5.4.2 DEFAULT

**语法位置：**

| 数据库          | 写法                                                                                                                     |
| --------------- | ------------------------------------------------------------------------------------------------------------------------ |
| MySQL / MariaDB | 内联：`DEFAULT {value}`                                                                                                  |
| PostgreSQL      | 内联：`DEFAULT {value}`                                                                                                  |
| SQL Server      | **必须命名约束**：`CONSTRAINT [df_name] DEFAULT {value}` 或独立 `ALTER TABLE ... ADD CONSTRAINT ... DEFAULT ... FOR col` |
| SQLite          | 内联：`DEFAULT {value}`                                                                                                  |

**值格式化：**

```csharp
private static string FormatDefault(string value)
{
    if (value == null) return null;
    if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) return "NULL";
    if (value.StartsWith("CURRENT_")) return value;
    if (decimal.TryParse(value, out _)) return value;
    if (value.StartsWith("'") && value.EndsWith("'")) return value;
    return $"'{Escape(value)}'";
}
```

#### 5.4.3 COMMENT

| 数据库          | 写法                                            |
| --------------- | ----------------------------------------------- |
| MySQL / MariaDB | **内联在列定义后**：`COMMENT '...'`             |
| PostgreSQL      | **独立语句**：`COMMENT ON COLUMN t.c IS '...';` |
| SQL Server      | 扩展属性，一期**跳过**                          |
| SQLite          | **不支持**，跳过                                |

#### 5.4.4 AUTO_INCREMENT / IDENTITY

| 数据库          | 写法                                | 限制                                  |
| --------------- | ----------------------------------- | ------------------------------------- |
| MySQL / MariaDB | `AUTO_INCREMENT`                    | 只能用于整数主键列                    |
| PostgreSQL      | `GENERATED BY DEFAULT AS IDENTITY`  | 推荐                                  |
| SQL Server      | `IDENTITY(1,1)`                     | 只能用于整数列                        |
| SQLite          | `INTEGER PRIMARY KEY AUTOINCREMENT` | **只能是主键**，且必须 `INTEGER` 类型 |

**SQLite 的特殊性：** 自增必须同时满足 `INTEGER` 类型 + `PRIMARY KEY`，编译器强制校验：

```csharp
if (dialect is SqliteDialect && col.AutoIncrement)
{
    if (col.LogicalType != LogicalType.Int64 || !col.PrimaryKey)
        throw new InvalidOperationException(
            "SQLite 自增列必须同时满足：类型为 Int64 且 primaryKey=true");
}
```

#### 5.4.5 ON UPDATE

| 数据库          | 支持 | 写法                                                    |
| --------------- | ---- | ------------------------------------------------------- |
| MySQL / MariaDB | ✅    | `DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP` |
| PostgreSQL      | ❌    | 忽略，输出警告，需触发器                                |
| SQL Server      | ❌    | 忽略，输出警告，需触发器                                |
| SQLite          | ❌    | 忽略，输出警告，需触发器                                |

#### 5.4.6 标识符引用

| 数据库          | 引用符                   |
| --------------- | ------------------------ |
| MySQL / MariaDB | 反引号 `` ` ``           |
| PostgreSQL      | 双引号 `"`               |
| SQL Server      | 方括号 `[]`              |
| SQLite          | 双引号 `"` 或方括号 `[]` |

#### 5.4.7 字符串转义

| 数据库          | 转义规则                                                      |
| --------------- | ------------------------------------------------------------- |
| MySQL / MariaDB | `'` → `''`；`\` → `\\`；`\0` → `\\0`                          |
| PostgreSQL      | `'` → `''`（`standard_conforming_strings=on` 时反斜杠不转义） |
| SQL Server      | `'` → `''`                                                    |
| SQLite          | `'` → `''`                                                    |

### 5.5 SQLite 特殊处理

#### 5.5.1 rebuild 策略

当操作不被 SQLite 原生支持时，自动走 rebuild：

```
① CREATE TABLE t_rebuild_tmp (...完整新结构...);
② INSERT INTO t_rebuild_tmp SELECT ... FROM t;
③ DROP TABLE t;
④ ALTER TABLE t_rebuild_tmp RENAME TO t;
```

**触发 rebuild 的操作：**

| 操作                                 | 原因                         |
| ------------------------------------ | ---------------------------- |
| `MODIFY COLUMN`                      | SQLite 不支持 `ALTER COLUMN` |
| `ADD PRIMARY KEY`                    | 不支持                       |
| `DROP PRIMARY KEY`                   | 不支持                       |
| `ADD FOREIGN KEY`                    | 只能在建表时声明             |
| `DROP FOREIGN KEY`                   | 不支持                       |
| `ADD COLUMN` + `primaryKey`/`unique` | 不能带主键/唯一              |
| `DROP COLUMN`（版本 < 3.35.0）       | 不支持                       |
| `RENAME COLUMN`（版本 < 3.25.0）     | 不支持                       |

#### 5.5.2 数据迁移脚本

rebuild 过程中若有数据转换需求，需生成 `INSERT ... SELECT ...` 的转换表达式。一期策略：

- 简单类型变更（如 `INTEGER` → `TEXT`）：`CAST(col AS TEXT)`
- 复杂转换：需用户在 `<dataMigration>` 中提供 SQL

#### 5.5.3 注释跳过

SQLite 不支持 `COMMENT ON`，编译时**静默跳过** `comment` 属性。

#### 5.5.4 时间格式的应用层约束

SQLite 的时间列用 `TEXT` 存 ISO8601 字符串。**编译器在 DDL 层无法强制格式一致性**，职责边界如下：

| 职责                                   | 归属       |
| -------------------------------------- | ---------- |
| 输出列注释，标注格式约定               | 编译器     |
| 保证写入时格式一致                     | **应用层** |
| 保证查询时同一列不混用带偏移和不带偏移 | **应用层** |

**文档明确说明：** SQLite 的时间格式一致性由应用层保证，DbChronicle 不做 DDL 层校验。这是 SQLite 存储类机制的固有限制。

#### 5.5.5 外键 PRAGMA

SQLite 默认**不启用外键约束**，需每次连接时执行：

```sql
PRAGMA foreign_keys = ON;
```

编译器在 `CREATE TABLE` 后不自动加，但执行器在连接 SQLite 时**自动执行**此 PRAGMA。

### 5.6 编译器架构

#### 5.6.1 方言接口

```csharp
public interface ISqlDialect
{
    string Name { get; }
    string Version { get; }
    TypeMapper TypeMapper { get; }

    string CreateTable(CreateTableOperation op);
    string DropTable(DropTableOperation op);
    string RenameTable(RenameTableOperation op);
    string CommentTable(CommentTableOperation op);

    string AddColumn(AddColumnOperation op);
    string ModifyColumn(ModifyColumnOperation op);
    string DropColumn(DropColumnOperation op);
    string RenameColumn(RenameColumnOperation op);

    string AddPrimaryKey(AddPrimaryKeyOperation op);
    string DropPrimaryKey(DropPrimaryKeyOperation op);

    string CreateIndex(CreateIndexOperation op);
    string DropIndex(DropIndexOperation op);

    string AddForeignKey(AddForeignKeyOperation op);
    string DropForeignKey(DropForeignKeyOperation op);

    string AddUniqueConstraint(AddUniqueConstraintOperation op);
    string DropUniqueConstraint(DropUniqueConstraintOperation op);
    string AddCheckConstraint(AddCheckConstraintOperation op);
    string DropCheckConstraint(DropCheckConstraintOperation op);

    string RawSql(RawSqlOperation op);
}
```

#### 5.6.2 方言类实现示例（MySQL）

```csharp
public class MySqlDialect : ISqlDialect
{
    public string Name => "mysql";
    public string Version { get; }
    public TypeMapper TypeMapper { get; }

    public MySqlDialect(string version)
    {
        Version = version;
        TypeMapper = TypeMapperLoader.Load("mysql");
    }

    public string AddColumn(AddColumnOperation op)
    {
        var type = TypeMapper.Resolve(op.LogicalType, op.Length, op.Precision, op.Scale, op.Unsigned);
        var nullClause = op.Nullable ? "NULL" : "NOT NULL";
        var defaultClause = op.DefaultValue != null
            ? $" DEFAULT {FormatDefault(op.DefaultValue)}" : "";
        var onUpdateClause = op.OnUpdate != null
            ? $" ON UPDATE {op.OnUpdate}" : "";
        var commentClause = op.Comment != null
            ? $" COMMENT '{Escape(op.Comment)}'" : "";

        return $"ALTER TABLE {Quote(op.Table)} ADD COLUMN {Quote(op.Name)} " +
               $"{type} {nullClause}{defaultClause}{onUpdateClause}{commentClause};";
    }

    // 其他方法略
}
```

#### 5.6.3 MariaDB Json CHECK 追加

```csharp
public class MariaDbDialect : MySqlDialect
{
    public override string Name => "mariadb";

    public override string AddColumn(AddColumnOperation op)
    {
        var sql = base.AddColumn(op);
        if (op.LogicalType == LogicalType.Json)
        {
            var ckName = BuildJsonCheckConstraintName(op.Table, op.Name);
            sql += $"\nALTER TABLE {Quote(op.Table)} ADD CONSTRAINT {Quote(ckName)} " +
                   $"CHECK ({Quote(op.Name)} IS NULL OR JSON_VALID({Quote(op.Name)}));";
        }
        return sql;
    }

    private string BuildJsonCheckConstraintName(string table, string column)
    {
        var name = $"ck_{table}_{column}_json";
        return name.Length <= 128 ? name : name.Substring(0, 120) + "_" + Hash8(name);
    }
}
```

#### 5.6.4 编译器主流程

```csharp
public class SchemaCompiler
{
    private readonly ISqlDialect _dialect;
    private readonly CompileOptions _options;

    public string Compile(ChangeUnit unit)
    {
        var sb = new StringBuilder();
        if (_options.IncludeComments)
            sb.AppendLine(BuildHeader(unit));

        foreach (var op in unit.Operations)
            sb.AppendLine(Dispatch(op));

        return sb.ToString();
    }

    private string Dispatch(SchemaOperation op) => op switch
    {
        AddColumnOperation o            => _dialect.AddColumn(o),
        ModifyColumnOperation o         => _dialect.ModifyColumn(o),
        DropColumnOperation o           => _dialect.DropColumn(o),
        RenameColumnOperation o         => _dialect.RenameColumn(o),
        CreateIndexOperation o          => _dialect.CreateIndex(o),
        DropIndexOperation o            => _dialect.DropIndex(o),
        AddForeignKeyOperation o        => _dialect.AddForeignKey(o),
        DropForeignKeyOperation o       => _dialect.DropForeignKey(o),
        AddPrimaryKeyOperation o        => _dialect.AddPrimaryKey(o),
        DropPrimaryKeyOperation o       => _dialect.DropPrimaryKey(o),
        AddUniqueConstraintOperation o  => _dialect.AddUniqueConstraint(o),
        DropUniqueConstraintOperation o => _dialect.DropUniqueConstraint(o),
        AddCheckConstraintOperation o   => _dialect.AddCheckConstraint(o),
        DropCheckConstraintOperation o  => _dialect.DropCheckConstraint(o),
        RawSqlOperation o               => _dialect.RawSql(o),
        _ => throw new NotSupportedException(op.GetType().Name)
    };
}
```

**编译器主流程不写任何方言判断。**

`SchemaCompiler` 在编译前先构建累积模型（5.10）：`incrementalModify: true`（默认）时必需，置 `false` 时可跳过以省开销。

#### 5.6.5 编译结果对象与诊断码

编译产物不是裸字符串，而是带诊断的结果对象——凡是"用户声明了但目标方言支持不了"的情况必须留痕，**不允许静默丢弃**。

```csharp
public sealed class CompileResult
{
    public string Sql { get; set; }                                // 拼接后的完整脚本（含注释头）
    public IReadOnlyList<SqlStatement> Statements { get; set; }     // 拆分后的执行单元，见 6.5
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; set; }
}

public sealed class CompileDiagnostic
{
    public string Code { get; set; }                                // W 开头，见下表
    public DiagnosticSeverity Severity { get; set; }
    public string ChangeId { get; set; }
    public string Table { get; set; }
    public string Column { get; set; }
    public string Message { get; set; }
}
```

**诊断码（一期）：**

| 码   | 触发                                                                       |
| ---- | -------------------------------------------------------------------------- |
| W001 | `unsigned=true` 被非 MySQL 方言忽略                                        |
| W002 | `onUpdate` 被非 MySQL 方言忽略                                             |
| W003 | `DateTimeOffset` 映射到 MySQL / MariaDB 的 `TIMESTAMP`                     |
| W004 | `Xml` 降级为文本类型（MySQL / MariaDB / SQLite）                           |
| W005 | `Blob` 的 `length` 被忽略                                                  |
| W006 | 表注释被方言跳过（SQL Server / SQLite）                                    |
| W007 | 列注释被方言跳过（SQL Server / SQLite）                                    |
| W008 | `MODIFY` 未声明 `defaultValue`（目标将无默认值）                           |
| W009 | 文件字节不符合约定（CRLF / UTF-8 BOM），由 `check` 输出                    |
| W010 | 列顺序（`AFTER` / `FIRST` / `REORDER`）被执行方言忽略                      |
| W012 | `release` 与 `change_id` 日期不一致                                        |
| W013 | 同一 `release` 跨天提交                                                    |
| W014 | 增量补全基底缺失（无基线 / 表不在链 / 列不在链），该列退化为按已写内容生成 |
| W015 | 列级 `charset` / `collate` 被非 MySQL 方言忽略                             |
| W016 | 列级 `foreignKey` 被忽略（MySQL 的 `ADD/MODIFY COLUMN` 不支持内联外键）    |
| W017 | 索引 `type` 不被方言支持（`FULLTEXT` 等），按普通索引生成                  |
| W018 | 表级 `REBUILD` 仅做破坏性校验，未生成重建 SQL                              |
| W019 | 配置项 `includeRollback` 当前不生效（回滚请用 `rollback --to-release`）    |
| W021 | `table="*"` 展开为 N 张表（列出表名）                                      |
| W022 | `table="*"` 离线编译保留为占位符，需 `apply` 连库后展开                    |

**错误码（通配符相关）：**

| 码   | 触发                                                                                      |
| ---- | ----------------------------------------------------------------------------------------- |
| E041 | `table="*"` 用于不支持的操作（CREATE / DROP / RENAME / REBUILD / rawSql / dataMigration） |

**诊断的传播路径（四处必须一致）：**

1. CLI stdout：`warning W001 nor_user.view_count: unsigned ignored by postgresql`
2. `output/{dialect}.sql` 文件头：`-- WARNINGS: 3 (W001 x2, W010 x1)`
3. `version_log/{env}/{release}_dbupdate.md` 条目的「警告」小节
4. `check --strict-warnings`：把警告升级为失败，供 CI 门禁使用（默认不升级，避免历史文件无法合并）

### 5.7 完整示例

**输入（逻辑模型，语言中立）：**

```xml
<changeUnit id="20261022_100000_a1b2c3" author="zhangsan" release="20261022">
  <description>用户表增加手机号、国家代码、创建时间、更新时间</description>
  <table name="nor_user" action="ALTER">
    <column name="phone" action="ADD" type="String" length="20"
            nullable="true" comment="手机号"/>
    <column name="country_code" action="ADD" type="FixedString" length="2"
            nullable="false" comment="国家代码"/>
    <column name="is_active" action="ADD" type="Boolean"
            nullable="false" defaultValue="true"/>
    <column name="created_at" action="ADD" type="DateTime"
            nullable="false" defaultValue="CURRENT_TIMESTAMP" comment="创建时间"/>
    <column name="updated_at" action="ADD" type="DateTimeOffset"
            nullable="true" comment="更新时间"/>
    <column name="profile" action="ADD" type="Json"
            nullable="true" comment="用户画像"/>
    <column name="config_xml" action="ADD" type="Xml"
            nullable="true" comment="配置"/>
    <column name="uid" action="ADD" type="Uuid"
            nullable="false" comment="用户唯一标识"/>
    <column name="view_count" action="ADD" type="Int32"
            unsigned="true" nullable="false" defaultValue="0"/>
    <column name="modified_at" action="ADD" type="DateTime"
            nullable="true" defaultValue="CURRENT_TIMESTAMP"
            onUpdate="CURRENT_TIMESTAMP"/>
  </table>
</changeUnit>
```

**MySQL 输出：**

```sql
ALTER TABLE `nor_user` ADD COLUMN `phone` VARCHAR(20) NULL COMMENT '手机号';
ALTER TABLE `nor_user` ADD COLUMN `country_code` CHAR(2) NOT NULL COMMENT '国家代码';
ALTER TABLE `nor_user` ADD COLUMN `is_active` TINYINT(1) NOT NULL DEFAULT 1;
ALTER TABLE `nor_user` ADD COLUMN `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间';

-- WARNING: DateTimeOffset mapped to MySQL TIMESTAMP.
-- MySQL TIMESTAMP stores UTC and converts by session timezone;
-- it does NOT store the original offset. Range limited to 2038-01-19.
ALTER TABLE `nor_user` ADD COLUMN `updated_at` TIMESTAMP NULL COMMENT '更新时间';

ALTER TABLE `nor_user` ADD COLUMN `profile` JSON NULL COMMENT '用户画像';
-- INFO: Xml mapped to MySQL LONGTEXT. MySQL has no native XML type; XPath queries are unavailable.
ALTER TABLE `nor_user` ADD COLUMN `config_xml` LONGTEXT NULL COMMENT '配置';
ALTER TABLE `nor_user` ADD COLUMN `uid` CHAR(36) NOT NULL COMMENT '用户唯一标识';
ALTER TABLE `nor_user` ADD COLUMN `view_count` INT UNSIGNED NOT NULL DEFAULT 0;
ALTER TABLE `nor_user` ADD COLUMN `modified_at` DATETIME NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP;
```

**MariaDB 输出（Json 自动追加 CHECK）：**

```sql
ALTER TABLE `nor_user` ADD COLUMN `profile` LONGTEXT NULL COMMENT '用户画像';
ALTER TABLE `nor_user` ADD CONSTRAINT `ck_nor_user_profile_json`
  CHECK (`profile` IS NULL OR JSON_VALID(`profile`));
```

**PostgreSQL 输出：**

```sql
ALTER TABLE "nor_user" ADD COLUMN "phone" VARCHAR(20) NULL;
COMMENT ON COLUMN "nor_user"."phone" IS '手机号';

ALTER TABLE "nor_user" ADD COLUMN "country_code" CHAR(2) NOT NULL;
COMMENT ON COLUMN "nor_user"."country_code" IS '国家代码';

ALTER TABLE "nor_user" ADD COLUMN "is_active" BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE "nor_user" ADD COLUMN "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP;
COMMENT ON COLUMN "nor_user"."created_at" IS '创建时间';

ALTER TABLE "nor_user" ADD COLUMN "updated_at" TIMESTAMPTZ NULL;
COMMENT ON COLUMN "nor_user"."updated_at" IS '更新时间';

ALTER TABLE "nor_user" ADD COLUMN "profile" JSONB NULL;
COMMENT ON COLUMN "nor_user"."profile" IS '用户画像';

ALTER TABLE "nor_user" ADD COLUMN "config_xml" XML NULL;
COMMENT ON COLUMN "nor_user"."config_xml" IS '配置';

ALTER TABLE "nor_user" ADD COLUMN "uid" UUID NOT NULL;
COMMENT ON COLUMN "nor_user"."uid" IS '用户唯一标识';

-- WARNING: unsigned=true ignored by PostgreSQL. Use CHECK (view_count >= 0) instead.
ALTER TABLE "nor_user" ADD COLUMN "view_count" INTEGER NOT NULL DEFAULT 0;

-- WARNING: onUpdate ignored by PostgreSQL. Implement via trigger instead.
ALTER TABLE "nor_user" ADD COLUMN "modified_at" TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP;
```

**SQL Server 输出：**

```sql
ALTER TABLE [nor_user] ADD [phone] NVARCHAR(20) NULL;
ALTER TABLE [nor_user] ADD [country_code] NCHAR(2) NOT NULL;
ALTER TABLE [nor_user] ADD [is_active] BIT NOT NULL CONSTRAINT [df_nor_user_is_active] DEFAULT 1;
ALTER TABLE [nor_user] ADD [created_at] DATETIME2 NOT NULL CONSTRAINT [df_nor_user_created_at] DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE [nor_user] ADD [updated_at] DATETIMEOFFSET NULL;
ALTER TABLE [nor_user] ADD [profile] NVARCHAR(MAX) NULL;
ALTER TABLE [nor_user] ADD [config_xml] XML NULL;
ALTER TABLE [nor_user] ADD [uid] UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE [nor_user] ADD [view_count] INT NOT NULL CONSTRAINT [df_nor_user_view_count] DEFAULT 0;
ALTER TABLE [nor_user] ADD [modified_at] DATETIME2 NULL CONSTRAINT [df_nor_user_modified_at] DEFAULT CURRENT_TIMESTAMP;
```

**SQLite 输出：**

```sql
ALTER TABLE "nor_user" ADD COLUMN "phone" TEXT NULL;
ALTER TABLE "nor_user" ADD COLUMN "country_code" TEXT NOT NULL;
ALTER TABLE "nor_user" ADD COLUMN "is_active" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "nor_user" ADD COLUMN "created_at" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
-- created_at 列存储格式：ISO8601 无时区，如 2026-10-22T10:30:00，格式一致性由应用层保证
ALTER TABLE "nor_user" ADD COLUMN "updated_at" TEXT NULL;
-- updated_at 列存储格式：ISO8601 带偏移，如 2026-10-22T10:30:00+08:00，格式一致性由应用层保证
ALTER TABLE "nor_user" ADD COLUMN "profile" TEXT NULL;
ALTER TABLE "nor_user" ADD COLUMN "config_xml" TEXT NULL;
ALTER TABLE "nor_user" ADD COLUMN "uid" TEXT NOT NULL;
ALTER TABLE "nor_user" ADD COLUMN "view_count" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "nor_user" ADD COLUMN "modified_at" TEXT NULL DEFAULT CURRENT_TIMESTAMP;
```

**逻辑模型完全不变，只改配置里的 `dialect`，输出自动切换。**

### 5.8 扩展新语言与新方言的成本

**扩展新方言（新增数据库）：**

1. 新增一份 `dialects/{name}.types.xml`（类型映射表，约 22 行）。
2. 新增一个 `ISqlDialect` 实现类（约 200~300 行）。

**扩展新语言（如 Java 支持）：**

需要两个方向的能力，分属不同模块：

| 能力                      | 模块                       | 成本                                                 |
| ------------------------- | -------------------------- | ---------------------------------------------------- |
| 反向工程：Java 实体 → XML | `DbChronicle.Scanner.Java` | 语言类型 ↔ 逻辑类型映射表（约 20 行）+ 实体解析器    |
| 代码生成：XML → Java 实体 | `DbChronicle.CodeGen.Java` | 逻辑类型 → Java 类型映射表（约 20 行）+ 代码生成模板 |

**逻辑模型 XML 文件完全不变**，这是语言中立类型设计的核心价值。

### 5.9 反向工程与种子逻辑模型

#### 5.9.1 种子逻辑模型的定位

```
存量项目接入流程：

① 现有 C# 实体类
      │
      │ 反向扫描（Scanner）
      ▼
② 种子逻辑模型 XML（v0.0.0 基线，release="00000000"）
      │
      │ 人工确认、修正
      ▼
③ 基线冻结（写入 DbchronicleConfig.xml 的 baseline）
      │
      ▼
④ 后续迭代只写增量变更单元
```

**种子逻辑模型 = 当前数据库期望结构的全量快照。** 它不是"变更"，是"起点"。

#### 5.9.2 反向扫描流程

```
C# 实体类（.cs 文件 / 编译后程序集）
      │
      ▼
① 元数据提取：反射读取程序集（一期支持）
      │
      ▼
② 实体识别：[Table] / [Entity] / 继承基类
      │
      ▼
③ 列提取：属性名、C# 类型、[MaxLength]、[Required]、[Column(TypeName)]、[Key]
      │
      ▼
④ 索引/外键提取：[Index]、[ForeignKey]、导航属性
      │
      ▼
⑤ 生成种子逻辑模型 XML
```

**一期 `scan` 命令只支持程序集反射（`--assembly`），源码扫描（`--source`，基于 Roslyn）作为二期可选能力。**

#### 5.9.3 C# 类型 → 逻辑类型判定规则

| C# 类型                                   | 逻辑类型                  | 判定依据                                |
| ----------------------------------------- | ------------------------- | --------------------------------------- |
| `bool`                                    | `Boolean`                 | 直接映射                                |
| `sbyte`                                   | `Int8`                    | 有符号                                  |
| `byte`                                    | `Int8` + `unsigned=true`  | 无符号，需标注                          |
| `short`                                   | `Int16`                   | —                                       |
| `ushort`                                  | `Int16` + `unsigned=true` | —                                       |
| `int`                                     | `Int32`                   | —                                       |
| `uint`                                    | `Int32` + `unsigned=true` | —                                       |
| `long`                                    | `Int64`                   | —                                       |
| `ulong`                                   | `Int64` + `unsigned=true` | —                                       |
| `float`                                   | `Float32`                 | —                                       |
| `double`                                  | `Float64`                 | —                                       |
| `decimal`                                 | `Decimal`                 | 精度从 `[Precision]` 或 `[Column]` 读取 |
| `string` + `[MaxLength]`                  | `String`                  | 定长                                    |
| `string` + `[Column(TypeName="CHAR(n)")]` | `FixedString`             | 定长字符                                |
| `string` + 无长度约束                     | `Text`                    | 不定长                                  |
| `string` + `[Column(TypeName="JSON")]`    | `Json`                    | JSON 语义                               |
| `string` + `[Column(TypeName="XML")]`     | `Xml`                     | XML 语义                                |
| `DateTime`                                | `DateTime`                | 无时区                                  |
| `DateTimeOffset`                          | `DateTimeOffset`          | 带时区                                  |
| `DateOnly`                                | `Date`                    | —                                       |
| `TimeOnly`                                | `Time`                    | —                                       |
| `byte[]`                                  | `Binary` / `Blob`         | 有长度 → `Binary`，无长度 → `Blob`      |
| `Guid`                                    | `Uuid`                    | —                                       |

**无法判定的类型**（如自定义结构体、枚举、复杂类型）：

- 枚举 → `Int32` 或 `String`，取决于 `[Column]` 的存储方式。
- 复杂类型 → 跳过，标记为 `TODO`，由人工补充。

#### 5.9.4 种子逻辑模型格式

```xml
<?xml version="1.0" encoding="utf-8"?>
<changeUnit id="00000000_000000_baseline" author="scanner" release="00000000">
  <description>基线：从 C# 实体反向生成（2026-10-22）</description>

  <table name="nor_user" action="CREATE" comment="用户表">
    <column name="id" action="ADD" type="Int64"
            primaryKey="true" autoIncrement="true" nullable="false"/>
    <column name="username" action="ADD" type="String" length="64"
            nullable="false" comment="用户名"/>
    <column name="email" action="ADD" type="String" length="128"
            nullable="true"/>
    <column name="status" action="ADD" type="Int32"
            nullable="false" defaultValue="0"/>
    <column name="created_at" action="ADD" type="DateTime"
            nullable="false" defaultValue="CURRENT_TIMESTAMP"/>
    <index name="idx_user_email" action="ADD" unique="true">
      <columnRef name="email"/>
    </index>
  </table>

  <table name="nor_order" action="CREATE" comment="订单表">
    <column name="id" action="ADD" type="Int64"
            primaryKey="true" autoIncrement="true" nullable="false"/>
    <column name="user_id" action="ADD" type="Int64" nullable="false"/>
    <column name="amount" action="ADD" type="Decimal"
            precision="18" scale="2" nullable="false"/>
    <foreignKey name="fk_order_user" action="ADD"
                columns="user_id" refTable="nor_user" refColumns="id"
                onDelete="RESTRICT" onUpdate="CASCADE"/>
  </table>

  <!-- TODO: 以下实体无法自动识别，需人工补充 -->
</changeUnit>
```

#### 5.9.5 基线冻结与版本迭代

**种子逻辑模型生成后，不是"变更"，而是"基线"。** 后续迭代只写增量，不回改基线。

**配置声明基线：**

```yaml
baseline:
  file: migrations/00000000_000000_baseline.xml
  frozen: true
  frozen_at: "2026-10-22"
```

**执行流程：**

```
apply 命令：
  1. 读 version_log/DbchronicleState.xml
  2. 若为空（首次接入）：
     → 先执行基线（创建所有表），release = "00000000"
     → 基线也写入状态文件，changeId = 00000000_000000_baseline
  3. 再按 (release, change_id) 顺序执行增量变更单元
```

**基线已存在的数据库接入：**

如果数据库已经存在（不是空库），基线执行会因表已存在而跳过（编译时生成 `IF NOT EXISTS`），但需要在状态文件里**补一条基线记录**。两种方式：

```powershell
# 推荐：用 CLI 标记（checksum 自动取自文件）
db-chronicle apply --config DbchronicleConfig.xml --mark-applied 00000000_000000_baseline
```

```xml
<!-- 或手工编辑 version_log/DbchronicleState.xml -->
<applied changeId="00000000_000000_baseline" release="00000000"
         appliedAt="2026-10-23 10:00:00" checksum="<文件的 SHA-256>"
         dialect="mysql" appliedBy="manual" description="基线（存量库接入）"
         failureAt="" failureStmt=""/>
```

#### 5.9.6 反向扫描的局限性

| 场景                           | 原因                   | 处理                                 |
| ------------------------------ | ---------------------- | ------------------------------------ |
| 列重命名历史                   | 实体只反映当前状态     | 基线只管当前状态                     |
| 数据迁移                       | 实体不含数据迁移逻辑   | 基线不含数据迁移                     |
| 复杂索引（函数索引、部分索引） | C# 特性表达能力有限    | 扫描跳过，人工补充 `RawSqlOperation` |
| 检查约束                       | C# 特性通常不表达      | 扫描跳过，人工补充                   |
| 视图、触发器、存储过程         | 实体不涉及             | 扫描跳过，人工补充                   |
| 自定义类型                     | 无法映射到逻辑类型     | 标记 `TODO`，人工确认                |
| 多对多中间表                   | 导航属性推断可能有歧义 | 标记 `TODO`，人工确认                |

**结论：反向扫描生成的是"草案"，不是"最终基线"。人工审核、修正后冻结。**

#### 5.9.7 非 C# 项目的基线生成

| 方式                                    | 适用场景              | 一期可用 |
| --------------------------------------- | --------------------- | -------- |
| 手写基线 XML                            | 实体不多（< 20 张表） | ✅        |
| `Scanner.Database` 从现有数据库反向生成 | 数据库已存在          | ❌ 二期   |
| 对应语言的 Scanner（如 `Scanner.Java`） | 实体多，长期维护      | ❌ 二期   |

**一期非 C# 项目的推荐路径：** 手写基线 XML，或用任意脚本从实体定义生成 XML。XML 格式是语言中立的契约，Core 不关心它从哪来。

#### 5.9.8 一期范围

| 能力                          | 一期       | 说明                                 |
| ----------------------------- | ---------- | ------------------------------------ |
| 语言类型映射表                | ✅ 文档定义 | 5.2.3 节                             |
| C# 实体反向扫描（程序集反射） | ⚠️ 可选     | `scan --assembly`                    |
| C# 源码扫描（Roslyn）         | ❌ 二期     | `scan --source`                      |
| 种子逻辑模型生成              | ⚠️ 可选     | 生成 XML，人工确认                   |
| 基线冻结机制                  | ✅ 必须     | `baseline` 配置 + `version_log` 记录 |
| 数据库反向扫描                | ❌ 二期     | 从现有数据库提取                     |
| 代码生成（XML → 实体）        | ❌ 二期     | 从逻辑模型生成实体类                 |

### 5.10 累积模型（结构状态推导）

**用途：** `incrementalModify` 默认开启，编译器必须知道"这个 changeUnit 执行**之前**，某张表的某一列长什么样"，才能把"只写变化项"补成完整定义。这个状态由 `migrations/` 中的历史变更回放推导，**纯离线，不连数据库**。

**推导过程：**

```
输入：migrations/ 下全部 changeUnit，按 (release, change_id) 升序
      截止：当前 changeUnit 之前（不含自身，也不含更晚的单元）
  ↓ 从基线 00000000_000000_baseline.xml 的 CREATE TABLE 开始
  ↓ 依次回放每个单元的表级 / 列级操作
状态：Dictionary<table, TableState>
        TableState.Columns      : Dictionary<列名, 完整列定义>
        TableState.PrimaryKey   : 主键列集合
        TableState.Indexes      : 索引集合（SQLite rebuild 需要）
        TableState.ForeignKeys  : 外键集合（同上）
```

**回放规则：**

| 操作                  | 对模型的影响                                                |
| --------------------- | ----------------------------------------------------------- |
| `CREATE TABLE`        | 整表建立，列定义取自 `<column action="ADD">`                |
| `ADD COLUMN`          | 插入列                                                      |
| `MODIFY`              | full 模式：整体替换列定义；incremental 模式：按显式属性覆盖 |
| `DROP COLUMN`         | 删除列                                                      |
| `RENAME COLUMN`       | 键改名，定义不变                                            |
| `RENAME TABLE`        | 表键改名，内容不变                                          |
| `DROP TABLE`          | 删除表                                                      |
| `COMMENT` / `REORDER` | 只改注释 / 顺序，不改变类型与约束                           |

**关键性质：**

- **顺序敏感**：模型只覆盖当前单元之前的部分，因此「单文件编译」与「全量编译」必须得到一致结果。
- **与库无关**：模型只反映"变更链声称的结构"，库被手工改过它并不知道——这是 `apply` 阶段 introspect 复核要解决的问题。
- **不依赖数据库**：纯文件回放，`compile` / `check` 可在无网络环境运行。
- **成本**：迁移文件数量在千级以内时回放是毫秒级；可用目录 mtime + 文件哈希做缓存。

**构建失败与退化：**

| 情形                                     | 结果                                           |
| ---------------------------------------- | ---------------------------------------------- |
| 基线缺失，或基线不含 `CREATE` 全量       | 模型为空 → 所有 `MODIFY` 退化为当前声明 + W014 |
| 变更链断裂（`depends` 指向不存在的单元） | `check` 报错（文件完整性问题，不做退化）       |
| 表 / 列不在模型中                        | 该表 / 该列的 `MODIFY` 退化为当前声明 + W014   |
| 同一列在单元内被多处修改                 | 沿用 4.5.10 的重复检测（解析失败）             |

退化语义与告警口径见 4.5.5：模型构建本身绝不"猜结构"，退化只是"不补全"。

**`apply` 的模型漂移复核（v1.1）：** 连接数据库后读取真实列定义（`INFORMATION_SCHEMA.COLUMNS` / `pg_catalog`），与累积模型比对，输出"模型漂移"报告（哪些表、哪些列、差异点），由用户决定是否中止。一期不做，只在 `status` 中提示"存在手工变更可能"。

---

## 六、核心设计

### 6.1 操作模型

```csharp
public abstract class SchemaOperation
{
    public string Table { get; set; }
}

public class AddColumnOperation : SchemaOperation
{
    public string Name { get; set; }
    public LogicalType LogicalType { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool Nullable { get; set; }
    public string DefaultValue { get; set; }
    public string Comment { get; set; }
    public bool Unsigned { get; set; }
    public string OnUpdate { get; set; }
}

public class ModifyColumnOperation : SchemaOperation { /* 同上 */ }
public class DropColumnOperation : SchemaOperation { public string Name { get; set; } }
public class RenameColumnOperation : SchemaOperation { public string From { get; set; } public string To { get; set; } }
public class CreateIndexOperation : SchemaOperation { /* ... */ }
public class DropIndexOperation : SchemaOperation { /* ... */ }
public class AddForeignKeyOperation : SchemaOperation { /* ... */ }
public class DropForeignKeyOperation : SchemaOperation { /* ... */ }
public class AddPrimaryKeyOperation : SchemaOperation { /* ... */ }
public class DropPrimaryKeyOperation : SchemaOperation { /* ... */ }
public class AddUniqueConstraintOperation : SchemaOperation { /* ... */ }
public class DropUniqueConstraintOperation : SchemaOperation { /* ... */ }
public class AddCheckConstraintOperation : SchemaOperation { /* ... */ }
public class DropCheckConstraintOperation : SchemaOperation { /* ... */ }
public class RawSqlOperation : SchemaOperation
{
    public Dictionary<string, string> SqlByDialect { get; set; }
}
```

**数据类操作的 `Stage`：** `SeedOperation` / `UpdateOperation` / `DeleteOperation` / `CleanOperation` / `BackfillOperation` 增加 `Stage`（`Before` / `After`，缺省 `After`），供 4.2 的四段排序使用；`RawSqlOperation` 不受 `Stage` 影响，恒在最后一段按声明顺序执行。

### 6.2 执行器（可选出口）

**嵌入式集成（不经过 CLI）：** 核心库**不含任何数据库驱动**，宿主应用（Web 服务 / 控制台 / 测试）需要：① 引用 `DbChronicle`；② 自己实现 `IDbExecutor`（约 100 行，用现有连接管理）；③ 用 `DbChronicleFactory` 拿到 `IDbChronicle`，调用 `DbUpgrade()`。

```csharp
// 1) 宿主自己创建执行器（ADO.NET / EF / Dapper 皆可，实现 IDbExecutor 的 5 个成员）
IDbExecutor executor = new MyHostDbExecutor(connectionString);

// 2) 工厂：把启动配置与执行器实例传进去
var db = DbChronicleFactory.Create(executor, o =>
{
    o.ConfigPath = "DbchronicleConfig.xml";   // 逻辑模型与审计路径都在这个 XML 里
    o.Log = Console.WriteLine;
});

// 3) 一个方法完成升级
var result = db.DbUpgrade();
if (!result.Success)
{
    throw new InvalidOperationException(result.Message);
}
```

同一实例还提供 `Compile`（只生成 SQL）、`Check`（语义检查）、`Status`（读版本表）、`LoadUnits`，便于宿主自己做流程编排（例如启动时只 `Check`、发布流水线里才 `DbUpgrade`）。

**约束：** 一个数据库对应一套变更链（`change_id` 是版本表主键）。需要多套独立变更链时，用不同的数据库实例（演示见 `Test/DbChronicle.Demo --embedded`，它会创建独立的 `dbchronicle_embedded_demo` 库）。

**宿主启动即升级（由宿主自己决定，组件不内置 `IHostedService`）：** 升级时机属于宿主的生命周期决策，组件只提供 `IDbChronicle.DbUpgrade()`。宿主若想"启动时升级"，自己写一个 hosted service：

```csharp
// Program.cs
builder.Services.AddHostedService<DbUpgradeOnStartup>();

// DbUpgradeOnStartup.cs（宿主代码，约 20 行）
public sealed class DbUpgradeOnStartup : IHostedService
{
    private readonly IDbExecutor _executor;
    private readonly ILogger<DbUpgradeOnStartup> _logger;

    public DbUpgradeOnStartup(IDbExecutor executor, ILogger<DbUpgradeOnStartup> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var db = DbChronicleFactory.Create(_executor, o =>
        {
            o.ConfigPath = "DbchronicleConfig.xml";
            o.Log = message => _logger.LogInformation(message);
        });

        var result = db.DbUpgrade();
        if (!result.Success)
        {
            throw new InvalidOperationException("数据库升级失败：" + result.Message);
        }

        _logger.LogInformation("数据库升级完成：应用 {Applied}，跳过 {Skipped}",
            result.AppliedCount, result.SkippedCount);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`StartAsync` 内同步执行（完成前不接流量）。不想要这个行为时，把升级放到发布流水线里跑 CLI 即可。

```csharp
public interface IDbExecutor : IDisposable
{
    string DialectName { get; }
    bool SupportsTransactionalDdl { get; }

    int Execute(string sql);
    object QueryScalar(string sql);
    List<Dictionary<string, object>> Query(string sql);

    void BeginTransaction();
    void Commit();
    void Rollback();
}

public interface IDbExecutorFactory
{
    string DialectName { get; }
    IDbExecutor Create(string connectionString);
}

// 语句拆分由方言负责：编译器产出文本，执行器只吃单条语句
public interface ISqlStatementSplitter
{
    IReadOnlyList<SqlStatement> Split(string script);
}

// 库级互斥锁：apply 全程持有，见 6.6
public interface IDbLock : IDisposable
{
    bool TryAcquire(TimeSpan timeout);
}

public sealed class SqlStatement
{
    public int Index { get; set; }
    public string Text { get; set; }
    public string Hash { get; set; }   // SHA-256(语句文本)，用于断点续跑比对
}
```

**核心库是库，只依赖接口；执行器实现是独立的库包，按需引用：**

| 包                                        | 形态 | 用途                                                      |
| ----------------------------------------- | ---- | --------------------------------------------------------- |
| `DbChronicle.Executor.AdoNet`（命名空间） | 内置 | ADO.NET 执行器（CLI 默认），仅随 `DbChronicle.Cli` 包发布 |
| `DbChronicle.Executor.EfCore`             | 库   | EF Core 执行器（应用内嵌，二期）                          |

应用也可以自己实现 `IDbExecutor`，注入自定义执行器。

**DDL 事务差异：**

| 数据库          | 支持事务回滚 DDL | 一期策略                                          | 失败后的状态           |
| --------------- | ---------------- | ------------------------------------------------- | ---------------------- |
| MySQL / MariaDB | ❌                | 执行前备份，破坏性操作二次确认                    | **可能半态**（见 6.7） |
| PostgreSQL      | ✅                | 整个 changeUnit 包裹在事务中                      | 无半态，可原样重跑     |
| SQL Server      | ✅                | 整个 changeUnit 包裹在事务中                      | 无半态，可原样重跑     |
| SQLite          | ✅                | 整个 changeUnit 包裹在事务中（`BEGIN IMMEDIATE`） | 无半态，可原样重跑     |

**幂等分两层，边界不同：**

| 层级       | 保证                               | 实现                                                                   | 覆盖范围                       |
| ---------- | ---------------------------------- | ---------------------------------------------------------------------- | ------------------------------ |
| 变更单元级 | 同一 `change_id` 不会被执行两次    | `DbchronicleState.xml` 中 `changeId` 查重 + 执行前比对                 | 全部方言（一期生效）           |
| 语句级     | 同一单元内的单条语句不会被执行两次 | 有事务方言：整单元一个事务；无事务方言：语句级断点记录（v1.1，见 6.7） | 仅无事务方言需要，一期人工处置 |

**一期不追求"通用语句级幂等"的判断依据：**

给 MySQL / SQL Server 生成 `IF NOT EXISTS` 条件块，需要 `information_schema` + `PREPARE`（MySQL 5.7）或 `sys.columns` 探针 + 动态 SQL（SQL Server），产物会从"可人工审核的 DDL"退化成一堆元数据判断，与「SQL 可审核」的核心目标冲突；且条件块只能覆盖 `ADD` 类操作，`MODIFY` / `RENAME` / `DROP` 仍无法幂等。

结论：**把幂等边界收敛到"变更单元"这一层**，用事务（能力具备的方言）或断点续跑（无事务方言）解决半态，而不是把幂等责任散落到每条 DDL 上。

### 6.3 版本状态文件（本地）

版本状态**不落数据库**（不再创建 `dbchronicle_version_log` / `dbchronicle_version_stmt_log`），保存在 `paths.versionLog` 指向的目录下：

```
version_log/
├── DbchronicleState.xml          ← 版本状态（已应用单元 + 语句进度）
└── {release}_dbupdate.md         ← 审计日志（见 6.4）
```

文件结构：

```xml
<?xml version="1.0" encoding="utf-8"?>
<state>
  <applied changeId="20261022_100000_a1b2c3" release="20261022"
           appliedAt="2026-10-23 09:53:07" checksum="…" dialect="mysql"
           appliedBy="my-app" description="…" failureAt="" failureStmt="">
    <statements>
      <statement index="0" hash="…" appliedAt="2026-10-23 09:53:07" />
    </statements>
  </applied>
</state>
```

| 属性                        | 说明                                           |
| --------------------------- | ---------------------------------------------- |
| `changeId`                  | 变更单元 id（幂等键）                          |
| `checksum`                  | 文件 checksum，用于检测"已应用文件被修改"      |
| `failureAt` / `failureStmt` | 半态标记：失败时间与失败语句序号（成功后清空） |
| `<statement>`               | 语句级续跑进度（无事务方言使用）               |

**执行顺序（apply）：**

```
1. 连接数据库 + 方言一致性校验
2. 获取库级互斥锁（见 6.6），拿不到即退出（退出码 4）
3. 确保状态目录存在（不建表）
4. 读 DbchronicleState.xml，校验已应用文件的 checksum
5. 按 (release, change_id) 顺序执行待应用单元（拆分后逐条下发，见 6.5）
6. 每单元成功后写状态文件，并落 SQL 快照（见 6.4.5）
7. 生成 version_log/{release}_dbupdate.md
8. 释放互斥锁
```

**数据库当前版本：** 取状态文件中已应用单元的 `release` 最大值。

**设计取舍：**

| 维度       | 数据库表（旧） | 本地文件（现行）                                   |
| ---------- | -------------- | -------------------------------------------------- |
| 多实例共享 | 天然共享       | **需共享目录**（NFS / 共享盘），否则各实例各自为政 |
| 权限       | 需要建表权限   | 只需目录读写权限                                   |
| 审计可读   | 需连库查询     | 直接打开 XML                                       |
| 备份       | 随库备份       | 需单独纳入备份                                     |

> 多实例部署请把 `paths.versionLog` 指向共享目录；单机部署直接用本地目录即可。切换时若数据库里还留有旧的两张表，可 `DROP TABLE` 清理；已应用的单元需要在新状态文件里重建（`apply --mark-applied <change_id>` 或手工编辑 XML），否则会重跑。

**checksum 计算范围：** `checksum = SHA-256(XML 文件原始字节)`。

**checksum 校验策略：**

`apply` 执行时，对已应用的 `change_id` 必须重新校验 checksum：

| 情况                                | 处理                                                             |
| ----------------------------------- | ---------------------------------------------------------------- |
| `change_id` 未应用                  | 正常执行，执行成功后写入 `version_log`（含 checksum）            |
| `change_id` 已应用，checksum 一致   | 跳过（幂等）                                                     |
| `change_id` 已应用，checksum 不一致 | **报错拒绝执行**，提示"已应用的文件不可修改，补丁请新增变更单元" |
| `change_id` 已应用，但文件不存在    | 报错"已应用的变更单元文件丢失"                                   |

**幂等边界（与 6.2 一致）：**

1. 变更单元级：`change_id` 已存在则跳过——这是**唯一的幂等边界**，全部方言生效。
2. 语句级：不生成 `IF NOT EXISTS` 条件块（SQL Server / MySQL 5.7 需要元数据探针，产物无法人工审核）；半态由事务（PG / SQL Server / SQLite 支持事务 DDL）或断点续跑（MySQL，v1.1）解决，见 6.7。

### 6.4 版本日志文件夹

#### 6.4.1 定位

`version_log/` 文件夹是**审计层**，记录每次 apply 实际执行的 SQL 脚本。

| 层     | 位置                                | 作用               |
| ------ | ----------------------------------- | ------------------ |
| 声明层 | 逻辑模型 `release` 属性             | 开发时声明版本归属 |
| 权威层 | `version_log/DbchronicleState.xml`  | 记录已应用的变更   |
| 审计层 | `version_log/{release}_dbupdate.md` | 记录实际执行的 SQL |

#### 6.4.2 目录结构

```
version_log/
├── dev/
│   ├── 00000000_dbupdate.md
│   └── 20261022_dbupdate.md
├── test/
│   └── 20261022_dbupdate.md
└── prod/
    ├── 00000000_dbupdate.md
    └── 20261022_dbupdate.md
```

按 `environment` 分目录，避免多环境冲突。

#### 6.4.3 日志文件格式

`version_log/prod/20261022_dbupdate.md`：

```markdown
# 20261022 数据库版本更新日志

## 2026-10-22 10:00:00 +08:00

- **执行人**：zhangsan
- **数据库**：mysql 8.0 @ localhost:3306/mydb
- **变更单元**：
  - `20261022_100000_p1x2y3` add index
  - `20261022_110000_q4r5s6` add foreign key
- **执行结果**：成功
- **警告**：无
- **SQL 快照**：`sql/20261022/20261022_100000_p1x2y3.mysql.sql`
- **SQL 脚本**：

```sql
-- ChangeUnit: 20261022_100000_p1x2y3
ALTER TABLE `nor_user` ADD INDEX `idx_user_phone` (`phone`);

-- ChangeUnit: 20261022_110000_q4r5s6
ALTER TABLE `nor_order` ADD CONSTRAINT `fk_order_user`
  FOREIGN KEY (`user_id`) REFERENCES `nor_user`(`id`);
```

---

## 2026-10-22 15:30:00 +08:00

- **执行人**：lisi
- ...

```
**同一天多次 apply，追加到同一个文件。**

#### 6.4.4 生成时机与容错

| 步骤 | 优先级 | 失败处理 |
|------|--------|---------|
| 执行 SQL | 第一 | 失败则回滚，不写 version_log 表，不生成日志 |
| 写 version_log 表 | 第一 | 失败则整体失败 |
| 生成日志文件 | 第二 | 失败输出警告，不阻塞；下次 apply 时补生成 |

**补生成逻辑：** `apply` 执行前，检查 `version_log` 表中有记录但日志文件缺失的 release，自动补生成。

#### 6.4.5 SQL 快照与补生成来源

日志条目里的 SQL 来自哪里？**正常 apply** 与**事后补生成**的可信度不同，必须在条目里可区分。

**SQL 快照（apply 时落盘）：**
```

version_log/{environment}/
├── 20261022_dbupdate.md                      # 聚合视图（人读）
└── sql/
    └── 20261022/
        ├── 20261022_100000_a1b2c3.mysql.sql  # 该单元当时实际执行的语句
        └── 20261022_110000_q4r5s6.mysql.sql

```
| 设计点   | 取值                                                        | 理由                                                         |
| -------- | ----------------------------------------------------------- | ------------------------------------------------------------ |
| 存放位置 | 与 md 同级的 `sql/{release}/{change_id}.{dialect}.sql`      | 不把大字段塞进版本表，版本表只留结论与哈希                   |
| 内容     | 实际执行的语句（拆分后逐条，含注释头与警告）                | 审计要能还原"执行了什么"，不是"编译出了什么"                 |
| 写入时机 | 语句执行成功、version_log 写入之后                          | 与 md 同为第二优先级，失败只警告，下次补生成                 |
| 方言后缀 | `.mysql.sql` / `.postgresql.sql` …                          | 同一 change_id 可能在不同方言环境执行，不能互相覆盖          |

**补生成（`--backfill-logs`）的来源优先级：**

| 优先级 | 来源                                                                     | 条目中的标注                                                         |
| ------ | ------------------------------------------------------------------------ | -------------------------------------------------------------------- |
| 1      | `sql/{release}/{change_id}.{dialect}.sql` 快照存在                       | 无标注（与原始条目等价）                                             |
| 2      | 快照缺失，但 XML 存在且 checksum 与版本表一致 → 用当前方言与版本重新编译 | `（补生成：按当前方言重新编译，可能与当时执行的语句存在方言版本差异）` |
| 3      | 快照缺失且 XML 缺失 / checksum 不一致                                    | `（无法补生成：源文件缺失或被修改）`，不报错，`verify` 单独报         |

"重新编译"不是等价替代：dialect 实现升级后同一份 XML 可能生成不同 SQL，因此必须在条目里显式标注，审计读者才知道这段 SQL 是推导值而非执行值。

#### 6.4.6 日志文件夹是否进 Git

| 选择 | 适用 |
|------|------|
| **进 Git（按环境分目录）** | 推荐。审计可追溯，PR 可 review |
| 不进 Git | 简单。数据库是权威，文件只是本地辅助 |

推荐进 Git，`version_log/dev/` 和 `version_log/test/` 可 `.gitignore`，`version_log/prod/` 进 Git。

### 6.5 SQL 语句拆分器

**为什么必须有：** 编译产物是多语句脚本；`RawSql`、视图、触发器、存储过程里还可能出现分号、`$$`、`GO`、`DELIMITER`。拆分错了等于执行错了，而且这类错误只在真库上暴露。

**分层原则：**

| 来源                                          | 拆分方式                                                                     |
| --------------------------------------------- | ---------------------------------------------------------------------------- |
| 编译器生成的结构化 DDL                        | 编译期已知语句边界（每条 operation → 一组语句），直接产出 `SqlStatement`       |
| `<rawSql>` / 视图 / 触发器 / 存储过程          | 文本拆分（方言拆分器），因为编译器不理解过程语言的语法                        |

**各方言拆分规则：**

| 方言            | 语句分隔                                                    | 必须识别的不拆分区域                                                                                                     |
| --------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| MySQL / MariaDB | `;`，并支持 `DELIMITER xxx` 自定义分隔符                     | `'...'`（`''`、`\'` 转义）、`"..."`、`` `...` ``、`-- ` / `#` 行注释、`/* */`、`BEGIN ... END`（存储过程 / 触发器）        |
| PostgreSQL      | `;`                                                          | `'...'`（含 `E'...'`）、`"..."`、`--`、`/* 可嵌套 */`、`$tag$ ... $tag$`（含 `$$`）、`BEGIN ... END`（PL/pgSQL）          |
| SQL Server      | `GO`（独占一行，支持 `GO 3` 计数）                            | `'...'`（含 `N'...'`）、`[...]`、`--`、`/* 可嵌套 */`、`BEGIN ... END`（T-SQL）                                          |
| SQLite          | `;`                                                          | `'...'`、`"..."`、`` `...` ``、`[...]`、`--`、`/* */`、`CREATE TRIGGER ... BEGIN ... END;`（`CASE ... END` 也以 END 结尾，需按 token 栈区分） |

**实现约束：**

- 单遍字符扫描 + 状态机（`Normal` / `SingleQuote` / `DoubleQuote` / `Backtick` / `Bracket` / `LineComment` / `BlockComment` / `DollarQuote` / `DelimiterDirective`），**不用正则切分**——引号内的分号、注释里的关键词都是必须覆盖的场景。
- 维护块深度：`BEGIN` / `CASE` / `IF` / `LOOP` 入栈，`END` 出栈；深度为 0 时遇到分隔符才切分。
- 语句文本保留原始缩进与换行（审计可读）；`Hash` 用「去首尾空白 + 统一 LF」后的文本计算，避免格式化差异导致续跑误判。
- 拆分失败（引号未闭合、块未闭合、`DELIMITER` 未复位）→ 编译期报错，`check` 也报，不进入执行阶段。
- 拆分器只保证边界正确，不解析语法、不做语义校验。

**执行器契约：** `IDbExecutor.Execute(string)` 永远只接收一条语句；`SchemaExecutor` 先调 `ISqlStatementSplitter.Split`，再逐条下发并逐条写入审计日志。

### 6.6 执行互斥（并发 apply）

`apply` 是"读版本表 → 判断待应用 → 执行 → 写版本表"的复合操作。两个进程并发时会同时判定为 pending 并重复执行 DDL；MySQL 无事务 DDL，重复执行直接留下半态。

**锁的作用域：** 从"读取版本表"到"写完全部版本记录"整段，不能只包住执行部分——否则 TOCTOU 依旧存在。

**各方言实现：**

| 方言            | 机制                                                                                          | 说明                                                   |
| --------------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| PostgreSQL      | `pg_try_advisory_lock(key)` / `pg_advisory_unlock(key)`                                        | 会话级，连接断开自动释放                               |
| MySQL / MariaDB | `SELECT GET_LOCK('dbchronicle:{db}', timeout)` / `RELEASE_LOCK`                                | **必须复用同一条连接**（连接池下不能分两次取连接）      |
| SQL Server      | `sp_getapplock @Resource='dbchronicle', @LockMode='Exclusive', @LockOwner='Session'`           | 会话级                                                 |
| SQLite          | 以 `FileShare.None` 独占打开 `<db>.dbchronicle.lock`                                           | 无服务端，用文件锁；进程退出自动释放                   |

**参数与失败语义：**

| 项       | 取值                                                                                     |
| -------- | ---------------------------------------------------------------------------------------- |
| 锁 key   | 库级固定值（不按 release / environment 区分），保证同库同时只有一个 apply                 |
| 等待上限 | `options.lockTimeoutSeconds`，默认 10；`0` = 立即失败                                     |
| 获取失败 | 退出码 `4`，输出"另一进程正在执行 apply（无法获取库级锁）"，不做无意义重试                |
| 释放     | `finally` 中释放，`IDbLock` 实现 `IDisposable`；进程崩溃由数据库会话级锁兜底              |
| 只读命令 | `status` / `verify` 不加锁（只读不破坏一致性），但读到"上次失败"标记时要输出提示          |

**边界（明确不做）：**

- 不防 `apply` 与业务 DML 的竞争：DDL 会取元数据锁，MySQL 上大表 `ALTER` 可能阻塞业务。一期只做"提示 + 建议低峰执行"，不集成 gh-ost / pt-online-schema-change。
- 不做多节点协调：锁由目标数据库提供，应用侧不引入协调服务（与"不做独立服务"的非目标一致）。

### 6.7 半态与恢复

**什么是半态：** 无事务 DDL 的方言（MySQL / MariaDB）中，一个 changeUnit 的多条语句中途失败——前若干条已生效、`version_log` 无记录。此时重跑会把已生效语句再执行一次（`ADD COLUMN` 报"列已存在"），`status` 也看不出"到底执行到哪一条"。

**识别：**

| 场景                       | 判定                                     | 输出                                                       |
| -------------------------- | ---------------------------------------- | ---------------------------------------------------------- |
| 版本表无记录但结构已部分变更 | 执行期报错中止即为信号                   | 错误信息附「已成功 N 条 / 共 M 条」与失败语句原文           |
| 事后检查                   | 依赖版本表的 `failure_at` / `failure_stmt` 列 | `status` 列出「上次失败」标记 + 失败语句序号，人工核对结构  |

**版本表为此预留两列（一期就建，避免后续加列）：**

```sql
failure_at    DATETIME,     -- 最近一次失败时间，成功执行后清空
failure_stmt  INT           -- 失败语句序号（0 基），成功执行后清空
```

**一期处置流程（人工，CLI 直接提示）：**

```
① apply 失败 → 输出：失败语句序号、已成功条数、SQL 原文、备份建议
② 人工核对数据库当前结构（SHOW CREATE TABLE / \d+）
③ 二选一修正：
   a) 按输出的 SQL 手工补齐剩余语句
   b) 手工回退已执行语句，再重跑 apply
④ 结构确认正确后：apply --mark-applied <change_id>
```

`--mark-applied` 是必要的逃生门：结构已被人工改对时，不应强迫重跑。

**语句级断点续跑（已实现，记录在本地状态文件）：**

```xml
<applied changeId="20261022_100000_a1b2c3" …>
  <statements>
    <statement index="0" hash="…" appliedAt="2026-10-23 09:53:07" />
    <statement index="1" hash="…" appliedAt="2026-10-23 09:53:08" />
  </statements>
</applied>
```

| 规则       | 说明                                                                                                          |
| ---------- | ------------------------------------------------------------------------------------------------------------- |
| 写入时机   | 每条语句成功后立即写入状态文件（原子替换）                                                                    |
| 续跑判定   | 按 `index` 跳过已记录语句，并比对 `hash` 与本次编译结果                                                       |
| 哈希一致   | 从第一个未记录的语句继续                                                                                      |
| 哈希不一致 | 文件被改（checksum 已先拦）或 dialect / options 变化 → 拒绝续跑，提示 `--restart`（人工确认后清空该单元记录） |
| 清理       | 单元全部成功后移除该单元的 `<statements>` 节点，避免文件膨胀                                                  |

---

## 七、第三方依赖与许可证合规

**硬性要求：所有引用到的第三方库必须是开源免费、允许商用的许可证。**

### 7.1 依赖清单与许可证

| 依赖库                       | 用途                                             | 许可证                 | 允许商用 | 说明                                                  |
| ---------------------------- | ------------------------------------------------ | ---------------------- | -------- | ----------------------------------------------------- |
| **YamlDotNet**               | 解析 `DbchronicleConfig.xml` 和方言类型映射 YAML | **MIT**                | ✅ 是     | 宽松许可证，可自由使用、修改、分发                    |
| **System.CommandLine**       | CLI 命令行解析                                   | **MIT**                | ✅ 是     | .NET Foundation 项目                                  |
| **MySqlConnector**           | MySQL 原生驱动                                   | **MIT**                | ✅ 是     | 比 Oracle 的 `MySql.Data`（GPL 2.0 或商业许可）更友好 |
| **Npgsql**                   | PostgreSQL 原生驱动                              | **PostgreSQL License** | ✅ 是     | OSI 批准的宽松许可证                                  |
| **Microsoft.Data.SqlClient** | SQL Server 原生驱动                              | **MIT**                | ✅ 是     | .NET Foundation 项目                                  |
| **Microsoft.Data.Sqlite**    | SQLite 原生驱动                                  | **MIT**                | ✅ 是     | .NET Foundation 项目                                  |
| **xUnit.net**                | 单元测试框架                                     | **Apache 2.0**         | ✅ 是     | OSI 批准，允许商业使用                                |

### 7.2 不引入的依赖

| 依赖                     | 原因                                                                      |
| ------------------------ | ------------------------------------------------------------------------- |
| **EF Core**              | 引入它违背"零 ORM 依赖"的核心设计                                         |
| **MySql.Data（Oracle）** | GPL 2.0 with Universal FOSS Exception，商业闭源产品可能需要购买商业许可证 |
| **任何 GPL / AGPL 库**   | 可能要求衍生作品开源                                                      |

### 7.3 合规检查机制

在 CI 中增加许可证扫描步骤：

```powershell
dotnet list package --include-transitive
```

结合 `dotnet-project-licenses` 等工具自动检查传递依赖的许可证，确保所有引入的包均为 MIT / Apache 2.0 / PostgreSQL / Public Domain 等宽松许可证。任何出现 GPL / AGPL / 商业许可的包，CI 直接失败。

---

## 八、命令行工具（CLI）设计

### 8.1 命令

```powershell
# 安装（一次性）
dotnet tool install -g DbChronicle.Cli

# 生成变更单元（不覆盖同名文件，自动生成文件名、ID、作者、release）
db-chronicle new --desc "add phone to nor_user"

# 可选：显式指定 release
db-chronicle new --desc "add phone to nor_user" --release 20261022

# 可选：显式指定作者
db-chronicle new --desc "add phone to nor_user" --author "zhangsan"

# 编译为 SQL（不连接数据库）
db-chronicle compile --config DbchronicleConfig.xml

# 应用到数据库（checksum 强校验，按 (release, change_id) 排序执行）
db-chronicle apply --config DbchronicleConfig.xml

# 执行前 dry-run（检测冲突，输出 SQL 但不执行）
db-chronicle apply --config DbchronicleConfig.xml --dry-run

# 补生成缺失的日志文件
db-chronicle apply --config DbchronicleConfig.xml --backfill-logs

# 结构已由人工修正，把半态单元标记为已应用（不执行 SQL，见 6.7）
db-chronicle apply --config DbchronicleConfig.xml --mark-applied 20261022_100000_a1b2c3

# 查看版本托管状态（显示数据库当前版本、待应用版本）
db-chronicle status --config DbchronicleConfig.xml

# 按 release 查询
db-chronicle status --config DbchronicleConfig.xml --release 20261022

# 检测所有变更单元之间的语义冲突（不连接数据库）
db-chronicle check --config DbchronicleConfig.xml

# 校验已应用文件的 checksum（连接数据库，只读）
db-chronicle verify --config DbchronicleConfig.xml

# 从 C# 实体生成种子逻辑模型（草案，一期可选，仅支持程序集反射）
db-chronicle scan --assembly ./bin/App.dll --out migrations/00000000_000000_baseline.xml

# 从 C# 源码生成种子逻辑模型（草案，二期，Roslyn）
db-chronicle scan --source ./src --out migrations/00000000_000000_baseline.xml

# 从现有数据库生成种子逻辑模型（草案，二期）
db-chronicle scan-db --dialect mysql --out migrations/00000000_000000_baseline.xml

# 初始化配置
db-chronicle init

# 初始化配置并指定方言（默认 mysql 8.0）
db-chronicle init --dialect sqlite
db-chronicle init --dialect postgresql --version 16
```

**命令表：**

| 命令                      | 是否连接数据库      | 说明                             |
| ------------------------- | ------------------- | -------------------------------- |
| `init`                    | 否                  | 初始化配置                       |
| `new`                     | 否                  | 生成变更单元，**不覆盖同名文件** |
| `compile`                 | 否                  | 编译为 SQL                       |
| `apply`                   | 是                  | 应用变更，**checksum 强校验**    |
| `apply --dry-run`         | 是（只读）          | 检测冲突，输出 SQL 但不执行      |
| `apply --backfill-logs`   | 是（只读 + 写文件） | 补生成缺失的日志文件             |
| `apply --mark-applied`    | 是（写版本表）      | 半态人工修正后标记为已应用       |
| `status`                  | 是                  | 查看版本托管状态                 |
| `status --release`        | 是                  | 按 release 查询                  |
| `check`                   | 否                  | 检测语义冲突                     |
| `check --strict-warnings` | 否                  | 编译警告升级为失败（CI 门禁）    |
| `verify`                  | 是（只读）          | 校验已应用文件 checksum          |
| `scan`                    | 否                  | 从 C# 实体生成基线草案           |
| `scan-db`                 | 是（只读）          | 从现有数据库生成基线草案（二期） |

### 8.2 status 输出

**首次接入（基线未应用）：**

```
数据库当前版本：无（未初始化）

待应用：
[ ] 00000000_000000_baseline  baseline            00000000  pending
[ ] 20261022_100000_a1b2c3    add phone           20261022  pending

提示：首次接入需先执行 `db-chronicle apply` 应用基线。
```

**基线已应用，有新的变更待应用：**

```
数据库当前版本：20261021
待应用版本：20261022

已应用：
[x] 00000000_000000_baseline  00000000  applied 2026-10-01 09:00 by scanner
[x] 20261021_100000_a1b2c3    20261021  applied 2026-10-21 10:05 by zhangsan
[x] 20261021_110000_d4e5f6    20261021  applied 2026-10-21 10:10 by zhangsan

待应用：
[ ] 20261022_100000_p1x2y3    20261022  pending
[ ] 20261022_110000_q4r5s6    20261022  pending

提示：执行 `db-chronicle apply` 应用 2 个待应用的变更单元，数据库版本将从 20261021 升级到 20261022。
```

**全部已应用：**

```
数据库当前版本：20261022
待应用版本：无

已应用：
[x] 00000000_000000_baseline  00000000  applied 2026-10-01 09:00 by scanner
[x] 20261021_100000_a1b2c3    20261021  applied 2026-10-21 10:05 by zhangsan
[x] 20261021_110000_d4e5f6    20261021  applied 2026-10-21 10:10 by zhangsan
[x] 20261022_100000_p1x2y3    20261022  applied 2026-10-22 10:00 by zhangsan
[x] 20261022_110000_q4r5s6    20261022  applied 2026-10-22 10:01 by zhangsan

数据库已是最新版本。
```

### 8.3 发布形态

| 形态        | 命令                                     | 适用                      |
| ----------- | ---------------------------------------- | ------------------------- |
| Global Tool | `dotnet tool install -g DbChronicle.Cli` | 开发者                    |
| 自包含 exe  | `dotnet publish --self-contained`        | DBA / 服务器 / 跨语言调用 |

### 8.4 退出码

CI 靠退出码判断成败，不能依赖 stdout 文本。所有命令统一使用下表（一期实现）：

| 码  | 含义                                                                    | 典型命令         |
| --- | ----------------------------------------------------------------------- | ---------------- |
| 0   | 成功；`status` 表示"数据库已是最新"                                     | 全部             |
| 1   | 参数错误 / 前置校验失败（文件名不合规、`release` 格式错、缺 `depends`） | 全部             |
| 2   | 有待应用变更（仅 `status` 使用，用于 CI 判断"是否需要跑 apply"）        | `status`         |
| 3   | 执行失败 / 校验失败（SQL 报错、checksum 不一致、文件丢失）              | `apply` `verify` |
| 4   | 无法获取库级互斥锁（另一进程正在 apply）                                | `apply`          |
| 5   | 环境问题（连接失败、非交互环境缺 `DBCHRONICLE_CONN`）                   | 需要连接的命令   |

**约定：** `apply` 在任何非 0 退出前都不得写入 version_log 记录（有事务方言回滚，无事务方言保持"半态 + `failure_at` 标记"）。

---

## 九、项目结构

```
DbChronicle/
├── src/
│   ├── DbChronicle/                      # 核心库（NuGet: DbChronicle，零第三方依赖）
│   │   ├── Abstractions/                 # 契约层（不依赖实现）
│   │   │   ├── Execution/                # IDbExecutor / IDbExecutorFactory / IDbLock
│   │   │   ├── Diagnostics/              # CompileDiagnostic / DiagnosticCodes
│   │   │   └── Versioning/               # AppliedChange
│   │   ├── Domain/                       # 领域层（纯模型，零 IO）
│   │   │   ├── Model/                    # ChangeUnit
│   │   │   ├── Operations/               # SchemaOperation 及各子类
│   │   │   ├── Structures/               # StructureModel / StructureModelBuilder / TableModel
│   │   │   └── Sql/                      # SqlStatement
│   │   ├── Compilation/                  # 编译层
│   │   │   ├── Compiler/                 # SchemaCompiler / CompilerOptions / CompileResult
│   │   │   ├── Dialects/                 # 五方言 + TypeMapper + DialectFactory
│   │   │   ├── Splitting/                # 语句拆分器
│   │   │   └── Checking/                 # 语义冲突检测
│   │   ├── Parsing/                      # 解析层：XML → Domain
│   │   ├── Runtime/                      # 运行时层（需要数据库/文件系统）
│   │   │   ├── Execution/                # SchemaExecutor / DbChronicleMigrator / 漂移比对
│   │   │   ├── Versioning/               # 状态文件 / 审计日志
│   │   │   ├── Rollback/                 # 回滚规划与执行
│   │   │   └── Naming/                   # 文件命名
│   │   ├── Scanning/                     # C# 实体反向扫描（纯反射）
│   │   ├── Configuration/                # 配置模型与加载
│   │   ├── Api/                          # IDbChronicle / DbChronicleFactory / Options / Result
│   │   ├── Resources/dialects/           # 五方言类型映射（嵌入资源）
│   │   └── DbChronicle.csproj
│   │
│   ├── DbChronicle.Cli/                  # 命令行（dotnet tool: DbChronicle.Cli）
│   │   ├── AdoNet/                       # ADO.NET 执行器 + 库级锁（唯一带驱动的地方）
│   │   ├── Commands.cs / Program.cs
│   │   └── DbChronicle.Cli.csproj
│   │
│   └── （二期/三期未创建：DbChronicle.Executor.EfCore、DbChronicle.Scanner.Database、DbChronicle.CodeGen.CSharp）
│
├── DbChronicle/Resources/dialects/       # 类型映射 XML（嵌入资源）
│   ├── mysql.types.xml
│   ├── mariadb.types.xml
│   ├── postgresql.types.xml
│   ├── sqlserver.types.xml
│   └── sqlite.types.xml
│
├── test/                                 # 测试与示例（不随核心库发布）
│   ├── samples/                          # 示例工程（config + migrations）
│   │   ├── migrations/
│   │   ├── version_log/
│   │   └── DbchronicleConfig.xml
│   ├── DbChronicle.IntegrationTests/     # 集成测试（SQLite 真跑，真库按环境变量启用）
│   └── DbChronicle.Demo/                 # 可运行演示
│
└── DbChronicle.sln
```

**项目形态与依赖关系：**

| 项目            | 形态 | 依赖                                                                                       | 许可证     |
| --------------- | ---- | ------------------------------------------------------------------------------------------ | ---------- |
| Core            | 库   | YamlDotNet                                                                                 | MIT        |
| Cli             | 工具 | Core + ADO.NET 执行器 + System.CommandLine                                                 | MIT        |
| Scanner.CSharp  | 库   | Core                                                                                       | MIT        |
| Executor.AdoNet | 库   | MySqlConnector (MIT) / Npgsql (PostgreSQL) / SqlClient (MIT) / Microsoft.Data.Sqlite (MIT) | ✅          |
| Core.Tests      | 测试 | xUnit.net                                                                                  | Apache 2.0 |

**核心库零数据库驱动依赖。**

---

## 十、部署形态

### 10.1 形态一：纯 CLI（推荐默认）

使用 `DbChronicle.Cli` 工具，独立部署。

```
db-chronicle.exe + DbchronicleConfig.xml + migrations/ + version_log/
```

- 独立部署，不依赖应用。
- DBA、CI、开发都用这一个工具。
- **开发机和 DBA 手动执行时，连接信息通过 CLI 交互输入。**
- **CI/CD 环境通过 `DBCHRONICLE_CONN` 环境变量注入。**
- **任何语言的项目通过 exec 子进程调用，不需要 .NET 运行时（自包含 exe）。**

### 10.2 形态二：应用内嵌（C# 项目，可选）

应用引用 `DbChronicle` 库，注入执行器。

```csharp
var connectionString = configuration.GetConnectionString("Default");
var dialect = DialectFactory.Create(config.Database.Dialect, config.Database.Version);
var executor = new SchemaExecutor(new AdoNetExecutorFactory(), dialect);
executor.ApplyPending(units, connectionString);
```

- 适合桌面应用、单体部署。
- 建议只用于开发/测试环境。
- **连接信息由宿主应用管理**，核心库不读取环境变量，也不触发交互式输入。

### 10.3 形态三：CI/CD 专用

CI 流水线调用 `DbChronicle.Cli` 工具，连接信息通过 Secrets 注入环境变量。

```yaml
- name: Compile SQL
  run: db-chronicle compile --config DbchronicleConfig.xml

- name: Check conflicts
  run: db-chronicle check --config DbchronicleConfig.xml

- name: Review SQL
  run: cat ./output/mysql.sql

- name: Verify applied checksums
  env:
    DBCHRONICLE_CONN: ${{ secrets.DB_CONNECTION }}
  run: db-chronicle verify --config DbchronicleConfig.xml

- name: Apply SQL
  env:
    DBCHRONICLE_CONN: ${{ secrets.DB_CONNECTION }}
  run: db-chronicle apply --config DbchronicleConfig.xml
```

- 编译、检查、执行分离，SQL 可人工审核。
- 生产环境推荐。
- **非交互环境必须通过环境变量提供连接信息**，否则 `apply` 报错退出。

---

## 十一、开发路线

### 11.1 第一版（MVP，1~2 周）

- [x] 工具集配置加载 + 默认配置生成
- [x] 变更单元 XML 解析（含 `release` 属性校验）
- [x] 操作模型（列 + 索引 + 外键 + 主键 + 唯一约束）
- [x] 逻辑类型 20 种 + 类型映射（MySQL）
- [x] MySQL 方言
- [x] 编译器主流程
- [x] ADO.NET 执行器
- [x] 版本托管表 + 幂等 + 自动创建
- [x] `apply` 按 `(release, change_id)` 排序执行
- [x] `version_log/{release}_dbupdate.md` 生成 + 追加模式
- [x] `status` 显示数据库当前版本、待应用版本
- [x] `status --release` 按版本查询
- [x] `new` 命令自动生成唯一文件名 + 自动填充作者
- [x] `new` 命令使用 `FileMode.CreateNew`，绝不覆盖同名文件
- [x] checksum 强校验（已应用文件被修改时拒绝执行）
- [x] `check` 命令（语义冲突检测 + 文件名正则校验）
- [x] `verify` 命令（已应用文件 checksum 校验）
- [x] `apply --dry-run`（检测冲突，输出 SQL）
- [x] `apply --backfill-logs`（补生成缺失日志）
- [x] 文件头注释模板
- [x] 连接信息交互式输入 + 环境变量
- [x] 基线冻结机制
- [x] Core 库公开 `ChangeUnitFactory.Create()` API
- [x] Pre-commit 钩子脚本
- [x] IDE 模板（VS Code / Rider）
- [x] 方言感知的 SQL 语句拆分器（引号 / 注释 / 块 / `DELIMITER` / `GO` / `$$`）
- [x] 库级互斥锁（PG 咨询锁 / MySQL `GET_LOCK` / SQL Server `sp_getapplock` / SQLite 文件锁）
- [x] SQL 快照落盘 + 补生成来源分级
- [x] 编译诊断码 W001~W013 + `check --strict-warnings`
- [x] 半态识别（`failure_at` / `failure_stmt`）+ `apply --mark-applied`
- [x] CLI 退出码规范化（0~5）
- [x] 累积模型（回放 `migrations/` 推导表结构状态，见 5.10）
- [x] `options.incrementalModify` 增量模式（`MODIFY` 只写变化项，编译器补全）

**目标：跑通 MySQL 单库端到端流程。**

### 11.2 第二版（2~3 周）

- [x] PostgreSQL 方言
- [x] MariaDB 方言（复用 MySQL + Json CHECK 追加）
- [x] SQL Server 方言（默认值约束命名规则）
- [x] SQLite 方言（rebuild 策略）
- [x] 检查约束 + 默认值约束
- [x] 表重命名 + 表注释
- [x] 数据迁移（`RawSqlOperation` 按方言）
- [x] 种子数据（`SEED` 节点）
- [x] 多方言编译输出
- [x] `unsigned` 属性
- [x] `onUpdate` 属性
- [x] `Scanner.CSharp`（程序集反射）
- [x] 语句级断点续跑（记录在 `DbchronicleState.xml` 的 `<statements>`，见 6.7）
- [x] `apply` 期累积模型漂移复核（introspect 真实列定义并比对，见 5.10）
- [x] 按 release 回滚（`rollback --to-release`）
- [x] 数据操作节点（`<update>` / `<delete>` / `<clean>` / `<backfill>`，见 4.1.10）
- [x] 列级 `charset` / `collate`、列内联 `foreignKey`、`FULLTEXT` 索引
- [ ] IDE 插件（右键菜单生成）

### 11.3 第三版（按需）

- [ ] `Scanner.Database`（数据库反向工程）
- [ ] `CodeGen.CSharp`（代码生成）
- [ ] `Scanner.Java` / `Scanner.Go` / `Scanner.TypeScript`
- [ ] `scan --source`（Roslyn 源码扫描）
- [x] 视图 / 序列 / 触发器（XML 节点映射为 `RawSqlOperation`，按方言写 SQL）
- [x] 冲突检测增强（RENAME 冲突、ADD/DROP 顺序、DROP 不存在列、补丁缺 `depends`）
- [x] CI 集成（`status` 退出码：0 最新 / 2 有待应用）
- [x] 累积模型（基线 + 变更链推导，已在一期实现，见 5.10）

---

## 十二、风险与对策

| 风险                             | 对策                                                                       |
| -------------------------------- | -------------------------------------------------------------------------- |
| **文件名相同导致覆盖**           | `FileMode.CreateNew` 绝不覆盖；Git 同名冲突保护；`new` 命令重试            |
| **补丁直接修改已应用文件**       | checksum 强校验，不一致报错拒绝执行；CI `verify` 门禁                      |
| **补丁未用 depends 声明依赖**    | `check` 命令检测，CI 门禁；`new` 命令模板提示                              |
| **两个补丁针对同一列冲突**       | `check` 命令检测修改目标冲突，CI 门禁                                      |
| **已应用文件丢失**               | `verify` 命令检测，报错                                                    |
| **开发者手动命名文件**           | `check` 命令正则校验，CI 门禁；Pre-commit 钩子本地拦截；IDE 模板提示       |
| **release 写错日期**             | `check` 校验格式、未来日期、与 change_id 日期一致性                        |
| **数据库已更新，日志文件未生成** | 日志生成失败不阻塞；下次 apply 补生成；`--backfill-logs` 手动补            |
| **日志文件存在，数据库无记录**   | 以数据库为准，`status` 输出警告                                            |
| **多环境日志冲突**               | 按 `environment` 分目录                                                    |
| **同一 release 跨天提交**        | 允许，按 `(release, change_id)` 排序，`check` 输出警告                     |
| **数据库回退**                   | 一期手工处理，二期考虑 `rollback` 命令                                     |
| 并行开发语义冲突（重复 ADD）     | `check` 命令检测，CI 门禁                                                  |
| 执行时列已存在                   | 变更单元级幂等跳过；MySQL 半态按 6.7 处置（不生成条件块）                  |
| DDL 不能事务回滚（MySQL）        | 执行前备份，破坏性操作二次确认                                             |
| 类型变更丢数据                   | `MODIFY` 前检查数据兼容性，提供 `dataMigration`                            |
| 重命名误判为删+增                | 显式 `RENAME` 操作 + `renamedFrom`                                         |
| 方言边界遗漏                     | 每个方言单元测试覆盖所有操作                                               |
| 版本表被篡改                     | `checksum` 校验变更单元内容                                                |
| SQL 拆分错误                     | 方言感知的语句拆分器                                                       |
| 执行器方言不一致                 | 启动时校验 `executor.DialectName == dialect.Name`                          |
| 许可证合规                       | CI 扫描所有传递依赖，仅允许 MIT / Apache 2.0 / PostgreSQL / Public Domain  |
| 连接信息泄露                     | 不落盘、不进历史、掩码输入；CI 通过 Secrets 注入环境变量；日志中连接串脱敏 |
| CI 环境交互阻塞                  | `Console.IsInputRedirected` 检测，非交互环境跳过交互，直接检查环境变量     |
| DateTimeOffset 在 MySQL 语义偏差 | 编译时输出警告，建议用 `DateTime` + 应用层 UTC 约定                        |
| SQLite rebuild 丢数据            | 执行前备份，rebuild 前校验数据完整性                                       |
| 非 C# 项目基线生成困难           | 一期手写 XML，二期提供 `Scanner.Database`                                  |
| 无符号整数跨库不一致             | 仅 MySQL/MariaDB 生效，其他方言忽略并警告                                  |
| MariaDB JSON 无原生类型          | 映射为 `LONGTEXT` + `CHECK (JSON_VALID(col))`                              |
| SQLite 时间格式不一致            | 编译器输出注释，格式一致性由应用层负责                                     |
| SQL Server 约束名超长            | 截断并追加 8 位哈希                                                        |

**补充风险（执行与审计）：**

| 风险                                 | 对策                                                                                               |
| ------------------------------------ | -------------------------------------------------------------------------------------------------- |
| 并发 apply 重复执行 DDL              | 库级互斥锁（6.6）：PG 咨询锁 / MySQL `GET_LOCK` / SQL Server `sp_getapplock` / SQLite 文件锁       |
| MySQL 执行中途失败留半态             | 版本表记 `failure_at` / `failure_stmt`；一期人工处置 + `apply --mark-applied`；v1.1 语句级续跑     |
| SQL 语句拆分错误                     | 方言感知单遍状态机（6.5），引号 / 注释 / 块 / `DELIMITER` / `GO` / `$$` 感知；拆分失败在编译期报错 |
| 换行 / BOM 导致 checksum 漂移        | `.gitattributes` 的 `-text`、UTF-8 无 BOM + LF、`check` 检测、`verify` 二次判定（4.6.3）           |
| 用户声明被方言静默丢弃               | 统一诊断码 W001~W013，四处传播（CLI / SQL 文件头 / 审计 md / `check --strict-warnings`）           |
| `MODIFY` 误清默认值                  | full 模式全量重述 + W008 提示；`incrementalModify: true` 由累积模型补全未写属性（5.10）            |
| 新建表与索引 / 外键顺序颠倒          | 依赖顺序固定「建表最先、外键最后」（4.2），`check` 报跨单元倒置                                    |
| 锁被长时间占用（大表 ALTER）         | `lockTimeoutSeconds` 超时退出（退出码 4），提示低峰执行；不集成在线 DDL 工具                       |
| 增量模式补全错误（变更链与库不一致） | 累积模型只回放 XML，不猜结构；v1.1 由 `apply` introspect 复核                                      |
| 补全基底缺失导致属性未继承           | 按列退化 + W014 告警 + SQL 注释头标注，语义在 4.5.5 明确写清                                       |

---

## 十三、关键设计决策

| 决策点                    | 选择                                     | 理由                                                                                       |
| ------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------ |
| 是否用 ORM 方言           | **不用**                                 | 自实现 200~300 行/方言，零依赖，完全可控                                                   |
| 数据库连接                | 原生驱动                                 | MySqlConnector (MIT) / Npgsql (PostgreSQL) / SqlClient (MIT) / Microsoft.Data.Sqlite (MIT) |
| 版本记录                  | 数据库表，首次 apply 自动创建            | 幂等、可审计、多环境一致                                                                   |
| 版本号来源                | 逻辑模型 `release` 属性                  | 变更的自然属性，开发时声明                                                                 |
| 版本号格式                | `yyyyMMdd`                               | 精确到天，简单直观                                                                         |
| 版本号权威来源            | 数据库 `MAX(release)`                    | 实际应用的版本为准                                                                         |
| config 是否维护版本号     | 否                                       | 避免手动维护，消除 config 与数据库不一致                                                   |
| 日志文件夹定位            | 审计层                                   | 记录实际执行的 SQL                                                                         |
| 日志文件夹路径            | `version_log/${environment}`             | 按环境隔离                                                                                 |
| 日志文件格式              | Markdown，按天聚合，追加模式             | 人类可读，同一天多次更新追加                                                               |
| 日志生成容错              | 失败不阻塞，下次补生成                   | 数据库状态优先                                                                             |
| 日志是否进 Git            | 推荐进（prod 必进）                      | 审计可追溯                                                                                 |
| checksum                  | SHA-256(XML 文件原始字节)                | 简单、可验证、防篡改                                                                       |
| 文件名                    | 时间戳 + 随机码 + 描述                   | 并行不冲突                                                                                 |
| **文件写入策略**          | `FileMode.CreateNew`                     | 文件已存在时抛异常，绝不覆盖                                                               |
| **已应用文件修改**        | checksum 强校验，不一致拒绝执行          | 补丁必须新增文件，不能改历史                                                               |
| **补丁依赖**              | 强制 `depends`                           | 明确补丁与原始变更的关系                                                                   |
| **语义冲突检测**          | `check` 命令 + CI 门禁                   | 跨文件冲突在合并前发现                                                                     |
| **已应用文件校验**        | `verify` 命令 + CI 门禁                  | 检测文件是否被修改或丢失                                                                   |
| 排序                      | `(release, change_id)` 升序              | 版本优先，同版本按时间                                                                     |
| 逻辑模型                  | 增量变更单元 XML                         | 不维护全量快照                                                                             |
| 执行层                    | 接口注入                                 | 核心库零数据库依赖，可插拔                                                                 |
| 发布形态                  | 库 + 工具                                | 能力复用 + 命令行入口                                                                      |
| 核心定位                  | 配置驱动的方言编译器                     | 执行是可选出口                                                                             |
| 许可证策略                | 仅宽松许可证                             | 确保商业闭源产品可合规使用                                                                 |
| 连接信息获取              | CLI 交互输入 + 环境变量兜底              | 交互式覆盖开发机/DBA 手动场景，密码不落盘；环境变量覆盖 CI/CD 非交互场景                   |
| 逻辑类型命名              | 位数明确 + 语义明确                      | `Int32` 而非 `int` 或 `Integer`，语言中立                                                  |
| 语言映射                  | 服务 Scanner/CodeGen，Core 不依赖        | 职责边界清晰                                                                               |
| 跨语言集成                | C# 用 NuGet，其他语言 exec CLI           | 标准做法，与 Flyway/Liquibase 一致                                                         |
| 无符号整数                | 属性而非逻辑类型                         | 仅 MySQL 原生支持，避免跨库语义不一致                                                      |
| onUpdate                  | 属性而非逻辑类型                         | 仅 MySQL 原生支持，其他方言警告                                                            |
| SQLite 策略               | rebuild 为主                             | DDL 能力有限，降级处理                                                                     |
| MariaDB JSON              | LONGTEXT + CHECK                         | MariaDB 无原生 JSON，CHECK 保证语义                                                        |
| SQL Server 约束名         | `DF_{table}_{column}` + 超长哈希         | 可读、可预测、防超长                                                                       |
| FixedString/String length | Parser 强制必填                          | 避免默认值歧义                                                                             |
| Blob length               | 忽略 + 警告                              | Blob 无长度语义                                                                            |
| 时间格式一致性（SQLite）  | 编译器注释 + 应用层负责                  | DDL 层无法强制                                                                             |
| scan 命令                 | 一期只支持 `--assembly`                  | Roslyn 源码扫描二期                                                                        |
| status 基线未应用         | 显示 pending + 提示                      | 首次接入体验清晰                                                                           |
| **变更单元生成时机**      | 开发阶段，非编译阶段                     | 和 `dotnet new`、`dotnet ef migrations add` 一致                                           |
| **C# 宿主项目的生成方式** | 仍用 CLI `new` 命令                      | CLI 是全局 dotnet tool，IDE 终端直接调用                                                   |
| **编程式生成**            | Core 库公开 `ChangeUnitFactory.Create()` | 供 CI、内部工具、IDE 插件使用                                                              |
| **编译时校验**            | Pre-commit 钩子 + CI `check` 门禁        | 比 MSBuild Target 更早、更轻量                                                             |
| **IDE 插件**              | 二期                                     | 最佳体验，但非必须                                                                         |

**补充决策（执行 / 幂等 / 审计）：**

| 决策点          | 选择                                                | 理由                                                                                 |
| --------------- | --------------------------------------------------- | ------------------------------------------------------------------------------------ |
| 幂等边界        | 只做变更单元级，不生成语句级 `IF NOT EXISTS` 条件块 | 条件块需方言元数据探针，产物不可人工审核；半态用事务或续跑解决                       |
| 排序            | `CREATE TABLE` 最先，`ADD FOREIGN KEY` 最后         | 否则「建表 + 索引 / 外键」必然失败；跨单元只按 `(release, change_id)`                |
| 数据操作时机    | `stage="before" / "after"` 显式声明                 | 收紧 NOT NULL / CHECK 前通常需要先回填                                               |
| 执行互斥        | 用数据库原生锁（SQLite 用文件锁），不引入协调服务   | 与"不做独立服务"一致；进程崩溃由数据库会话释放兜底                                   |
| 审计 SQL 来源   | 落盘快照 + 补生成分级标注，不塞进版本表             | 版本表保持轻量；"重新编译"不是"实际执行"，必须可区分                                 |
| 语句拆分        | 编译器产出结构化语句，文本拆分只服务 `RawSql`       | 过程语言无法在编译器内解析；拆分错误只在真库暴露                                     |
| checksum 规范化 | 不做规范化，另用 `verify` 二次判定区分字节漂移      | 审计真实性依赖"哈希 = 当时字节"，规范化会掩盖实质修改                                |
| `MODIFY` 语义   | 默认 incremental（只写变化项）                      | MySQL `MODIFY COLUMN` 是整列替换，故增量写法必须由编译器补全；写全的历史文件结果不变 |
| 增量补全来源    | 离线累积模型（回放 `migrations/`）                  | 保持 `compile` 纯离线；库漂移由 `apply` introspect 复核（v1.1）                      |
| 补全不可用      | 按列退化为当前声明 + W014，不阻断编译               | 新项目 / 外部表必须能编译；补不到就失败会把可用性拖死                                |

---

## 十四、核心价值

本节描述的是 DbChronicle 工具集的整体价值，其中「零 ORM 依赖」特指核心库。

1. **逻辑模型与数据库解耦**：一份 XML，五方言输出。
2. **版本托管可追溯**：版本号由逻辑模型声明，数据库记录权威状态，`version_log/` 文件夹留档 SQL 审计日志。
3. **并行开发友好**：文件名唯一，独立文件，Git 不冲突。
4. **零 ORM 依赖**：核心库不引用任何 ORM，执行可插拔。
5. **可测试**：编译器纯函数，Mock 执行器秒级测试。
6. **可扩展**：新增数据库 = 一份 YAML + 一个方言类。
7. **离线编译**：不连数据库也能生成 SQL，CI 友好。
8. **许可证合规**：所有第三方依赖均为 MIT / Apache 2.0 / PostgreSQL / Public Domain，允许商业闭源使用。
9. **跨语言兼容**：C# 项目引用 NuGet 进程内调用，其他语言 exec CLI 跨进程调用。
10. **存量项目平滑接入**：反向扫描生成种子逻辑模型，基线冻结后只写增量。
11. **文件唯一性四层防护**：生成层 `CreateNew`、版本控制层 Git 冲突保护、执行层 checksum 强校验、CI 层 `check` + `verify` 门禁，彻底规避文件名相同导致的覆盖。
12. **补丁安全**：已应用文件不可修改，checksum 不一致拒绝执行，补丁必须新增文件 + `depends` 声明。

---

## 十五、附录

### 附录 A：完整示例

#### A.1 输入

**DbchronicleConfig.xml：**

```yaml
database:
  dialect: mysql
  version: "8.0"

environment: prod

paths:
  migrations: ./migrations
  version_log: ./version_log/prod
  output: ./output

baseline:
  file: migrations/00000000_000000_baseline.xml
  frozen: true
```

**migrations/20261022_100000_a1b2c3_add_phone_to_user.xml：**

```xml
<changeUnit id="20261022_100000_a1b2c3" author="zhangsan" release="20261022">
  <description>用户表增加手机号</description>
  <table name="nor_user" action="ALTER">
    <column name="phone" action="ADD" type="String" length="20"
            nullable="true" comment="手机号"/>
    <index name="idx_user_phone" action="ADD" unique="true">
      <columnRef name="phone"/>
    </index>
  </table>
</changeUnit>
```

#### A.2 输出（MySQL）

**编译产物 `output/mysql.sql`：**

```sql
-- ChangeUnit: 20261022_100000_a1b2c3
-- Author: zhangsan
-- Release: 20261022
-- Description: 用户表增加手机号
-- Dialect: mysql 8.0

ALTER TABLE `nor_user` ADD COLUMN `phone` VARCHAR(20) NULL COMMENT '手机号';

CREATE UNIQUE INDEX `idx_user_phone` ON `nor_user` (`phone`);
```

**执行后 `version_log/prod/20261022_dbupdate.md`：**

```markdown
# 20261022 数据库版本更新日志

## 2026-10-22 10:00:00 +08:00

- **执行人**：zhangsan
- **数据库**：mysql 8.0 @ localhost:3306/mydb
- **变更单元**：
  - `20261022_100000_a1b2c3` 用户表增加手机号
- **执行结果**：成功
- **SQL 脚本**：

```sql
-- ChangeUnit: 20261022_100000_a1b2c3
ALTER TABLE `nor_user` ADD COLUMN `phone` VARCHAR(20) NULL COMMENT '手机号';
CREATE UNIQUE INDEX `idx_user_phone` ON `nor_user` (`phone`);
```

```
#### A.3 输出（PostgreSQL，仅改配置）

```sql
-- ChangeUnit: 20261022_100000_a1b2c3
-- Author: zhangsan
-- Release: 20261022
-- Description: 用户表增加手机号
-- Dialect: postgresql 14

ALTER TABLE "nor_user" ADD COLUMN "phone" VARCHAR(20) NULL;
COMMENT ON COLUMN "nor_user"."phone" IS '手机号';

CREATE UNIQUE INDEX "idx_user_phone" ON "nor_user" ("phone");
```

**逻辑模型完全不变，只改配置里的 `dialect`，输出自动切换。**

### 附录 B：当前版本查询（本地状态文件）

```powershell
# 当前版本（已应用单元中最大的 release）
db-chronicle status --config DbchronicleConfig.xml

# 直接看状态文件
Get-Content .\version_log\dev\DbchronicleState.xml
```

```xml
<!-- DbchronicleState.xml 结构（节选） -->
<state>
  <applied changeId="20261022_100000_a1b2c3" release="20261022"
           appliedAt="2026-10-23 09:53:07" checksum="…" dialect="mysql"
           appliedBy="my-app" description="…" failureAt="" failureStmt="" />
</state>
```

### 附录 C：日志文件夹与数据库一致性检查

| 场景       | 数据库          | 日志文件 | 状态   | 处理                                          |
| ---------- | --------------- | -------- | ------ | --------------------------------------------- |
| 正常       | 有记录          | 有文件   | 一致   | —                                             |
| 日志缺失   | 有记录          | 无文件   | 不一致 | `apply --backfill-logs` 补生成                |
| 日志过期   | 无记录          | 有文件   | 不一致 | 以数据库为准，`status` 输出警告，手工删除日志 |
| 文件被修改 | checksum 不一致 | —        | 不一致 | `verify` 报错，`apply` 拒绝执行               |

---

## 十六、实现状态（代码与方案的对应关系）

本节记录当前代码（`Src/`）与本文档的对应关系，避免后续维护时"文档说要、代码没做"的错位。

### 16.1 工程结构（实际）

```
Src/
├── DbChronicle.sln
├── DbChronicle/Resources/dialects/*.types.xml   五方言类型映射（嵌入资源）
├── DbChronicle/                       核心库（netstandard2.0，零第三方依赖，nuget: DbChronicle）
│   ├── Abstractions/                  契约层：IDbExecutor / IDbLock、诊断码、AppliedChange
│   ├── Domain/                        领域层：ChangeUnit、SchemaOperation、StructureModel、SqlStatement
│   ├── Compilation/                   编译层：Compiler / Dialects / Splitting / Checking
│   ├── Parsing/                       解析层：XML → Domain（含校验与 checksum）
│   ├── Runtime/                       运行时层：Execution / Versioning / Rollback / Naming
│   ├── Scanning/                      C# 实体反射 → 基线 XML（纯反射）
│   ├── Configuration/                 配置模型与加载
│   ├── Api/                           IDbChronicle / DbChronicleFactory / Options / Result
│   └── Resources/dialects/            五方言类型映射（嵌入资源）
├── DbChronicle.Cli/                   net8.0 命令行（dotnet tool: DbChronicle.Cli）
├── scripts/                           pre-commit 钩子（sh / ps1）
├── templates/                         IDE / 手工模板
└── Tests/                             测试（四个程序集，解决方案内同一文件夹）
    ├── DbChronicle.Tests/        单元：解析、编译、方言、拆分、检查、回滚
    ├── DbChronicle.Scanner.CSharp.Tests/  单元：反向工程（类型映射 / 特性读取 / XML）
    └── DbChronicle.IntegrationTests/  集成：SQLite 真跑；MySQL/PG/SS 按环境变量启用
Test/
├── DbChronicle.Demo/                  可运行演示（SQLite 全流程）
└── samples/                           示例工程（config + migrations + 审计日志）
```

**发布形态（两个包，职责分离）：**

| 包                | 内容                                                                              | 依赖                                                                                       |
| ----------------- | --------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `DbChronicle`     | `lib/netstandard2.0/DbChronicle.dll`：编译、检查、版本编排、回滚规划、C# 反向扫描 | **无第三方依赖**（纯 BCL）                                                                 |
| `DbChronicle.Cli` | dotnet tool（`db-chronicle`），内含 ADO.NET 执行器与库级锁                        | `DbChronicle` + MySqlConnector / Npgsql / Microsoft.Data.SqlClient / Microsoft.Data.Sqlite |

核心库**不提供**数据库驱动、也不提供宿主启动钩子：执行通过 `IDbExecutor` 注入，升级时机由宿主决定。

与第九章的差异：`Scanner.Database`、`CodeGen.*`、`Executor.EfCore` 未创建（对应三期/二期项目）；单元测试按方案放在 `Src/Tests/DbChronicle.Tests`，集成测试与 demo 放在仓库根 `Test/`。

### 16.2 已实现能力

| 能力                                                                                                                      | 状态 | 位置                                                                                              |
| ------------------------------------------------------------------------------------------------------------------------- | ---- | ------------------------------------------------------------------------------------------------- |
| 配置加载/默认配置生成                                                                                                     | ✅    | `Config/DbChronicleConfig.cs`                                                                     |
| 五方言编译（MySQL/MariaDB/PG/SQL Server/SQLite）                                                                          | ✅    | `Dialects/*Dialect.cs`                                                                            |
| 20 种逻辑类型 + YAML 映射                                                                                                 | ✅    | `Dialects/TypeMapper.cs`                                                                          |
| XML 解析与全部校验规则                                                                                                    | ✅    | `Parsing/ChangeUnitParser.cs`                                                                     |
| 四段排序（数据前置/拆依赖/结构变更/数据后置）                                                                             | ✅    | `Compilation/SchemaCompiler.cs`                                                                   |
| 累积模型 + `incrementalModify` 补全与 W014 退化                                                                           | ✅    | `Compilation/StructureModel.cs`、`SchemaCompiler.cs`                                              |
| 方言感知语句拆分（引号/注释/块/`DELIMITER`/`GO`/`$$`）                                                                    | ✅    | `Compilation/SqlStatementSplitter.cs`                                                             |
| 诊断码 W001~W014                                                                                                          | ✅    | `Compilation/CompileDiagnostic.cs`                                                                |
| SQLite rebuild（含列映射）                                                                                                | ✅    | `Dialects/SqliteDialect.cs` + `SchemaCompiler` 的 rebuild 聚合                                    |
| 本地状态文件（`DbchronicleState.xml`）+ checksum 校验 + 半态标记                                                          | ✅    | `Versioning/VersionStore.cs`                                                                      |
| 库级互斥锁（GET_LOCK / advisory lock / applock / 文件锁）                                                                 | ✅    | `Cli/AdoNet/*Lock.cs`                                                                             |
| SQL 快照 + md 审计 + `--backfill-logs`                                                                                    | ✅    | `Versioning/VersionLogWriter.cs`                                                                  |
| 语句级断点续跑（状态文件 `<statements>`）                                                                                 | ✅    | `VersionStore` + `SchemaExecutor.ExecuteUnit`                                                     |
| `apply --check-drift`（introspect 比对）                                                                                  | ✅    | `Execution/SchemaIntrospector.cs`                                                                 |
| `rollback --to-release`（保守版，见 16.3）                                                                                | ✅    | `Rollback/RollbackPlanner.cs`                                                                     |
| `scan --assembly`（C# 实体 → 基线草案）                                                                                   | ✅    | `Scanner.CSharp/CSharpScanner.cs`                                                                 |
| CLI 退出码 0~5                                                                                                            | ✅    | `Cli/Program.cs`                                                                                  |
| 语义冲突检测（重复 ADD / MODIFY 冲突 / ADD-DROP 矛盾 / RENAME 冲突 / 补丁缺 depends / RENAME 后引用旧名 / DROP 不存在列） | ✅    | `Checking/ChangeChecker.cs`                                                                       |
| 基线冻结校验（`frozen=true` 时禁止 MODIFY/DROP/RENAME）                                                                   | ✅    | `Parsing/ChangeUnitParser.cs`（E040）                                                             |
| 多方言一次性输出（`compile --all-dialects`）                                                                              | ✅    | `Cli/Commands.cs`                                                                                 |
| SQLite 时间列存储格式提示注释                                                                                             | ✅    | `Dialects/SqliteDialect.cs`                                                                       |
| MariaDB `RENAME COLUMN` 版本判断（10.5.2+）                                                                               | ✅    | `Dialects/MySqlDialect.cs`                                                                        |
| 数据操作节点 `<update>` / `<delete>` / `<clean>` / `<backfill>`                                                           | ✅    | `Parsing/ChangeUnitParser.cs` + `Dialects/SqlDialectBase.cs`                                      |
| 通配符表名 `table="*"`（离线占位符 W022，连库展开 W021，优先执行，E041 校验）                                             | ✅    | `Compilation/SchemaCompiler.cs` + `Execution/SchemaExecutor.cs` + `SchemaIntrospector.ListTables` |
| 列级 `charset` / `collate`、列内联 `foreignKey`、`FULLTEXT` 索引                                                          | ✅    | `Dialects/*Dialect.cs`                                                                            |
| 主键 `REPLACE` / 外键 `MODIFY`（生成 DROP + ADD）                                                                         | ✅    | `Compilation/SchemaCompiler.cs`                                                                   |
| 诊断码 W015~W019                                                                                                          | ✅    | `Compilation/CompileDiagnostic.cs`                                                                |
| 执行器方言一致性校验                                                                                                      | ✅    | `Execution/SchemaExecutor.cs`                                                                     |
| Global Tool 打包（`PackAsTool`）                                                                                          | ✅    | `Cli/DbChronicle.Cli.csproj`                                                                      |

### 16.3 实现与方案的偏差（需知）

| 项                                                       | 方案                    | 实现                                                                                                               | 原因                                                                                    |
| -------------------------------------------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------- |
| CLI 参数解析                                             | System.CommandLine      | 手写轻量解析（`Cli/CliArguments.cs`）                                                                              | 减少 beta 版 API 依赖；命令与参数名与方案一致                                           |
| 语句级续跑                                               | 标注 v1.1               | 默认启用（无事务方言）                                                                                             | 半态是 MySQL 的真实风险，收益大于簿记成本；哈希不一致时拒绝续跑                         |
| `rollback`                                               | 二期"按需"，未给细节    | 保守版：只回滚可逆操作，遇到 `DROP COLUMN` / `DROP TABLE` / 删除约束 / 数据操作 / `RawSql` 直接标记 blocker 并中止 | 这些操作数据已丢失，自动"回滚"会制造更严重的问题                                        |
| `--check-drift`                                          | v1.1 的 apply 内复核    | 独立开关，只读检查，检出即返回失败码 1，不自动执行 apply                                                           | 让用户先确认再执行，避免检查与执行混在一次调用里                                        |
| SQLite rebuild                                           | 建新表→导数据→删旧→改名 | 同上，但重建后**不恢复外键与检查约束**（模型只跟踪列与主键）                                                       | 外键/检查约束的定义未进累积模型；需要时用 `<rawSql>` 重建                               |
| 版本表 DDL                                               | 单一 `DATETIME` 写法    | 按方言生成 `DATETIME` / `TIMESTAMP` / `DATETIME2` / `TEXT`，时间函数 `NOW()` / `GETDATE()` / `datetime('now')`     | 同一条建表语句无法在四类库上通用                                                        |
| 依赖清单                                                 | 含 System.CommandLine   | 未引入                                                                                                             | 见上                                                                                    |
| `options.includeRollback` / `options.statementDelimiter` | 参与编译选项            | 仅解析、当前不生效（回滚走 `rollback` 独立命令；拆分器按方言固定分隔符）                                           | 两项没有实际消费点，保留字段避免破坏配置兼容                                            |
| 表级 `action="REBUILD"`                                  | 重建表                  | 按 `ALTER` 处理（仅做破坏性校验），输出 W018                                                                       | MySQL/PG/SS 的重建语义差异大，SQLite 已由 `RequiresRebuild` 自动处理                    |
| 表级 `ENGINE` / `CHARSET` / `TABLESPACE`                 | 4.1.2 列出专用 action   | 报 E012 并提示改用 `<rawSql>`                                                                                      | 4.1.11 已规定高级对象统一走 `RawSqlOperation`                                           |
| `rollback` 对 SQLite 的列定义回滚                        | 未细化                  | 直接标记 blocker："需要整表重建，请手工执行"                                                                       | SQLite 的 `MODIFY` 回滚等价于一次 rebuild（需要完整目标结构），保守阻断比生成半成品安全 |

### 16.4 明确未实现（后续版本）

| 项                                                                              | 归属     | 说明                                                                  |
| ------------------------------------------------------------------------------- | -------- | --------------------------------------------------------------------- |
| `DbChronicle.Scanner.Database`                                                  | 三期     | 从现有数据库反向生成基线（需要各库元数据查询）                        |
| `DbChronicle.CodeGen.*`                                                         | 二期     | XML → 各语言实体                                                      |
| `DbChronicle.Executor.EfCore`                                                   | 二期     | EF Core 执行器                                                        |
| 在线 DDL                                                                        | 非目标   | 不集成 gh-ost / pt-online-schema-change                               |
| 多节点协调                                                                      | 非目标   | 只依赖数据库原生锁                                                    |
| SQL Server 表/列注释                                                            | 一期跳过 | 编译期输出 W006/W007，需要时用 `<rawSql>` 写 `sp_addextendedproperty` |
| 强类型差异比对（列类型/长度/可空）                                              | v1.2     | `--check-drift` 目前只比对"列是否存在"，避免类型规范名误报            |
| IDE 插件（右键菜单生成）                                                        | 二期     | 未提供；用 `db-chronicle new` + `Src/templates` 模板代替              |
| `scan --source`（Roslyn）/ `Scanner.Java` / `Scanner.Go` / `Scanner.TypeScript` | 三期     | 仅实现程序集反射扫描                                                  |

### 16.5 测试现状

`dotnet test`：**76 条全部通过**（`Src/Tests` 下三个程序集：Core 58 + Scanner.CSharp 7 + Integration 11），覆盖：

- 解析校验（缺 type/length、RENAME、Blob 长度、文件名规范）
- 编译输出（ADD/MODIFY/CREATE、DateTimeOffset 告警、增量补全与 W014 退化）
- 四方言输出（PG 的 COMMENT ON 与多语句、SQL Server 的默认值约束命名、SQLite 的 rebuild）
- 语句拆分（字符串内分号、注释、`BEGIN...END`、`DELIMITER`）
- 冲突检测与命名
- SQLite 端到端：建表、版本表记录、checksum 拒绝、半态续跑
- 回滚规划（反向 ADD/MODIFY、不可逆阻断、SQLite 重建边界）
- 基线冻结（E040）与 `DROP 不存在列`（E100）
- 反向工程：CLR 类型映射、`[Column]` / `[MaxLength]` / `[Comment]` / `[NotMapped]` 读取、导航属性跳过、基线 XML 生成
- 操作全集补全：列级 `charset`/`collate`、列内联 `foreignKey`、`FULLTEXT` 索引、主键 `REPLACE`、外键 `MODIFY`、`update`/`delete`/`clean`/`backfill` 渲染与 `stage` 排序、自定义分隔符、`REBUILD` 告警、`ENGINE` 的 E012 提示
- 集成测试：SQLite 真库 apply / 二次跳过 / checksum 拒绝 / 半态续跑；MySQL / PostgreSQL / SQL Server 按 `DBCHRONICLE_TEST_MYSQL` / `DBCHRONICLE_TEST_PG` / `DBCHRONICLE_TEST_SQLSERVER` 环境变量启用（未配置则跳过）

### 16.6 工程化交付物

| 交付物                        | 路径                                                                                  | 说明                                                                                                                                                                                                                                         |
| ----------------------------- | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pre-commit 钩子（POSIX）      | `Src/scripts/pre-commit`                                                              | 提交前跑 `check`，未安装 CLI 时跳过                                                                                                                                                                                                          |
| Pre-commit 钩子（PowerShell） | `Src/scripts/pre-commit.ps1`                                                          | Windows 版本                                                                                                                                                                                                                                 |
| IDE / 手工模板                | `Src/templates/migration-template.xml`                                                | 复制后改名，或用 `db-chronicle new`                                                                                                                                                                                                          |
| 示例工程                      | `Test/samples/`（config + 基线 + 增量）                                               | 与 demo / 集成测试同处 Test 下，可直接 `compile --all-dialects` / `check`                                                                                                                                                                    |
| 宿主启动升级示例              | 文档 §6.2（宿主自写 `IHostedService` 调 `IDbChronicle.DbUpgrade()`）                  | 组件不内置启动钩子，升级时机由宿主决定                                                                                                                                                                                                       |
| 可运行演示                    | `Test/DbChronicle.Demo`（默认 MySQL / `--sqlite` / `--embedded` / `--config <path>`） | 配置随项目发布：`Test/DbChronicle.Demo/Config/dbchronicle.{mysql,sqlite,embedded}.yaml`，逻辑模型取 `LogicModels/{mysql,sqlite,embedded}/`；`--config <path>` 覆盖内置配置（不生成任何文件）；`--apply` 才落库，`--reset` 仅重建嵌入式演示库 |
| 演示用逻辑模型                | `Test/DbChronicle.Demo/LogicModels/*/**/*.xml`                                        | 每套含基线 + 增量；可直接阅读修改（基线改动会导致 checksum 拒绝，需换库或 `--reset`）                                                                                                                                                        |
| CI 工作流模板                 | `Docs/ci/github-actions.yml`                                                          | 未初始化 Git 时仅作参考；启用 GitHub 后复制到 `.github/workflows/`                                                                                                                                                                           |
| 发布验证                      | `dotnet publish Src/DbChronicle.Cli -c Release`                                       | 已验证产物 `db-chronicle.exe` 可运行                                                                                                                                                                                                         |

**真库验证记录：**

| 库                                                | 状态     | 证据                                                                                                                                                                                                      |
| ------------------------------------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| MySQL 5.7 / `snibemiddlewaredb`（127.0.0.1:3806） | ✅ 已实测 | `Test/DbChronicle.Demo --mysql --apply`：introspect 生成基线 → 版本表创建 → `ADD test1`（charset/collate/default/comment）→ `MODIFY msg` 为 `LONGTEXT` → 版本表 2 条记录 → 结构复核通过；二次运行正确跳过 |
| PostgreSQL / SQL Server                           | ⏳ 未实测 | 方言 SQL 仅有单元断言，需要连接后跑 `apply` / `verify`                                                                                                                                                    |

MySQL 实测中发现并修复的问题（已加回归测试）：

1. 版本表列名 `release` 是 MySQL 保留字，早期 SQL 未加引号导致 `CREATE TABLE` 语法错误 → 现在按方言引用标识符（`` `release` `` / `"release"` / `[release]`）。
2. `apply --dry-run` 原本也会建版本表，与"只读"承诺不符 → 改为 dry-run 只读，表不存在时按空记录处理。

---

> 文档结束。  
> 版本：v1.0
