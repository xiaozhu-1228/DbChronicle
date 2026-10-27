using System.Collections.Generic;
using System;

using DbChronicle.Configuration;
using DbChronicle.Migrations.State;
using DbChronicle.Models;

namespace DbChronicle.Embedded
{
    /// <summary>
    /// 组件实例。宿主拿到实例后即可完成升级、编译、检查与状态查询。
    /// </summary>
    public interface IDbChronicle : IDisposable
    {
        /// <summary>生效的配置（已合并 MigrationsPath 覆盖）。</summary>
        DbChronicleConfig Config { get; }

        /// <summary>
        /// 执行升级：读版本表 → 编译 → 逐条执行 → 写版本表 → 落审计日志。
        /// 使用工厂注入的 <see cref="Execution.IDbExecutor"/>，核心库不创建连接。
        /// </summary>
        DbUpgradeResult DbUpgrade(DbUpgradeOptions options = null);

        /// <summary>只编译生成 SQL（离线，不连库）。changeId 为空表示全部单元。</summary>
        string Compile(string changeId = null);

        /// <summary>语义检查（重复 ADD、冲突、DROP 不存在列等），不连库。</summary>
        IReadOnlyList<CompileDiagnostic> Check();

        /// <summary>读取版本表。</summary>
        IReadOnlyList<AppliedChange> Status();

        /// <summary>按 (release, change_id) 加载全部变更单元。</summary>
        IReadOnlyList<ChangeUnit> LoadUnits();
    }
}