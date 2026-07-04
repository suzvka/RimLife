using RimLife;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// Pawn 个体记忆 Skill 的 Hook Provider。
    /// 提供记忆追加、心境写入两个工具。记忆查询通过 get_character_card 的 memory section 提供，此处仅负责写入。
    /// </summary>
    public class PawnMemoryProvider : IMcpHookProvider
    {
        public string HookId => "pawn_memory";
        public string HookName => "个体记忆";
        public string HookDescription => "追加角色短期记忆，写入即时心境";
                public string PromptInstruction => "用这些工具，让脚本能够实际影响游戏进程";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddInteractionMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddEventMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddCombatMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddObservationMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddMilestoneMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(UpdateMindset))),
            };
        }

        /// <summary>
        /// 内部共享实现：为角色追加一条短期记忆。
        /// </summary>
        private static string AddMemoryInternal(
            string pawnId,
            string memoryType,
            string summary,
            string relatedPawnId)
        {
            try
            {
                if (string.IsNullOrEmpty(summary))
                    return "{\"success\":false,\"error\":\"summary is required\"}";

                var comp = PawnQueryHelper.FindMemoryComp(pawnId);
                if (comp == null)
                    return "{\"success\":false,\"error\":\"Pawn not found or no memory data\"}";

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                var memory = new ShortTermMemory(currentTick, memoryType, summary, relatedPawnId);
                comp.AddShortTerm(memory);

                return "{\"success\":true,\"tick\":" + currentTick + ",\"type\":" + JsonHelper.Quote(memoryType ?? "") + "}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.PawnMemoryProvider] add_memory({pawnId}, {memoryType}) failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 为角色追加一条社交互动记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_interaction_memory",
                 Description = "[+2] 角色会记住的社交互动")]
        public static string AddInteractionMemory(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "互动内容描述")] string summary,
            [McpParam(Description = "互动的对象角色 ID，可选",
                      Required = McpRequired.False)] string relatedPawnId = null)
        {
            return AddMemoryInternal(pawnId, "Interaction", summary, relatedPawnId);
        }

        /// <summary>
        /// 为角色追加一条事件记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_event_memory",
                 Description = "[+2] 角色会记住的重要事件")]
        public static string AddEventMemory(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "事件内容描述")] string summary)
        {
            return AddMemoryInternal(pawnId, "Event", summary, null);
        }

        /// <summary>
        /// 为角色追加一条战斗记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_combat_memory",
                 Description = "[+2] 角色会记住的战斗经历")]
        public static string AddCombatMemory(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "战斗内容描述")] string summary,
            [McpParam(Description = "交战的角色 ID，可选",
                      Required = McpRequired.False)] string relatedPawnId = null)
        {
            return AddMemoryInternal(pawnId, "Combat", summary, relatedPawnId);
        }

        /// <summary>
        /// 为角色追加一条观察记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_observation_memory",
                 Description = "[+2] 角色会记住的观察到的现象")]
        public static string AddObservationMemory(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "观察内容描述")] string summary)
        {
            return AddMemoryInternal(pawnId, "Observation", summary, null);
        }

        /// <summary>
        /// 为角色追加一条里程碑记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_milestone_memory",
                 Description = "[+2] 角色会记住的人生里程碑")]
        public static string AddMilestoneMemory(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "里程碑内容描述")] string summary)
        {
            return AddMemoryInternal(pawnId, "Milestone", summary, null);
        }

        /// <summary>
        /// 更新角色的即时心境（凌驾层）。由 LLM 自行判断何时调用。
        /// </summary>
        [McpTool(Name = "update_mindset",
                 Description = "[+2] 覆盖更新角色心境自述")]
        public static string UpdateMindset(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "内容")] string content)
        {
            try
            {
                if (string.IsNullOrEmpty(content))
                    return "{\"success\":false,\"error\":\"content is required\"}";

                var comp = PawnQueryHelper.FindMemoryComp(pawnId);
                if (comp == null)
                    return "{\"success\":false,\"error\":\"Pawn not found or no memory data\"}";

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                comp.UpdateMindset(content, currentTick);

                return "{\"success\":true,\"tick\":" + currentTick + "}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.PawnMemoryProvider] update_mindset({pawnId}) failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }
    }
}
