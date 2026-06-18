namespace NPCLife.Framework
{
    /// <summary>
    /// 统一日志接口。所有核心组件通过此接口输出日志，
    /// 由宿主层（RimWorld 适配层）注入具体实现。
    /// 零外部依赖。
    /// </summary>
    public interface ILogger
    {
        /// <summary>信息级别日志。</summary>
        void Message(string msg);

        /// <summary>警告级别日志。</summary>
        void Warning(string msg);

        /// <summary>错误级别日志。</summary>
        void Error(string msg);
    }
}
