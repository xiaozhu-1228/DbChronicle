using System;

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
