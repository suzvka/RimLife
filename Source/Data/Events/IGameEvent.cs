using System;
using System.Collections.Generic;

namespace RimLife
{
    /// <summary>
    /// 事件语义分类。
    /// </summary>
    public enum EventCategory
    {
        Combat,    // 战斗相关（袭击、战斗）
        Nature,    // 自然事件（天气、灾害）
        Social,    // 社交事件（结婚、争执）
        Quest,     // 任务事件
        Health,    // 健康事件（死亡、倒地、疾病）
        Economy,   // 经济事件（贸易、空投）
        Anomaly    // 异常事件
    }

    /// <summary>
    /// 游戏事件的标准接口。所有具体事件实现必须实现此接口。
    /// </summary>
    public interface IGameEvent
    {
        /// <summary>事件唯一标识。</summary>
        string EventID { get; }

        /// <summary>事件定义名 (例如 "RaidEnemy", "QuestNode")。</summary>
        string DefName { get; }

        /// <summary>语义分类。</summary>
        EventCategory Category { get; }

        /// <summary>发生时刻 (游戏 tick)。</summary>
        int Tick { get; }

        /// <summary>严重程度: "Minor"/"Major"/"Extreme"。</summary>
        string Severity { get; }

        /// <summary>涉及的实体引用列表。</summary>
        IReadOnlyList<EventActorRef> Actors { get; }

        /// <summary>空间提示 (例如 "殖民地西侧", "餐厅")。</summary>
        string MapHint { get; }

        /// <summary>松结构扩展参数 (事件特有的数据)。</summary>
        IDictionary<string, string> Payload { get; }
    }

    /// <summary>
    /// 事件涉及的实体引用。
    /// </summary>
    public struct EventActorRef
    {
        /// <summary>实体标识 (ThingID 或 Faction 名)。</summary>
        public string ID;

        /// <summary>显示名称。</summary>
        public string Name;

        /// <summary>角色: "Initiator"/"Target"/"Victim"/"Bystander"。</summary>
        public string Role;

        /// <summary>引用类型: "Pawn"/"Faction"/"Thing"。</summary>
        public string RefType;

        public static EventActorRef Pawn(string id, string name, string role)
        {
            return new EventActorRef
            {
                ID = id ?? "?",
                Name = name ?? "?",
                Role = role ?? "Bystander",
                RefType = "Pawn"
            };
        }

        public static EventActorRef Faction(string name, string role)
        {
            return new EventActorRef
            {
                ID = name ?? "?",
                Name = name ?? "?",
                Role = role ?? "Bystander",
                RefType = "Faction"
            };
        }
    }
}
