# DbChronicle

一套高效、可审计的数据库版本升级工具集：用与数据库无关的逻辑模型描述结构变更，自动编译为 MySQL / MariaDB / PostgreSQL / SQL Server / SQLite 的 DDL，并负责版本托管、审计留档与保守回滚。

## 组成

| 交付物 | 说明 |
| --- | --- |
| `DbChronicle` | 核心库（netstandard2.0，**零第三方依赖**）：解析、编译、检查、版本编排、回滚规划、C# 反射扫描、数据库反向扫描、代码生成 |
| `DbChronicle.Cli` | 命令行工具（`db-chronicle`，dotnet tool）：**命令宿主**，能力按需插件化（插件契约也在本程序集内） |
| `DbChronicle.Executor.AdoNet` | 插件：ADO.NET 执行器与库级锁（MySqlConnector / Npgsql / SqlClient / Sqlite） |
| `DbChronicle.Executor.EfCore` | 插件（应用内嵌）：EF Core 执行器，复用宿主 `DbContext` |
| `DbChronicle.Scanner.Source` | 插件：C# 源码扫描（Roslyn，`scan --source --lang csharp`） |
| `DbChronicle.Scanner.Languages` | 插件：Java / Go / TypeScript 源码扫描 |

## 快速开始

```powershell
# 初始化（生成 DbchronicleConfig.xml 与 migrations / version_log / output 目录）
dotnet run --project Src/DbChronicle.Cli -- init --dialect mysql

# 生成变更单元（文件名、id、作者、release 自动生成，绝不覆盖同名文件）
dotnet run --project Src/DbChronicle.Cli -- new --desc "add phone to nor_user"

# 编译（离线，不连数据库）
dotnet run --project Src/DbChronicle.Cli -- compile --config DbchronicleConfig.xml
dotnet run --project Src/DbChronicle.Cli -- compile --config DbchronicleConfig.xml --all-dialects

# 语义检查（重复 ADD / MODIFY 冲突 / DROP 不存在列 / 跨单元顺序 …）
dotnet run --project Src/DbChronicle.Cli -- check --config DbchronicleConfig.xml --strict-warnings

# 应用（连接信息通过 DBCHRONICLE_CONN 环境变量或交互式输入）
$env:DBCHRONICLE_CONN = "Server=localhost;Port=3306;Database=mydb;User ID=root;Password=***"
dotnet run --project Src/DbChronicle.Cli -- upgrade --config DbchronicleConfig.xml   # 别名：apply

# 其他常用选项：--dry-run 只打印 SQL / --restart 清空断点重跑 / --continue-on-error 失败后继续其余单元
#               --check-drift 先比对库结构与模型 / --backfill-logs 补生成审计日志
#               --mark-applied <id> 手工标记已应用
dotnet run --project Src/DbChronicle.Cli -- upgrade --config DbchronicleConfig.xml --dry-run

# 状态 / 校验（读本地 version_log 状态文件，无需连库）
dotnet run --project Src/DbChronicle.Cli -- status --config DbchronicleConfig.xml
dotnet run --project Src/DbChronicle.Cli -- verify --config DbchronicleConfig.xml

# 反向工程：从数据库现有结构生成基线草案（5 方言，只读）
dotnet run --project Src/DbChronicle.Cli -- scan-db --out migrations/00000000_000000_baseline.xml

# 反向工程：从源码生成基线草案（--lang csharp|java|go|typescript|auto）
dotnet run --project Src/DbChronicle.Cli -- scan --source ./src --lang csharp --out migrations/00000000_000000_baseline.xml

# 反向工程：从编译后的程序集生成基线草案
dotnet run --project Src/DbChronicle.Cli -- scan --assembly ./bin/App.dll --out migrations/00000000_000000_baseline.xml

# 代码生成：由 migrations 生成实体（C# / Java / Go / TypeScript / C++）
dotnet run --project Src/DbChronicle.Cli -- codegen --lang csharp --out ./Entities --namespace MyApp.Entities
dotnet run --project Src/DbChronicle.Cli -- codegen --lang cpp --out ./cpp-entities --namespace myapp::entities
```

退出码：`0` 成功、`1` 参数/校验失败、`2` 有待应用变更（仅 `status`）、`3` 执行/校验失败、`4` 未取得库级锁、`5` 环境问题（连接失败、非交互环境缺连接信息）。

## 插件与按需部署

CLI 是命令宿主：核心命令（`init` / `new` / `compile` / `check` / `status` / `verify` / `rollback` / `codegen` / `scan --assembly`）**不需要任何插件**；只有在需要数据库执行或源码扫描时，才加载对应的那一个插件 dll。

