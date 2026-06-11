using RimLife.Cards;
using System.Collections.Generic;

namespace RimLife.Core
{
    /// <summary>
    /// 事件日志抽象接口。提供 append-only 写入与按条件查询能力，
    /// 替代固定容量的 EventBuffer。
    /// </summary>
    public interface IEventLog
    {
        /// <summary>追加一条事件。</summary>
        void Append(IGameEvent evt);

        /// <summary>按条件查询事件（支持分页）。</summary>
        IReadOnlyList<IGameEvent> Query(EventQuery query);

        /// <summary>返回满足条件的总数（不受 Limit 限制）。</summary>
        int Count(EventQuery query);

        /// <summary>最近一条事件，无事件时返回 null。</summary>
        IGameEvent Latest { get; }

        /// <summary>累计追加的事件总数。</summary>
        int TotalAppended { get; }
    }
}
