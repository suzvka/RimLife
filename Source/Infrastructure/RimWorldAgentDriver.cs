using RimLife.Framework;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimWorld GameComponent 桥接。唯一职责：每帧清空主线程回调队列。
    /// AgentLoop 通过事件池 OnThresholdReached 回调自动激活，不依赖 Tick。
    /// </summary>
    public class RimWorldAgentDriver : GameComponent
    {
        public RimWorldAgentDriver(Game game) : base()
        {
        }

        public override void GameComponentUpdate()
        {
            MainThreadDispatcher.DrainQueue();
        }
    }
}
