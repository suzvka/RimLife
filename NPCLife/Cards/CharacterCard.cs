using System.Collections.Generic;

namespace NPCLife.Cards
{
    /// <summary>
    /// 人物卡：聚合单个角色的身份元数据。
    /// 纯 DTO，零游戏依赖。各维度的语义描述由 ICharacterContentProvider 动态生成。
    /// </summary>
    public class CharacterCard : IExtensibleCard
    {
        // --- 基本元数据 ---
        public string ID;
        public string Name;
        public string FullName;
        public string DefName;
        public string FactionLabel;
        public string Gender;
        public string PawnType;     // "Character" / "Animal" / "Mechanoid" / "Insect" / "Other"
        public string PawnRelation; // "OurParty" / "Ally" / "Neutral" / "Enemy" / "Other"
        public bool IsDead;
        public bool IsAwake;

        /// <summary>扩展字段。</summary>
        public Dictionary<string, string> ExtensionFields { get; set; }
    }

    /// <summary>
    /// 社交互动流水记录。用于 InteractionHistoryStore 的 append-only 存储。
    /// 每条记录描述一次已发生的角色间互动。
    /// </summary>
    public struct InteractionRecord
    {
        /// <summary>发生时刻 (游戏 tick)。</summary>
        public int Tick;

        /// <summary>互动发起者 ID。</summary>
        public string InitiatorID;

        /// <summary>互动接受者 ID。</summary>
        public string RecipientID;

        /// <summary>互动定义名 (如 "Insult", "Chat")。</summary>
        public string InteractionDef;

        /// <summary>互动结果标签。</summary>
        public string Outcome;
    }

    /// <summary>
    /// 短期记忆详情：完整结构化条目，用于 full view。
    /// </summary>
    public struct ShortTermMemoryDetail
    {
        public int Tick;
        public string Type;
        public string Summary;
        public string RelatedPawnId;
    }

    /// <summary>
    /// 长期记忆详情：完整结构化条目，用于 full view。
    /// </summary>
    public struct LongTermMemoryDetail
    {
        public int ConsolidatedTick;
        public string Topic;
        public string Summary;
        public List<string> RelatedPawnIds;
    }
}