| 能力 | 提供者 | 缺失时 |
| --- | --- | --- |
| 数据库执行（`upgrade` / `scan-db` / `rollback --apply`） | `DbChronicle.Executor.AdoNet.dll` | 退出码 **6** + "请把 … 部署到 plugins 目录" |
| C# 源码扫描（`scan --source --lang csharp`） | `DbChronicle.Scanner.Source.dll`（含 Roslyn） | 同上 |
| Java / Go / TS 源码扫描 | `DbChronicle.Scanner.Languages.dll` | 同上 |

### 分发方式一：dotnet tool（NuGet）

```powershell
# 三个变体包，按需选一个装
dotnet tool install -g DbChronicle.Cli           # 无插件，约 0.18 MB（只要 compile/check/status/codegen）
dotnet tool install -g DbChronicle.Cli.AdoNet    # +执行器，约 17 MB（追加 upgrade/scan-db）
dotnet tool install -g DbChronicle.Cli.All       # 全量，约 22 MB（装完即用，可关掉不用的）

# 生成本地包
./Tools/pack.ps1 -Output nupkg
```

装完全量包后按需启停（状态存用户目录，`dotnet tool update` 不会恢复）：

```powershell
db-chronicle plugin list                  # 看已部署 / 已禁用 / 各提供什么
db-chronicle plugin disable scanner-source   # 用不到源码扫描就关掉
db-chronicle plugin enable scanner-source
```

### 分发方式二：zip（离线 / 自包含 / 内网）

```powershell
./Tools/publish.ps1 -Output dist                 # 基础包（仅离线能力）
./Tools/publish.ps1 -Output dist -Plugins adonet # 带数据库执行能力
./Tools/publish.ps1 -Output dist -Plugins all    # 全量
./Tools/publish.ps1 -Output dist -Plugins adonet -SelfContained  # 目标机无需 .NET 运行时
```

两种方式的产物目录结构相同（**每个插件独占子目录**，删/禁用互不影响）：

```
dist/  （或 tool 的 tools/net8.0/any/）
├── db-chronicle.exe / db-chronicle.dll
├── DbChronicle.dll                 核心库（零第三方依赖）
└── plugins/
    ├── adonet/                     ← 整目录 = 一个能力
    │   ├── DbChronicle.Executor.AdoNet.dll
    │   ├── MySqlConnector.dll · Npgsql.dll · SqlClient · Sqlite
    │   └── runtimes/win-x64/native/e_sqlite3.dll
    └── scanner-source/
        ├── DbChronicle.Scanner.Source.dll
        └── Microsoft.CodeAnalysis*.dll
```

开发期：`dotnet build Src/DbChronicle.sln` 后，CLI 会把插件输出复制到 `bin/<Configuration>/net8.0/plugins/<插件名>/`；也可用 `--plugin-dir <dir>` 指定目录，`--unload-plugins` 在命令结束后卸载已加载插件。

编写新插件：在类库中引用 `DbChronicle.Cli`，给静态类标 `[DbChroniclePlugin(Name = "...")]`，实现 `public static void Initialize(IPluginRegistry registry)`，在其中 `AddExecutor` / `AddSourceScanner` / `AddCodeGenerator` / `AddCommand`，然后把 dll 放进 `plugins/`。

## 核心约定

- **逻辑模型是唯一事实来源**：`migrations/*.xml` 只写「改什么」，不写物理类型与方言语法。
- **已应用文件不可修改**：`checksum = SHA-256(文件原始字节)`，`upgrade`（别名 `apply`）校验不一致直接拒绝；修正请新增补丁单元并用 `<depends>` 声明依赖。
- **状态本地化**：已应用记录与语句级续跑进度都在 `version_log/{environment}/DbchronicleState.xml`，不建版本表。
- **审计留档**：`version_log/{environment}/{release}_dbupdate.md` + `sql/{release}/{change_id}.{dialect}.sql` 快照。
- **执行安全**：整单元事务（PG / SQL Server / SQLite）或语句级续跑（MySQL / MariaDB）；库级互斥锁防并发 upgrade。`options.commitScope` 可改按表/按批提交（table / batch，配 `commitBatchSize`），断点记入状态文件；`--continue-on-error` 可在失败后继续其余单元并在结尾汇总。

## 目录

