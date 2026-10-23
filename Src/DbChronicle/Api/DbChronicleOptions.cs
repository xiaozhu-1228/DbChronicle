using System;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle
{
    /// <summary>
    /// 组件启动必须条件。核心库不包含数据库驱动实现，执行器在工厂方法上单独注入。
    /// </summary>
    public sealed class DbChronicleOptions
    {
        /// <summary>启动配置文件路径（DbchronicleConfig.xml）。</summary>
        public string ConfigPath { get; set; }

        /// <summary>诊断与执行日志输出。</summary>
        public Action<string> Log { get; set; }
    }
}
