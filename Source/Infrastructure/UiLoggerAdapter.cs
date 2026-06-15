using RimLife.Framework;
using RimLife.UI;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// ILogger 适配器实现。
    /// 将框架日志重定向到 UI 调试窗口的 LogBuffer。
    /// </summary>
    public class UiLoggerAdapter : ILogger
    {
        public void Message(string msg)
        {
            RimLifeLogger.Message(msg);
        }

        public void Warning(string msg)
        {
            RimLifeLogger.Warning(msg);
        }

        public void Error(string msg)
        {
            RimLifeLogger.Error(msg);
        }
    }
}
