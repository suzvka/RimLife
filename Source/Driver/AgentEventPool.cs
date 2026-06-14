using RimLife.Cards;
using RimLife.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimLife.Driver
{
    /// <summary>
    /// Agent 事件池。实现 IEventLog，替代原来的 RimWorldEventLog。
    ///
    /// 双层结构：
    /// - _pending 激活缓冲区：暂存未处理事件，导演激活时清空（瞬态）
    /// - _recent 历史环形缓冲：保留近期事件供 query_events 等 MCP 工具查询
    ///
    /// 事件通过 Append() 同时写入两个缓冲区。
    /// 触发条件（Count/Importance）仅评估 _pending。
    /// 零持久化：存档时不保留池内容，读档后从零开始。
    /// </summary>
    public class AgentEventPool : IEventLog, IDisposable
    {
        private readonly DriverConfig _config;
        private readonly List<IGameEvent> _pending = new List<IGameEvent>();
        private readonly List<IGameEvent> _recent = new List<IGameEvent>();
        private int _totalImportance;
        private int _totalAppended;

        /// <summary>当池状态变化且满足任一阈值时触发。订阅者（AgentLoop）被动激活。</summary>
        public event Action OnThresholdReached;

        /// <summary>创建事件池。</summary>
        /// <param name="config">驱动配置（重要度权重、历史容量等）。</param>
        public AgentEventPool(DriverConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ================================================================
        // IEventLog 实现
        // ================================================================

        /// <summary>追加一条事件。同时写入 pending 缓冲区和 recent 历史。</summary>
        public void Append(IGameEvent evt)
        {
            if (evt == null) return;

            // 写入 pending 激活缓冲区
            _pending.Add(evt);
            _totalImportance += _config.GetSeverityWeight(evt.Severity);

            // 写入 recent 历史环形缓冲
            _recent.Add(evt);
            while (_recent.Count > _config.RecentHistoryCapacity)
            {
                // 优先裁剪 Minor 事件
                int removeIdx = -1;
                for (int i = 0; i < _recent.Count; i++)
                {
                    if (_recent[i].Severity == "Minor")
                    {
                        removeIdx = i;
                        break;
                    }
                }
                if (removeIdx < 0) removeIdx = 0;
                _recent.RemoveAt(removeIdx);
            }

            _totalAppended++;

            // 每次追加后检查阈值，满足则通知订阅者
            CheckThreshold();
        }

        private void CheckThreshold()
        {
            if (PendingCount >= _config.CountThreshold
                || TotalImportance >= _config.ImportanceThreshold)
            {
                OnThresholdReached?.Invoke();
            }
        }

        /// <summary>从 recent 历史缓冲中查询事件。</summary>
        public IReadOnlyList<IGameEvent> Query(EventQuery query)
        {
            if (query == null) query = EventQuery.All;

            IEnumerable<IGameEvent> result = _recent;

            // 按标签筛选
            if (query.TagsAny != null && query.TagsAny.Count > 0)
                result = result.Where(e => e.Tags != null && query.TagsAny.Any(t => e.Tags.Contains(t)));
            if (query.TagsAll != null && query.TagsAll.Count > 0)
                result = result.Where(e => e.Tags != null && query.TagsAll.All(t => e.Tags.Contains(t)));

            // 按时间范围
            if (query.SinceTick.HasValue)
                result = result.Where(e => e.Tick >= query.SinceTick.Value);
            if (query.UntilTick.HasValue)
                result = result.Where(e => e.Tick < query.UntilTick.Value);

            // 按 Actor
            if (!string.IsNullOrEmpty(query.ActorId))
                result = result.Where(e => e.Actors != null && e.Actors.Any(a => a.ID == query.ActorId));

            // 按严重度
            if (!string.IsNullOrEmpty(query.Severity))
                result = result.Where(e => e.Severity == query.Severity);

            // 按时间正序
            result = result.OrderBy(e => e.Tick);

            // 分页
            int offset = query.Offset ?? 0;
            if (offset > 0)
                result = result.Skip(offset);

            if (query.Limit.HasValue)
                result = result.Take(query.Limit.Value);

            return result.ToList();
        }

        /// <summary>从 recent 历史缓冲中统计数量。</summary>
        public int Count(EventQuery query)
        {
            if (query == null) return _recent.Count;

            var q = new EventQuery
            {
                TagsAny = query.TagsAny,
                TagsAll = query.TagsAll,
                SinceTick = query.SinceTick,
                UntilTick = query.UntilTick,
                ActorId = query.ActorId,
                Severity = query.Severity,
                Limit = null,
                Offset = null
            };
            return Query(q).Count;
        }

        /// <summary>最近一条事件（来自 recent 缓冲）。</summary>
        public IGameEvent Latest => _recent.Count > 0 ? _recent[_recent.Count - 1] : null;

        /// <summary>累计追加的事件总数。</summary>
        public int TotalAppended => _totalAppended;

        // ================================================================
        // Pool 专属操作
        // ================================================================

        /// <summary>pending 缓冲区中的事件数。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>pending 缓冲区中所有事件的重要度总和。</summary>
        public int TotalImportance => _totalImportance;

        /// <summary>获取 pending 缓冲区的只读快照（用于调试/日志）。</summary>
        public IReadOnlyList<IGameEvent> PendingEvents => _pending.AsReadOnly();

        /// <summary>获取 recent 历史缓冲的只读快照。</summary>
        public IReadOnlyList<IGameEvent> RecentEvents => _recent.AsReadOnly();

        /// <summary>
        /// 取出所有 pending 事件并清空缓冲区。
        /// 调用者获得事件所有权，池重置计数器和重要度。
        /// </summary>
        /// <returns>pending 事件的副本。</returns>
        public IReadOnlyList<IGameEvent> DrainPending()
        {
            var drained = new List<IGameEvent>(_pending);
            _pending.Clear();
            _totalImportance = 0;
            return drained;
        }

        /// <summary>
        /// 丢弃所有 pending 事件（导演不选中时使用）。
        /// </summary>
        public void ClearPending()
        {
            _pending.Clear();
            _totalImportance = 0;
        }

        // ================================================================
        // IDisposable
        // ================================================================

        /// <summary>清空所有缓冲区和状态。</summary>
        public void Dispose()
        {
            _pending.Clear();
            _recent.Clear();
            _totalImportance = 0;
            _totalAppended = 0;
        }
    }
}
