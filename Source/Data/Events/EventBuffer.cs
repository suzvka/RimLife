using NPCLife.Cards;
using RimLife.UI;
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
        private readonly HashSet<string> _pendingEventIds = new HashSet<string>();
        private int _lastEventTick = -1;

        /// <summary>
        /// 缓冲空闲超时（游戏 tick）。自最后一个事件到达起，
        /// 经过此 tick 数无新事件到达，即触发推送。默认 300（5 秒@1×速度）。
        /// 较低的超时值可减少事件等待延迟，让导演更快感知到新事件。
        /// </summary>
        public int IdleTimeoutTicks { get; set; } = 300;

        public int Count => _pending.Count;
        public bool HasEvents => _pending.Count > 0;

        /// <summary>向缓冲追加一个游戏事件。</summary>
        public void Append(IGameEvent evt)
        {
            if (evt == null) return;

            // EventID 去重：同一事件可能被多个 Harmony 补丁注入（LetterStack.ReceiveLetter 有多个重载），
            // 在此层拦截重复 EventID，避免同一事件在缓冲中重复出现。
            if (!string.IsNullOrEmpty(evt.EventID) && !_pendingEventIds.Add(evt.EventID))
            {
                RimLifeLogger.Message($"[RimLife.DIAG] EventBuffer SKIP (duplicate EventID): id={evt.EventID}, def={evt.DefName}");
                return;
            }

            _pending.Add(evt);
            int currentTick = Verse.Find.TickManager?.TicksGame ?? 0;
            if (currentTick > _lastEventTick)
                _lastEventTick = currentTick;
            
            // 诊断日志：记录事件进入缓冲，包括 EventID 和 Payload 摘要
            var payloadSummary = "";
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in evt.Payload)
                    parts.Add($"{kv.Key}={UIHelper.Truncate(kv.Value, 30)}");
                payloadSummary = string.Join(", ", parts);
            }
            RimLifeLogger.Message($"[RimLife.DIAG] EventBuffer.Append: id={evt.EventID}, def={evt.DefName}, imp={evt.Importance:F1}, payload=[{payloadSummary}]");
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
            _pendingEventIds.Clear();
            _lastEventTick = -1;
            return drained;
        }

        /// <summary>重置缓冲状态（新游戏/读档时调用）。</summary>
        public void Reset()
        {
            _pending.Clear();
            _pendingEventIds.Clear();
            _lastEventTick = -1;
        }
    }
}
