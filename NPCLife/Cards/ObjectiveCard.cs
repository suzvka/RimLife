using System.Collections.Generic;

namespace NPCLife.Cards
{
    /// <summary>
    /// 目标卡：通用抽象——导演 Agent 在任何游戏中都需要「当前被追踪的目标」信息。
    /// RimWorld 的 Quest 系统仅为其中一个数据来源（Source = "QuestSystem"）。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class ObjectiveCard : IExtensibleCard
    {
        /// <summary>目标唯一标识。</summary>
        public string ID;

        /// <summary>目标标题。</summary>
        public string Title;

        /// <summary>目标描述。</summary>
        public string Description;

        /// <summary>"Active" / "Completed" / "Failed" / "Expired"</summary>
        public string Status;

        /// <summary>数据来源: "QuestSystem" / "ColonyNeed" / "AgentInferred"</summary>
        public string Source;

        /// <summary>截止时间，null 表示无时限。</summary>
        public string Deadline;

        /// <summary>子步骤进展。</summary>
        public IReadOnlyList<ObjectiveStepEntry> Steps;

        /// <summary>扩展字段。</summary>
        public Dictionary<string, string> ExtensionFields { get; set; }
    }

    public struct ObjectiveStepEntry
    {
        /// <summary>步骤标签。</summary>
        public string Label;

        /// <summary>是否已完成。</summary>
        public bool IsCompleted;
    }
}
