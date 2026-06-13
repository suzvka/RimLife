using RimLife;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// Pawn 个体记忆 Skill 的 Hook Provider。
    /// 提供记忆查询、追加、心境写入三个工具。
    /// </summary>
    public class PawnMemoryProvider : IMcpHookProvider
    {
        public string HookId => "pawn_memory";
        public string HookName => "个体记忆";
        public string HookDescription => "查询、追加角色短期记忆，写入即时心境";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(GetMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(AddMemory))),
                McpTool.FromMethod(typeof(PawnMemoryProvider).GetMethod(nameof(UpdateMindset))),
            };
        }

        /// <summary>
        /// 查询指定角色的记忆。支持 summary（心境+回顾）和 full（全部四区）两种模式。
        /// </summary>
        [McpTool(Name = "get_memory",
                 Description = "查询指定角色的记忆。mode=\"summary\"返回即时心境和短期回顾（推荐，大多数场景足够）；mode=\"full\"返回全部四区记忆。")]
        public static string GetMemory(
            [McpParam(Description = "角色唯一 ID（ThingID）")] string pawnId,
            [McpParam(Description = "查询模式：\"summary\"（心境+回顾）或 \"full\"（全部四区），默认 summary",
                      Required = McpRequired.False)] string mode = "summary")
        {
            try
            {
                var comp = PawnQueryHelper.FindMemoryComp(pawnId);
                if (comp == null)
                    return "{\"error\":\"Pawn not found or no memory data\"}";

                bool fullMode = string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase);

                var w = new JsonWriter(fullMode ? 2048 : 512);

                // 凌驾层：即时心境（始终返回）
                w.Prop("mindset", comp.Mindset?.Content ?? "");
                w.Prop("mindsetTick", comp.Mindset?.LastUpdateTick ?? 0);

                // 短期回顾（始终返回）
                w.Prop("review", comp.Review?.Content ?? "");
                w.Prop("reviewTick", comp.Review?.LastUpdateTick ?? 0);

                w.Prop("shortTermCount", comp.ShortTermMemories.Count);
                w.Prop("longTermCount", comp.LongTermMemories.Count);

                if (fullMode)
                {
                    // STM 列表
                    var stmList = comp.GetRecentMemories(10);
                    var stmJsons = new List<string>();
                    foreach (var stm in stmList)
                    {
                        var sw = new JsonWriter(256);
                        sw.Prop("tick", stm.Tick);
                        sw.Prop("type", stm.Type ?? "");
                        sw.Prop("summary", stm.TruncatedSummary(200));
                        if (!string.IsNullOrEmpty(stm.RelatedPawnId))
                            sw.Prop("relatedPawnId", stm.RelatedPawnId);
                        stmJsons.Add(sw.Close());
                    }
                    w.ArrayRaw("shortTerm", stmJsons);

                    // LTM 列表
                    var ltmList = comp.GetKeyMemories(20);
                    var ltmJsons = new List<string>();
                    foreach (var ltm in ltmList)
                    {
                        var lw = new JsonWriter(512);
                        lw.Prop("topic", ltm.Topic ?? "");
                        lw.Prop("consolidatedTick", ltm.ConsolidatedTick);
                        lw.Prop("summary", ltm.TruncatedSummary(500));
                        if (ltm.RelatedPawnIds != null && ltm.RelatedPawnIds.Count > 0)
                            lw.Array("relatedPawnIds", ltm.RelatedPawnIds);
                        ltmJsons.Add(lw.Close());
                    }
                    w.ArrayRaw("longTerm", ltmJsons);
                }

                return w.Close();
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.PawnMemoryProvider] get_memory({pawnId}) failed: {e.Message}");
                return "{\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 手动为角色追加一条短期记忆（导演/编剧主动注入）。
        /// </summary>
        [McpTool(Name = "add_memory",
                 Description = "为指定角色手动追加一条短期记忆。可用于导演主动为角色注入经历（如获得灵感、目睹事件等）。")]
        public static string AddMemory(
            [McpParam(Description = "角色唯一 ID（ThingID）")] string pawnId,
            [McpParam(Description = "记忆类型：Interaction/Event/Combat/Observation/Milestone")] string type,
            [McpParam(Description = "记忆摘要（≤200字）")] string summary,
            [McpParam(Description = "关联角色 ID，可选",
                      Required = McpRequired.False)] string relatedPawnId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(summary))
                    return "{\"success\":false,\"error\":\"summary is required\"}";

                var comp = PawnQueryHelper.FindMemoryComp(pawnId);
                if (comp == null)
                    return "{\"success\":false,\"error\":\"Pawn not found or no memory data\"}";

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                var memory = new ShortTermMemory(currentTick, type, summary, relatedPawnId);
                comp.AddShortTerm(memory);

                return "{\"success\":true,\"tick\":" + currentTick + ",\"type\":" + JsonHelper.Quote(type ?? "") + "}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.PawnMemoryProvider] add_memory({pawnId}) failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 更新角色的即时心境（凌驾层）。由 LLM 自行判断何时调用。
        /// </summary>
        [McpTool(Name = "update_mindset",
                 Description = "更新指定角色的即时心境（凌驾层）。以第一人称描述角色当前的心理状态。LLM 自行判断何时调用，不依赖任何自动触发。")]
        public static string UpdateMindset(
            [McpParam(Description = "角色唯一 ID（ThingID）")] string pawnId,
            [McpParam(Description = "第一人称心境描述（≤200字）")] string content)
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