```
Src/
  DbChronicle/                核心库（零第三方依赖）
    Common/                   枚举（Enums/）+ 列合并 + 内嵌类型映射（Resources/dialects/）
    Configuration/            配置加载（DbchronicleConfig.xml）
    Models/                   逻辑模型：变更单元 / 操作定义（Operations/）/ 结构模型 / 各 Options 与 Result
    Parsing/                  变更单元 XML 解析 + checksum
    Compilation/              模型 → 方言 SQL（Compiler / Dialects / Checks）
    Migrations/               版本编排（Executor / State / Rollback / Templates）
    Scanner/                  C# 实体反射扫描 + BaselineXmlWriter + Database/（5 方言反向工程）
    CodeGen/                  XML → 实体（C# / Java / Go / TypeScript）
    Diagnostics/              诊断码
    Execution/                执行器契约（IDbExecutor / IDbLock 等）
    Embedded/                 宿主内嵌入口（DbChronicleFactory / IDbChronicle）
  DbChronicle.Cli/            命令行（init / new / compile / check / upgrade / status / verify / scan / scan-db / codegen / rollback）
  DbChronicle.Executor.AdoNet/  ADO.NET 执行器与库级锁（唯一带数据库驱动的工程）
  DbChronicle.Executor.EfCore/  EF Core 执行器（复用宿主 DbContext）
  DbChronicle.Scanner.Source/   Roslyn 源码扫描（scan 的 source 模式）
  DbChronicle.Scanner.Languages/ Java / Go / TypeScript 实体扫描
  Tests/                      单元测试（DbChronicle.Tests / Cli.Tests / Scanner.Tests）与集成测试
Test/
  samples/                    示例工程（配置 + 基线 + 增量）
  DbChronicle.Demo/           可运行演示（MySQL / PostgreSQL）
Tools/
  publish.ps1                 zip 发布（基础包 + 按需插件组合，支持自包含）
  pack.ps1                    NuGet 变体包（Cli / Cli.AdoNet / Cli.All）
Docs/
  DbChronicle — 数据库版本托管与升级工具集 方案文档.md
  ci/github-actions.yml       CI 模板
```

代码组织约定：一个文件只放一个类型；枚举统一放 `Common/Enums`；目录名与命名空间一一对应；`using` 分组为 `System.*` 在前、空行、项目命名空间。

## 构建与测试

```powershell
dotnet build Src/DbChronicle.sln
dotnet test Src/DbChronicle.sln
```

可运行演示（Demo 只依赖核心库 + 执行器库，与 CLI 无关）：

```powershell
# 方言取自 Test/DbChronicle.Demo/Config/DbchronicleConfig.xml 的 <database dialect="...">，无需命令行参数
dotnet run --project Test/DbChronicle.Demo
```

连接串默认指向本地演示库，可用环境变量覆盖：`DBCHRONICLE_DEMO_MYSQL`（MySQL / MariaDB）、`DBCHRONICLE_DEMO_PG`（PostgreSQL）。未设置环境变量时使用内置默认值，不会拒绝运行。

演示表需预先存在（Demo 只连接目标库并执行 `LogicModels/` 里的增量变更，不建库也不建表）：

```sql
-- MySQL
CREATE TABLE `useraccess` (`id` BIGINT NOT NULL AUTO_INCREMENT,
                           `name` VARCHAR(64) NOT NULL, PRIMARY KEY (`id`));

-- PostgreSQL（schema dbchronicle_demo）
CREATE TABLE "useraccess" ("id" BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                           "name" VARCHAR(64) NOT NULL, CONSTRAINT "useraccess_pkey" PRIMARY KEY ("id"));
```

状态文件按场景隔离：`version_log/mysql/` 与 `version_log/pgsql/`。模型文件一旦被应用过就不能再改（checksum 强校验）；要重新演示，请删除对应的状态目录并把演示表恢复到初始结构。

删除或重命名 `LogicModels/` 下的文件后，先 `dotnet clean`（或删掉 `bin/.../LogicModels` 里的旧副本），否则输出目录残留旧文件会导致 `change_id` 重复。

Demo 共用一份逻辑模型（`LogicModels/`）与一份配置（`Config/DbchronicleConfig.xml`），`database dialect` 由配置决定——这就是本组件"一套模型、多数据库编译"的最小示例。模型只含增量变更单元（表由既有系统创建）。当前覆盖 MySQL / PostgreSQL，SQLite 与 SQL Server 待后续。

所有场景统一通过**组件 API** 调用：`DbChronicleFactory.Create(executor, new DbChronicleOptions { Config = ... })`（也可用 `ConfigPath` 指向配置文件）拿到 `IDbChronicle`，再调 `Compile / Check / DbUpgrade / Status`；Demo 不引用 `DbChronicle.Cli`，也不演示 `DbChronicleMigrator`（那是核心库内的另一条编排入口，供工具形态使用）。

真库用例按环境变量启用：`DBCHRONICLE_TEST_MYSQL`、`DBCHRONICLE_TEST_PG`、`DBCHRONICLE_TEST_SQLSERVER`（未配置时自动跳过）。

## 许可证

MIT，见 [LICENSE](LICENSE)。第三方依赖均为 MIT / Apache-2.0 / PostgreSQL License 等宽松许可证，允许商业使用。
