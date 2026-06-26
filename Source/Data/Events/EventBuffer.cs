using NPCLife.Cards;
using System.Collections.Generic;

namespace RimLife
{
    /// <summary>
    /// 游戏侧事件缓冲层。收集 Harmony 钩子产生的事件，在无新事件的
    /// 指定 tick 数后批量推送到框架侧 EventPool，实现事件合并去抖。
    ///
    /// 设计决策：
    /// - 不做最大持有时间：正常游戏不会持续产生事件超过缓冲窗口；若持续不静默，
    ///   通常是某 mod 死循环，此时整个游戏已无法正常继续。
    /// - 不做高重要度绕行：Agent 讨论本身需要分钟级时间，争 1000 tick 无意义。
    /// - 不做每批数量限制：突发事件通常属于同一因果链，截断反而有害；若单个因果链
    ///   事件量超过 LLM 上下文窗口，说明游戏已处于异常状态。
    /// - TimerPulse 不经过此处：定时心跳是框架内部轮询信号，通过 TickTimerPulses
    ///   直写 EventPool，不再此缓冲。
    /// </summary>
    public class EventBuffer
    {
        private readonly List<IGameEvent> _pending = new List<IGameEvent>();
        private int _lastEventTick = -1;

        /// <summary>
        /// 缓冲空闲超时（游戏 tick）。自最后一个事件到达起，
        /// 经过此 tick 数无新事件到达，即触发推送。默认 600（10 秒）。
        /// </summary>
        public int IdleTimeoutTicks { get; set; } = 600;

        public int Count => _pending.Count;
        public bool HasEvents => _pending.Count > 0;

        /// <summary>向缓冲追加一个游戏事件。</summary>
        public void Append(IGameEvent evt)
        {
            if (evt == null) return;
            _pending.Add(evt);
            if (evt.Tick > _lastEventTick)
                _lastEventTick = evt.Tick;
            
            // 诊断日志：记录事件进入缓冲
            Verse.Log.Message($"[RimLife.EventBuffer] Event appended: {evt.EventID} (pending={_pending.Count})");
        }

        /// <summary>
        /// 检查是否满足推送条件：缓冲非空，且自最后一个事件到达起已超过 IdleTimeoutTicks。
        /// </summary>
        public bool ShouldFlush(int currentTick)
        {
            return _pending.Count > 0
                && _lastEventTick >= 0
                && (currentTick - _lastEventTick) >= IdleTimeoutTicks;
        }

        /// <summary>取出缓冲中所有事件并清空缓冲。</summary>
        public IReadOnlyList<IGameEvent> Drain()
        {
            if (_pending.Count == 0)
                return new List<IGameEvent>();

            var drained = new List<IGameEvent>(_pending);
            _pending.Clear();
            _lastEventTick = -1;
            return drained;
        }

        /// <summary>重置缓冲状态（新游戏/读档时调用）。</summary>
        public void Reset()
        {
            _pending.Clear();
            _lastEventTick = -1;
        }
    }
}
