using NPCLife.Cards;
using System;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 事件日志抽象接口。提供 append-only 写入、按条件查询，以及 pending 缓冲区的阈值激活能力。
    /// AgentLoop 依赖此接口获取待处理事件并订阅激活通知。
    /// 实现：WorkspaceEventPool。
    /// </summary>
    public interface IEventLog
    {
        // --- 日志写入与查询 ---

        /// <summary>追加一条事件。</summary>
        void Append(IGameEvent evt);

        /// <summary>按条件查询事件（支持分页）。</summary>
        IReadOnlyList<IGameEvent> Query(EventQuery query);

        /// <summary>返回满足条件的总数（不受 Limit 限制）。</summary>
        int Count(EventQuery query);

        /// <summary>最近一条事件，无事件时返回 null。</summary>
        IGameEvent Latest { get; }

        /// <summary>按事件 ID 查找事件（查 recent 缓冲区）。路由工具依赖此方法。</summary>
        IGameEvent GetById(string eventId);

        /// <summary>累计追加的事件总数。</summary>
        int TotalAppended { get; }

        // --- 池激活语义 ---

        /// <summary>pending 缓冲区中的事件数。</summary>
        int PendingCount { get; }

        /// <summary>pending 缓冲区中所有事件的重要度总和。</summary>
        float TotalImportance { get; }

        /// <summary>
        /// 取出所有 pending 事件并清空缓冲区。
        /// 调用者获得事件所有权，池重置计数器和重要度。
        /// </summary>
        IReadOnlyList<IGameEvent> DrainPending();

        /// <summary>当池状态变化且满足任一阈值时触发。订阅者（AgentLoop）被动激活。</summary>
        event Action OnThresholdReached;
    }
}
