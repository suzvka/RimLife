using NPCLife.Framework;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimWorld GameComponent 桥接。职责：
    /// 1. 每帧清空主线程回调队列（MainThreadDispatcher）。
    /// 2. 驱动导演/临时编剧的定时器脉冲（TickTimerPulses）。
    /// AgentLoop 通过事件池 OnThresholdReached 回调自动激活。
    /// </summary>
    public class RimWorldAgentDriver : GameComponent
    {
        public RimWorldAgentDriver(Game game) : base()
        {
        }

        public override void GameComponentUpdate()
        {
            MainThreadDispatcher.DrainQueue();
            RimLifeCore.TickTimerPulses();
        }
    }
}
