using System;

namespace DbChronicle.Cli
{
    public static class ExitCodes
    {
        public const int Success = 0;
        public const int Usage = 1;
        public const int Pending = 2;
        public const int Execution = 3;
        public const int Locked = 4;
        public const int Environment = 5;

        /// <summary>缺少所需能力（执行器 / 扫描器等插件未部署）。</summary>
        public const int MissingPlugin = 6;
    }
}
