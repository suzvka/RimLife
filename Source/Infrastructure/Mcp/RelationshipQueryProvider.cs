using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 关系网络 Skill 的 Hook Provider。
    /// 提供社交关系查询和交互历史流水查询。
    /// </summary>
    public class RelationshipQueryProvider : IMcpHookProvider
    {
        public string HookId => "relationship_query";
        public string HookName => "关系网络";
        public string HookDescription => "查询角色社交关系、交互历史流水";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(RelationshipQueryProvider).GetMethod(nameof(GetRelationships))),
                McpTool.FromMethod(typeof(RelationshipQueryProvider).GetMethod(nameof(GetInteractionHistory))),
            };
        }

        /// <summary>
        /// 获取指定角色与其他人的社交关系。
        /// </summary>
        [McpTool(Name = "get_relationships",
                 Description = "获取指定角色与其他人的社交关系：关系类型、好感度、好感层级。")]
        public static string GetRelationships(
            [McpParam(Description = "角色唯一 ID")] string pawnId)
        {
            try
            {
                var pawn = PawnQueryHelper.FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var prompt = RimLifeCore.PromptProvider?.GetSectionPrompt(pawn, "social");
                return prompt ?? "{}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.RelationshipQueryProvider] get_relationships({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 获取指定角色近期的社交互动流水记录。
        /// </summary>
        [McpTool(Name = "get_interaction_history",
                 Description = "获取指定角色近期的社交互动流水记录，用于理解角色间动态。")]
        public static string GetInteractionHistory(
            [McpParam(Description = "角色唯一 ID")] string pawnId,
            [McpParam(Description = "起始 tick（含），默认 5000 ticks 前",
                      Required = McpRequired.False)] int? sinceTick = null,
            [McpParam(Description = "最大返回数，默认 20")] int limit = 20)
        {
            try
            {
                var store = RimLifeCore.InteractionStore;
                if (store == null) return "[]";

                var records = store.QueryByPawn(pawnId, sinceTick, limit);
                return CardSerializer.SerializeInteractionList(records);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.RelationshipQueryProvider] get_interaction_history({pawnId}) failed: {e.Message}");
                return "[]";
            }
        }
    }
}
