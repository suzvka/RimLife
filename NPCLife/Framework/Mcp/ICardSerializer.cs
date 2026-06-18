using NPCLife.Cards;
using NPCLife.Core;
using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// Card DTO → JSON 序列化接口。
    /// AgentLoop 通过此接口解耦序列化实现，便于测试注入。
    /// Infrastructure 层可直接使用 <see cref="CardSerializer.Default"/> 静态实例。
    /// </summary>
    public interface ICardSerializer
    {
        string SerializeEvent(IGameEvent evt);
        string SerializeEventList(IReadOnlyList<IGameEvent> events);

        /// <summary>反序列化事件 JSON → IGameEvent。</summary>
        IGameEvent DeserializeEvent(string json);

        /// <summary>序列化事件 KV 缓存（供 WorkspaceState 持久化）。</summary>
        string SerializeEventCache(Dictionary<string, string> eventCache);

        /// <summary>反序列化事件 KV 缓存。</summary>
        Dictionary<string, string> DeserializeEventCache(string json);
        string SerializeCharacterCard(CharacterCard card, string view, IReadOnlyList<ICharacterContentProvider> contentProviders);
        string SerializeColonyContext(ColonyContext ctx);
        string SerializeObjective(ObjectiveCard obj);
        string SerializeObjectiveList(IReadOnlyList<ObjectiveCard> objectives);
        string SerializeEnvironment(EnvironmentCard env);
        string SerializeInteraction(InteractionRecord rec);
        string SerializeInteractionList(IReadOnlyList<InteractionRecord> records);
    }
}
