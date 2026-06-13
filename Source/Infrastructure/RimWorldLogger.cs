using RimLife.Framework;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimWorld 日志适配器。将 <see cref="ILogger"/> 桥接到 Verse.Log。
    /// </summary>
    public class RimWorldLogger : ILogger
    {
        public void Message(string msg) => Verse.Log.Message(msg);
        public void Warning(string msg) => Verse.Log.Warning(msg);
        public void Error(string msg)   => Verse.Log.Error(msg);
    }
}
