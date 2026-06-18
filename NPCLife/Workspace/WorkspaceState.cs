using NPCLife.Framework.Script;
using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// Agent 角色身份。决定对工作空间的操作权限。
    /// </summary>
    public enum WorkspaceRole
    {
        /// <summary>导演：管理分支结构（创建/分支/合并/关闭），不接触叙事内容。</summary>
        Director,

        /// <summary>编剧：创作叙事内容（push_round），无分支/合并权。</summary>
        Screenwriter,

        /// <summary>临时任务代理：处理日常对话、突发独立事件。与编剧同构（push_round），
        /// 但无剧情上下文/前情提要，无 signal 上报。由预注册定时器事件驱动快速响应。</summary>
        Freelancer
    }

    /// <summary>
    /// 工作空间状态枚举。
    /// </summary>
    public enum WorkspaceStatus
    {
        /// <summary>活跃中，编剧可继续推送回合。</summary>
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

        /// <summary>创作时刻 (格式化时间字符串，框架原样透传)。</summary>
        public string CreatedAt;

        /// <summary>本轮触发的事件 ID 列表。仅作溯源，不注入 prompt。</summary>
        public List<string> TriggerEventIds;

        /// <summary>本轮作者角色（Director/Screenwriter）。</summary>
        public WorkspaceRole AuthorRole;

        /// <summary>本轮作者标识（Agent ID，可选）。</summary>
        public string AuthorId;

        /// <summary>解析后的台词行列表。由 ScriptFormat.Parse 生成，游戏侧按此列表逐行显示。</summary>
        public List<ScriptLine> ScriptLines;
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

        /// <summary>创建此空间的 Agent 角色。</summary>
        public WorkspaceRole CreatedByRole;

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

        /// <summary>创建时刻 (格式化时间字符串，框架原样透传)。</summary>
        public string CreatedAt;

        /// <summary>最后活跃时刻 (格式化时间字符串，框架原样透传)。</summary>
        public string LastActivityAt;

        /// <summary>此工作空间激活的 Skill ID 列表。持久化以支持冷启动恢复。</summary>
        public List<string> ActiveSkillIds;

        /// <summary>工作空间内部事件池。Agent 的待处理素材。</summary>
        /// <summary>事件 KV 缓存：eventId → 序列化事件 JSON。编剧 drain 时消费，随 workspace 持久化。</summary>
        public Dictionary<string, string> EventCache;

        /// <summary>待处理事件 ID 的 FIFO 顺序列表。</summary>
        public List<string> PendingEventIds;

        /// <summary>pending 事件的累积重要度值（避免反序列化重算）。</summary>
        public float PendingImportance;

        /// <summary>结束原因描述（Completed / Abandoned 时填充）。</summary>
        public string Outcome;

        /// <summary>编剧给导演的留言。每轮 finish_round 时写入，导演查看所有活跃工作空间时可见。
        /// 内容：本轮剧情发展简述、是否可继续、期望接收的事件类型等。</summary>
        public string DirectorMessage;
    }
}
