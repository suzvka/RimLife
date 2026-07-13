using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using RimLife.Data;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 角色查询 Skill 的 Hook Provider。
    /// 提供角色人物卡获取、按条件筛选殖民者、列出全部角色三个工具。
    /// </summary>
    [SkillDefinition(
        Id = "character_query",
        Name = "角色与环境查询",
        Description = "获取局部叙事事实",
        DefaultRoles = new[] { WorkspaceRole.Director, WorkspaceRole.Screenwriter, WorkspaceRole.Improviser })]
    public class CharacterQueryProvider : IMcpHookProvider
    {
        public string HookId => "character_query";
        public string HookName => "角色查询";
        public string HookDescription => "获取角色信息";
        
                /// <summary>首轮并发查询所有需了解的角色（static view），避免逐轮查询浪费 token。</summary>
                public string PromptInstruction => "";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(CharacterQueryProvider).GetMethod(nameof(GetCharacterCard))),
            };
        }

        // ================================================================
        // B. 角色深挖
        // ================================================================

        /// <summary>
        /// 获取指定角色的完整人物卡，按 view 分层控制数据量。
        /// </summary>
        [McpTool(Name = "get_character_card",
                 Description = "[评分-2] 获取指定角色的人物卡")]
        public static string GetCharacterCard(
            [McpParam(Description = "角色ID")] string pawnId,
            [McpParam(Description = "数据层级：static(从第三人称视角审视时用)/ dynamic(深入人物内心世界用)/ full(超级深度挖掘时用)",
                      Required = McpRequired.False)] string view = null)
        {
            try
            {
                var pawn = PawnQueryHelper.FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var card = PawnQueryHelper.BuildCharacterCard(pawn, view);
                return CardSerializer.Default.SerializeCharacterCard(card, view, RimLifeCore.ContentProviders);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] get_character_card({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 按条件筛选殖民者。
        /// </summary>
        [McpTool(Name = "find_characters",
                 Description = "按条件筛选角色")]
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
                        string tier = SemanticLabels.MapMoodTier(mood);
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

                    // 构建卡片：basic only，序列化时由 promptProvider 按需填充
                    var card = PawnQueryHelper.BuildCharacterCard(p, null);

                    results.Add(card);
                }

                // 序列化为列表（find_characters 始终使用 static view）
                var jsons = new List<string>();
                foreach (var c in results)
                    jsons.Add(CardSerializer.Default.SerializeCharacterCard(c, "static", RimLifeCore.ContentProviders));

                return PawnQueryHelper.SerializeJsonArray(jsons);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] find_characters failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // F. 角色普查
        // ================================================================

        /// <summary>
        /// 列出所有地图中的全部角色，按类型和派系分类。
        /// </summary>
        [McpTool(Name = "list_all_pawns",
                 Description = "列出殖民地中所有角色（殖民者/动物/囚犯/访客/机械体），按类型和派系分类。")]
        public static string ListAllPawns(
            [McpParam(Description = "角色类型过滤：Colonist/Animal/Prisoner/Guest/Mechanoid/All，默认 All",
                      Required = McpRequired.False)] string type = "All",
            [McpParam(Description = "最大返回数，默认 50")] int limit = 50)
        {
            try
            {
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p => p != null));
                }

                var filtered = allPawns.Where(p => MatchesPawnType(p, type)).ToList();

                // 按分类排序：殖民者 > 囚犯 > 访客 > 动物 > 机械体
                filtered.Sort((a, b) =>
                {
                    int typeOrderA = GetPawnTypeOrder(a);
                    int typeOrderB = GetPawnTypeOrder(b);
                    if (typeOrderA != typeOrderB) return typeOrderA.CompareTo(typeOrderB);
                    return string.Compare(a.LabelShortCap, b.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                });

                if (filtered.Count > limit)
                    filtered = filtered.Take(limit).ToList();

                var summaries = new List<string>();
                foreach (var p in filtered)
                    summaries.Add(SerializePawnSummary(p));

                return PawnQueryHelper.SerializeJsonArray(summaries);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] list_all_pawns failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        /// <summary>
        /// 获取所有可见角色的极简摘要（供上下文注入使用）。
        /// 返回 JSON 数组字符串，包含 id、name、pawnType、factionLabel、isDead、isDowned。
        /// </summary>
        public static string GetAllPawnsSummary(int limit = 50)
        {
            try
            {
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p => p != null));
                }

                // 按分类排序：殖民者 > 囚犯 > 访客 > 动物 > 机械体
                allPawns.Sort((a, b) =>
                {
                    int typeOrderA = GetPawnTypeOrder(a);
                    int typeOrderB = GetPawnTypeOrder(b);
                    if (typeOrderA != typeOrderB) return typeOrderA.CompareTo(typeOrderB);
                    return string.Compare(a.LabelShortCap, b.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                });

                if (allPawns.Count > limit)
                    allPawns = allPawns.Take(limit).ToList();

                var summaries = new List<string>();
                foreach (var p in allPawns)
                    summaries.Add(SerializePawnSummary(p));

                return PawnQueryHelper.SerializeJsonArray(summaries);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] GetAllPawnsSummary failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 按 ID 列表获取角色精简卡。用于上下文注入时只列出事件相关角色。
        /// 每卡约 150-200 Token，包含健康/心情/Top3技能。
        /// </summary>
        public static string GetPawnsByIds(IEnumerable<string> ids)
        {
            try
            {
                var summaries = new List<string>();
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    var pawn = PawnQueryHelper.FindPawnById(id);
                    if (pawn == null) continue;
                    summaries.Add(SerializePawnCondensed(pawn));
                }
                return PawnQueryHelper.SerializeJsonArray(summaries);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] GetPawnsByIds failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取所有可见角色的精简卡（供上下文注入使用）。
        /// 与 GetAllPawnsSummary 的区别：每卡附带健康/心情/Top3技能，帮助 LLM 做出更好的初始判断。
        /// </summary>
        public static string GetAllPawnsCondensed(int limit = 8)
        {
            try
            {
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p => p != null));
                }

                allPawns.Sort((a, b) =>
                {
                    int typeOrderA = GetPawnTypeOrder(a);
                    int typeOrderB = GetPawnTypeOrder(b);
                    if (typeOrderA != typeOrderB) return typeOrderA.CompareTo(typeOrderB);
                    return string.Compare(a.LabelShortCap, b.LabelShortCap, StringComparison.OrdinalIgnoreCase);
                });

                if (allPawns.Count > limit)
                    allPawns = allPawns.Take(limit).ToList();

                var cards = new List<string>();
                foreach (var p in allPawns)
                    cards.Add(SerializePawnCondensed(p));

                return PawnQueryHelper.SerializeJsonArray(cards);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.CharacterQueryProvider] GetAllPawnsCondensed failed: {e.Message}");
                return "[]";
            }
        }

        private static bool MatchesPawnType(Pawn p, string typeFilter)
        {
            if (p == null) return false;
            if (string.IsNullOrEmpty(typeFilter) || typeFilter == "All") return true;

            // 使用统一分类标签，与序列化/排序保持一致。
            // distinguishEnemy=true：确保 "Guest" 筛选不包含敌对角色。
            return string.Equals(
                GetPawnRoleLabel(p, distinguishEnemy: true),
                typeFilter,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 统一 Pawn 游戏角色分类标签。所有序列化/排序/筛选方法共用此唯一入口。
        /// </summary>
        /// <param name="distinguishEnemy">true 时将敌对 Humanlike 标为 "Enemy" 而非 "Guest"</param>
        private static string GetPawnRoleLabel(Pawn p, bool distinguishEnemy = false)
        {
            if (p.IsColonist && !p.IsPrisoner) return "Colonist";
            if (p.IsPrisoner) return "Prisoner";
            if (p.RaceProps.Animal) return "Animal";
            if (p.RaceProps.IsMechanoid) return "Mechanoid";
            if (p.RaceProps.Humanlike)
            {
                if (distinguishEnemy && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer))
                    return "Enemy";
                return "Guest";
            }
            return "Other";
        }

        private static int GetPawnTypeOrder(Pawn p)
        {
            switch (GetPawnRoleLabel(p))
            {
                case "Colonist":  return 0;
                case "Prisoner":  return 1;
                case "Guest":
                case "Enemy":     return 2;
                case "Animal":    return 3;
                case "Mechanoid": return 4;
                default:          return 5;
            }
        }

        private static string SerializePawnSummary(Pawn p)
        {
            var w = new NPCLife.Framework.JsonWriter(128);
            w.Prop("id", p.ThingID ?? "");
            w.Prop("name", p.Name?.ToStringShort ?? p.LabelShortCap ?? "?");
            w.Prop("pawnType", GetPawnRoleLabel(p));
            w.Prop("factionLabel", p.Faction?.Name ?? "None");
            return w.Close();
        }

        /// <summary>
        /// 序列化角色的极简摘要卡，供上下文注入使用。
        /// 仅含 id + name（~25 Token），其他数据通过伪造工具调用链注入。
        /// 与 SerializePawnSummary 的区别：对敌对 Humanlike 输出 "Enemy" 而非 "Guest"。
        /// </summary>
        private static string SerializePawnCondensed(Pawn p)
        {
            var w = new NPCLife.Framework.JsonWriter(96);
            w.Prop("id", p.ThingID ?? "");
            w.Prop("name", p.Name?.ToStringShort ?? p.LabelShortCap ?? "?");
            w.Prop("pawnType", GetPawnRoleLabel(p, distinguishEnemy: true));
            w.Prop("factionLabel", p.Faction?.Name ?? "None");
            return w.Close();
        }
    }
}
