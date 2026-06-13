using System.Collections.Generic;

namespace RimLife.Workspace
{
    /// <summary>
    /// 工作空间状态枚举。
    /// </summary>
    public enum WorkspaceStatus
    {
        /// <summary>活跃中，导演可继续推送回合。</summary>
        Active,

        /// <summary>暂时挂起，保留数据但暂停回合推送。</summary>
        Suspended,

        /// <summary>剧情线已完结。</summary>
        Completed,

        /// <summary>已废弃/放弃。</summary>
        Abandoned
    }

    /// <summary>
    /// 一个轮次的类型。Normal 为常规叙事轮，Branch/Merge 为结构轮（仅含 recap）。
    /// </summary>
    public enum RoundType
    {
        /// <summary>常规叙事轮：含前情提要和正式台词。</summary>
        Normal,

        /// <summary>分支声明轮：仅含分支前情提要，无台词。</summary>
        Branch,

        /// <summary>合并声明轮：仅含合并前情提要，无台词。</summary>
        Merge
    }

    /// <summary>
    /// 工作空间中单个轮次的 Agent 写作日志。
    /// 不存储事件数据（事件由 EventLog 权威管理），只存 Agent 自己的创作输出。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public struct WorkspaceRound
    {
        /// <summary>轮次序号，从 0 开始递增。</summary>
        public int Seq;

        /// <summary>轮次类型。</summary>
        public RoundType Type;

        /// <summary>前情提要：Agent 对本轮叙事起点的总结。</summary>
        public string Recap;

        /// <summary>正式台词（Branch/Merge 轮为空）。</summary>
        public string Narrative;

        /// <summary>创作时刻 (游戏 tick)。</summary>
        public int CreatedAtTick;

        /// <summary>本轮触发的事件 ID 列表。仅作溯源，不注入 prompt。</summary>
        public List<string> TriggerEventIds;
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

        /// <summary>Agent 写作日志：按轮次的 recap + narrative 列表。</summary>
        public List<WorkspaceRound> Rounds;

        /// <summary>最新一期前情提要。注入下一轮 prompt 的唯一上下文窗口。</summary>
        public string CurrentRecap;

        /// <summary>创建时刻 (游戏 tick)。</summary>
        public int CreatedAtTick;

        /// <summary>最后活跃时刻 (游戏 tick)。</summary>
        public int LastActivityTick;

        /// <summary>此工作空间激活的 Skill ID 列表。持久化以支持冷启动恢复。</summary>
        public List<string> ActiveSkillIds;

        /// <summary>结束原因描述（Completed / Abandoned 时填充）。</summary>
        public string Outcome;
    }
}
