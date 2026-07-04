using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using RimLife.Mappers;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 殖民地全局 Skill 的 Hook Provider。
    /// 提供殖民地概览、近期事件、活跃目标、资源库存四个工具。
    /// </summary>
    public class ColonyOverviewProvider : IMcpHookProvider
    {
        public string HookId => "colony_overview";
        public string HookName => "无";
        public string HookDescription => "";
                public string PromptInstruction => null;

        public IReadOnlyList<McpTool> GetTools()
        {
            return Array.Empty<McpTool>();
        }

        // ================================================================
        // A. 快速全局感知
        // ================================================================

        /// <summary>
        /// 获取殖民地全局快照：人口、财富、食物/电力状态、士气、威胁、派系关系、时间季节。
        /// 已由上下文注入替代，保留供测试调用。
        /// </summary>
        public static string GetColonyOverview()
        {
            try
            {
                var ctx = ColonyContextMapper.Create();
                return CardSerializer.Default.SerializeColonyContext(ctx);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.ColonyOverviewProvider] get_colony_overview failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 获取最近 N 条事件。
        /// </summary>
        [McpTool(Name = "get_recent_events",
                 Description = "获取最近 N 条事件，用于快速了解当前局势。")]
        public static string GetRecentEvents(
            [McpParam(Description = "返回条数，默认 10")] int limit = 10)
        {
            try
            {
                var eventLog = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (eventLog == null) return "[]";

                var all = eventLog.Query(EventQuery.All);
                int count = all.Count;
                if (count == 0) return "[]";

                int take = Math.Min(limit, count);
                var recent = new List<IGameEvent>(take);
                for (int i = count - take; i < count; i++)
                    recent.Add(all[i]);

                return CardSerializer.Default.SerializeEventList(recent);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.ColonyOverviewProvider] get_recent_events failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取当前所有活跃中的目标/任务。
        /// </summary>
        [McpTool(Name = "get_active_objectives",
                 Description = "获取当前所有活跃中的目标/任务，包括期限和进展。")]
        public static string GetActiveObjectives()
        {
            try
            {
                var objectives = ObjectiveCardMapper.GetActive();
                return CardSerializer.Default.SerializeObjectiveList(objectives);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.ColonyOverviewProvider] get_active_objectives failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // G. 资源库存
        // ================================================================

        /// <summary>
        /// 获取殖民地关键资源库存。
        /// </summary>
        [McpTool(Name = "get_resource_inventory",
                 Description = "获取殖民地关键资源库存：钢铁、木材、零件、药品、食物等。")]
        public static string GetResourceInventory(
            [McpParam(Description = "地图 ID，0=当前地图",
                      Required = McpRequired.False)] int mapId = 0)
        {
            try
            {
                Map map = mapId == 0 ? Find.CurrentMap
                    : Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null) return "{}";

                var keyResources = new HashSet<string>
                {
                    "Steel", "WoodLog", "Plasteel", "Gold", "Silver", "Uranium",
                    "ComponentIndustrial", "ComponentSpacer", "Chemfuel",
                    "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech",
                    "Neutroamine", "Cloth", "Synthread", "Hyperweave", "DevilstrandCloth"
                };

                var inventory = new Dictionary<string, int>();

                if (map.listerThings?.AllThings != null)
                {
                    foreach (var thing in map.listerThings.AllThings)
                    {
                        if (thing == null || thing.def == null) continue;
                        string defName = thing.def.defName;

                        if (keyResources.Contains(defName))
                        {
                            if (!inventory.ContainsKey(defName))
                                inventory[defName] = 0;
                            inventory[defName] += thing.stackCount;
                        }
                    }
                }

                // 额外统计食物总量
                int totalFood = 0;
                if (map.listerThings?.AllThings != null)
                {
                    foreach (var thing in map.listerThings.AllThings)
                    {
                        if (thing?.def?.IsNutritionGivingIngestible == true
                            && thing.def.ingestible?.HumanEdible == true)
                            totalFood += thing.stackCount;
                    }
                }
                inventory["_totalFood"] = totalFood;

                var w = new NPCLife.Framework.JsonWriter(256);
                foreach (var kv in inventory)
                    w.Prop(kv.Key, kv.Value);
                return w.Close();
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.ColonyOverviewProvider] get_resource_inventory failed: {e.Message}");
                return "{}";
            }
        }
    }
}
