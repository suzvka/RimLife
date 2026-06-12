using System;
using System.Collections.Generic;
using System.Linq;
using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using RimLife.Mappers;
using RimWorld;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 导演 Agent 的 MCP 工具集。提供第二轮交互所需的全部查询能力。
    /// 每个静态方法对应一个 MCP 工具，通过 [McpTool] / [McpParam] 标注。
    /// </summary>
    public static class DirectorMcpTools
    {
        // ================================================================
        // A. 快速全局感知
        // ================================================================

        /// <summary>
        /// 获取殖民地全局快照：人口、财富、食物/电力状态、士气、威胁、派系关系、时间季节。
        /// </summary>
        [McpTool(Name = "get_colony_overview",
                 Description = "获取殖民地全局快照：人口、财富、食物/电力状态、士气、威胁、派系关系、时间季节。")]
        public static string GetColonyOverview()
        {
            try
            {
                var ctx = ColonyContextMapper.Create();
                return CardSerializer.SerializeColonyContext(ctx);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_colony_overview failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 获取最近 N 条事件，可选按标签过滤。
        /// </summary>
        [McpTool(Name = "get_recent_events",
                 Description = "获取最近 N 条事件，用于快速了解当前局势。可选按标签过滤。")]
        public static string GetRecentEvents(
            [McpParam(Description = "返回条数，默认 10")] int limit = 10,
            [McpParam(Description = "过滤标签，如 Combat/Raid/Death。留空则不限制",
                      Required = McpRequired.False)] string tag = null)
        {
            try
            {
                var eventLog = RimLifeCore.EventLog;
                if (eventLog == null) return "[]";

                var query = new EventQuery();
                if (!string.IsNullOrEmpty(tag))
                    query.TagsAny = new List<string> { tag };

                // EventLog 按时间正序，取全部后从尾部截取最近 N 条
                var all = eventLog.Query(query);
                int count = all.Count;
                if (count == 0) return "[]";

                int take = Math.Min(limit, count);
                var recent = new List<IGameEvent>(take);
                for (int i = count - take; i < count; i++)
                    recent.Add(all[i]);

                return CardSerializer.SerializeEventList(recent);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_recent_events failed: {e.Message}");
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
                return CardSerializer.SerializeObjectiveList(objectives);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_active_objectives failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // B. 角色深挖
        // ================================================================

        /// <summary>
        /// 获取指定角色的完整人物卡，可选指定需要的子模块。
        /// </summary>
        [McpTool(Name = "get_character_card",
                 Description = "获取指定角色的完整人物卡。可选指定需要的子模块以节省上下文。")]
        public static string GetCharacterCard(
            [McpParam(Description = "角色唯一 ID（ThingID）")] string pawnId,
            [McpParam(Description = "需要的子模块：health/mood/skills/needs/activity/gear/backstory/social/perspective/psychology。逗号分隔，留空=全部",
                      Required = McpRequired.False)] string sections = null)
        {
            try
            {
                var pawn = FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var card = BuildCharacterCard(pawn, sections);
                return CardSerializer.SerializeCharacterCard(card, sections);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_character_card({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 按条件筛选殖民者。
        /// </summary>
        [McpTool(Name = "find_characters",
                 Description = "按条件筛选殖民者。可用于查找特定技能、心情状态或健康状态的角色。")]
        public static string FindCharacters(
            [McpParam(Description = "最低技能等级筛选，格式：技能名=等级，如 Shooting=10",
                      Required = McpRequired.False)] string minSkill = null,
            [McpParam(Description = "心情层级筛选：Excellent/Good/Neutral/Bad/Critical",
                      Required = McpRequired.False)] string moodTier = null,
            [McpParam(Description = "是否仅查找受伤角色",
                      Required = McpRequired.False)] bool injuredOnly = false,
            [McpParam(Description = "最大返回数，默认 10")] int limit = 10)
        {
            try
            {
                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists == null || colonists.Count == 0) return "[]";

                // 解析技能筛选
                string skillDefName = null;
                int skillLevel = 0;
                if (!string.IsNullOrEmpty(minSkill))
                {
                    var parts = minSkill.Split('=');
                    if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out skillLevel))
                    {
                        skillDefName = parts[0].Trim();
                    }
                }

                var results = new List<CharacterCard>();
                foreach (var p in colonists)
                {
                    if (p == null || p.Dead) continue;
                    if (results.Count >= limit) break;

                    // 心情筛选
                    if (!string.IsNullOrEmpty(moodTier))
                    {
                        float mood = p.needs?.mood?.CurLevelPercentage ?? 0.5f;
                        string tier = Framework.SemanticLabels.MapMoodTier(mood);
                        if (!string.Equals(tier, moodTier, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    // 受伤筛选
                    if (injuredOnly)
                    {
                        float pain = p.health?.hediffSet?.PainTotal ?? 0f;
                        if (pain <= 0.01f) continue;
                    }

                    // 技能筛选
                    if (!string.IsNullOrEmpty(skillDefName))
                    {
                        var skill = p.skills?.skills?.FirstOrDefault(
                            s => string.Equals(s.def?.defName, skillDefName, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(s.def?.label, skillDefName, StringComparison.OrdinalIgnoreCase));
                        if (skill == null || skill.Level < skillLevel) continue;
                    }

                    // 构建卡片：basic + 按需子模块
                    var card = CharacterCardMapper.CreateBasic(p);
                    if (!string.IsNullOrEmpty(skillDefName))
                        card.WithSkills(p);
                    if (!string.IsNullOrEmpty(moodTier))
                        card.WithMood(p);
                    if (injuredOnly)
                        card.WithHealth(p);

                    results.Add(card);
                }

                // 序列化为列表
                var jsons = new List<string>();
                foreach (var c in results)
                {
                    // 只序列化实际填充的 sections
                    var activeSections = new List<string>();
                    if (c.Skills != null) activeSections.Add("skills");
                    if (c.Mood != null) activeSections.Add("mood");
                    if (c.Health != null) activeSections.Add("health");
                    string secs = activeSections.Count > 0 ? string.Join(",", activeSections) : null;
                    jsons.Add(CardSerializer.SerializeCharacterCard(c, secs));
                }

                return SerializeJsonArray(jsons);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] find_characters failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // C. 事件回溯
        // ================================================================

        /// <summary>
        /// 多维事件历史查询。
        /// </summary>
        [McpTool(Name = "query_events",
                 Description = "多维事件历史查询。支持按标签(OR/AND)、时间范围、Actor、严重度筛选。")]
        public static string QueryEvents(
            [McpParam(Description = "OR标签：命中任一即匹配，逗号分隔。如 Combat,Raid",
                      Required = McpRequired.False)] string tagsAny = null,
            [McpParam(Description = "AND标签：必须全部命中，逗号分隔。如 Combat,Death",
                      Required = McpRequired.False)] string tagsAll = null,
            [McpParam(Description = "起始 tick（含）",
                      Required = McpRequired.False)] int? sinceTick = null,
            [McpParam(Description = "参与角色 ID",
                      Required = McpRequired.False)] string actorId = null,
            [McpParam(Description = "严重度：Minor/Major/Extreme",
                      Required = McpRequired.False)] string severity = null,
            [McpParam(Description = "最大返回数，默认 20")] int limit = 20)
        {
            try
            {
                var eventLog = RimLifeCore.EventLog;
                if (eventLog == null) return "[]";

                var query = new EventQuery
                {
                    TagsAny = ParseTagList(tagsAny),
                    TagsAll = ParseTagList(tagsAll),
                    SinceTick = sinceTick,
                    ActorId = !string.IsNullOrEmpty(actorId) ? actorId : null,
                    Severity = !string.IsNullOrEmpty(severity) ? severity : null,
                    Limit = limit
                };

                var results = eventLog.Query(query);
                return CardSerializer.SerializeEventList(results);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] query_events failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // D. 关系网络
        // ================================================================

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
                var pawn = FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var card = CharacterCardMapper.CreateBasic(pawn).WithSocial(pawn);
                if (card.Social == null) return "{}";

                return CardSerializer.SerializeSocial(card.Social);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_relationships({pawnId}) failed: {e.Message}");
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
                Log.Warning($"[RimLife.DirectorMcp] get_interaction_history({pawnId}) failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // E. 环境
        // ================================================================

        /// <summary>
        /// 获取指定角色当前所处环境信息。
        /// </summary>
        [McpTool(Name = "get_environment",
                 Description = "获取指定角色当前所处环境信息：室内外、温光、天气、房间评分。")]
        public static string GetEnvironment(
            [McpParam(Description = "角色唯一 ID")] string pawnId)
        {
            try
            {
                var pawn = FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var env = EnvironmentCardMapper.CreateFrom(pawn);
                return CardSerializer.SerializeEnvironment(env);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectorMcp] get_environment({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        /// <summary>
        /// 通过 ThingID 在所有地图中查找 Pawn。
        /// </summary>
        private static Pawn FindPawnById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.ThingID == id) return pawn;
                }
            }
            return null;
        }

        /// <summary>
        /// 按需构建 CharacterCard：sections 为空时全量，否则按需链式添加。
        /// </summary>
        private static CharacterCard BuildCharacterCard(Pawn pawn, string sections)
        {
            var card = CharacterCardMapper.CreateBasic(pawn);

            if (string.IsNullOrEmpty(sections))
            {
                return card
                    .WithHealth(pawn)
                    .WithMood(pawn)
                    .WithSkills(pawn)
                    .WithNeeds(pawn)
                    .WithActivity(pawn)
                    .WithGear(pawn)
                    .WithBackstory(pawn)
                    .WithSocial(pawn)
                    .WithPerspective(pawn)
                    .WithPsychology(pawn);
            }

            var parts = new HashSet<string>(
                sections.Split(new char[] { ',' }).Select(s => s.Trim().ToLowerInvariant()));

            if (parts.Contains("health")) card.WithHealth(pawn);
            if (parts.Contains("mood")) card.WithMood(pawn);
            if (parts.Contains("skills")) card.WithSkills(pawn);
            if (parts.Contains("needs")) card.WithNeeds(pawn);
            if (parts.Contains("activity")) card.WithActivity(pawn);
            if (parts.Contains("gear")) card.WithGear(pawn);
            if (parts.Contains("backstory")) card.WithBackstory(pawn);
            if (parts.Contains("social")) card.WithSocial(pawn);
            if (parts.Contains("perspective")) card.WithPerspective(pawn);
            if (parts.Contains("psychology")) card.WithPsychology(pawn);

            return card;
        }

        /// <summary>
        /// 解析逗号分隔的标签字符串为列表。
        /// </summary>
        private static IReadOnlyList<string> ParseTagList(string tags)
        {
            if (string.IsNullOrEmpty(tags)) return null;
            return tags.Split(new char[] { ',' })
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 将 JSON 字符串列表拼接为 JSON 数组。
        /// </summary>
        private static string SerializeJsonArray(List<string> jsons)
        {
            if (jsons == null || jsons.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < jsons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(jsons[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
