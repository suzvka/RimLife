using System.Collections.Generic;
using RimLife.Cards;

namespace RimLife.Workspace
{
    /// <summary>
    /// 工作空间状态枚举。
    /// </summary>
    public enum WorkspaceStatus
    {
        /// <summary>活跃中，导演可继续推送事件。</summary>
        Active,

        /// <summary>暂时挂起，保留数据但暂停事件推送。</summary>
        Suspended,

        /// <summary>剧情线已完结。</summary>
        Completed,

        /// <summary>已废弃/放弃。</summary>
        Abandoned
    }

    /// <summary>
    /// 工作空间内存储的事件快照。从 IGameEvent 展平复制，保证上下文自包含。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public struct WorkspaceEvent
    {
        /// <summary>原始事件 ID。</summary>
        public string EventId;

        /// <summary>事件定义名。</summary>
        public string DefName;

        /// <summary>语义标签列表。</summary>
        public List<string> Tags;

        /// <summary>发生时刻 (游戏 tick)。</summary>
        public int Tick;

        /// <summary>严重程度: "Minor"/"Major"/"Extreme"。</summary>
        public string Severity;

        /// <summary>涉及的实体引用列表。</summary>
        public List<EventActorRef> Actors;

        /// <summary>空间提示。</summary>
        public string MapHint;

        /// <summary>松结构扩展参数。</summary>
        public Dictionary<string, string> Payload;

        /// <summary>
        /// 从 IGameEvent 创建快照副本。
        /// </summary>
        public static WorkspaceEvent From(IGameEvent evt)
        {
            if (evt == null) return default;

            return new WorkspaceEvent
            {
                EventId = evt.EventID ?? "?",
                DefName = evt.DefName ?? "?",
                Tags = evt.Tags != null ? new List<string>(evt.Tags) : new List<string>(),
                Tick = evt.Tick,
                Severity = evt.Severity ?? "Minor",
                Actors = evt.Actors != null ? new List<EventActorRef>(evt.Actors) : new List<EventActorRef>(),
                MapHint = evt.MapHint ?? "",
                Payload = evt.Payload != null
                    ? new Dictionary<string, string>(evt.Payload)
                    : new Dictionary<string, string>()
            };
        }
    }

    /// <summary>
    /// 单个工作空间（上下文空间）的状态描述。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class WorkspaceState
    {
        /// <summary>工作空间唯一标识。</summary>
        public string Id;

        /// <summary>人类可读标签。</summary>
        public string Label;

        /// <summary>当前状态。</summary>
        public WorkspaceStatus Status;

        /// <summary>分支来源工作空间 ID（null 表示根空间）。</summary>
        public string ParentId;

        /// <summary>合并来源工作空间 ID 列表。</summary>
        public List<string> MergedFromIds;

        /// <summary>关联的殖民者 ThingID 列表。</summary>
        public List<string> ColonistIds;

        /// <summary>语义标签（如 "RaidAftermath", "RomanceArc"）。</summary>
        public List<string> Tags;

        /// <summary>复制进来的事件数据快照列表。</summary>
        public List<WorkspaceEvent> PinnedEvents;

        /// <summary>创建时刻 (游戏 tick)。</summary>
        public int CreatedAtTick;

        /// <summary>最后活跃时刻 (游戏 tick)。</summary>
        public int LastActivityTick;

        /// <summary>结束原因描述（Completed / Abandoned 时填充）。</summary>
        public string Outcome;
    }
}
