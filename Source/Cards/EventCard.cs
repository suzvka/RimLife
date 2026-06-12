using System.Collections.Generic;

namespace RimLife.Cards
{
    /// <summary>
    /// 游戏事件的标准接口。所有具体事件实现必须实现此接口。
    /// 纯 DTO 接口，零 RimWorld 依赖。
    /// 标签示例：["Raid", "Combat", "TribalSappers"] — 首标签为具体类型，后续为领域/子类型。
    /// </summary>
    public interface IGameEvent
    {
        /// <summary>事件唯一标识。</summary>
        string EventID { get; }

        /// <summary>事件定义名 (例如 "RaidEnemy", "QuestNode")。</summary>
        string DefName { get; }

        /// <summary>语义标签列表。LLM 消费者直接读字符串，无需枚举解析。</summary>
        IReadOnlyList<string> Tags { get; }

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
