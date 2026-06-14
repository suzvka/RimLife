using RimLife.Cards;
using RimLife.Core;
using System.Collections.Generic;

namespace RimLife.Framework.Mcp
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
        string SerializeCharacterCard(CharacterCard card, string view, IPawnPromptProvider promptProvider);
        string SerializeColonyContext(ColonyContext ctx);
        string SerializeObjective(ObjectiveCard obj);
        string SerializeObjectiveList(IReadOnlyList<ObjectiveCard> objectives);
        string SerializeEnvironment(EnvironmentCard env);
        string SerializeInteraction(InteractionRecord rec);
        string SerializeInteractionList(IReadOnlyList<InteractionRecord> records);
    }
}
