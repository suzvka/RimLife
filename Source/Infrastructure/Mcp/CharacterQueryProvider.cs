using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
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
    public class CharacterQueryProvider : IMcpHookProvider
    {
        public string HookId => "character_query";
        public string HookName => "角色查询";
        public string HookDescription => "获取角色完整人物卡、按条件筛选殖民者、列出全部角色";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(CharacterQueryProvider).GetMethod(nameof(GetCharacterCard))),
                McpTool.FromMethod(typeof(CharacterQueryProvider).GetMethod(nameof(FindCharacters))),
                McpTool.FromMethod(typeof(CharacterQueryProvider).GetMethod(nameof(ListAllPawns))),
            };
        }

        // ================================================================
        // B. 角色深挖
        // ================================================================

        /// <summary>
        /// 获取指定角色的完整人物卡，按 view 分层控制数据量。
        /// </summary>
        [McpTool(Name = "get_character_card",
                 Description = "获取指定角色的完整人物卡。view 控制数据层级：\"static\"（默认）= 客观属性；\"dynamic\" = + 视角/记忆快照；\"full\" = + 完整记忆流水账。")]
        public static string GetCharacterCard(
            [McpParam(Description = "角色唯一 ID（ThingID）")] string pawnId,
            [McpParam(Description = "数据层级：static（默认，客观属性）/ dynamic（+视角/记忆快照）/ full（+完整记忆流水）",
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
                        string tier = NPCLife.Framework.SemanticLabels.MapMoodTier(mood);
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

        private static bool MatchesPawnType(Pawn p, string typeFilter)
        {
            if (p == null) return false;
            if (string.IsNullOrEmpty(typeFilter) || typeFilter == "All") return true;

            string lower = typeFilter.ToLowerInvariant();

            switch (lower)
            {
                case "colonist":
                    return p.IsColonist && !p.IsPrisoner;
                case "prisoner":
                    return p.IsPrisoner;
                case "guest":
                    return p.IsColonist == false && p.IsPrisoner == false
                        && p.Faction != null && p.Faction != Faction.OfPlayer
                        && !p.Faction.HostileTo(Faction.OfPlayer)
                        && p.RaceProps.Humanlike;
                case "animal":
                    return p.RaceProps.Animal;
                case "mechanoid":
                    return p.RaceProps.IsMechanoid;
                default:
                    return true;
            }
        }

        private static int GetPawnTypeOrder(Pawn p)
        {
            if (p.IsColonist && !p.IsPrisoner) return 0;
            if (p.IsPrisoner) return 1;
            if (p.RaceProps.Humanlike) return 2;
            if (p.RaceProps.Animal) return 3;
            if (p.RaceProps.IsMechanoid) return 4;
            return 5;
        }

        private static string SerializePawnSummary(Pawn p)
        {
            string pawnType;
            if (p.IsColonist && !p.IsPrisoner) pawnType = "Colonist";
            else if (p.IsPrisoner) pawnType = "Prisoner";
            else if (p.RaceProps.Animal) pawnType = "Animal";
            else if (p.RaceProps.IsMechanoid) pawnType = "Mechanoid";
            else if (p.RaceProps.Humanlike) pawnType = "Guest";
            else pawnType = "Other";

            var w = new NPCLife.Framework.JsonWriter(128);
            w.Prop("id", p.ThingID ?? "");
            w.Prop("name", p.Name?.ToStringShort ?? p.LabelShortCap ?? "?");
            w.Prop("pawnType", pawnType);
            w.Prop("factionLabel", p.Faction?.Name ?? "None");
            w.Prop("isDead", p.Dead);
            w.Prop("isDowned", p.Downed);
            return w.Close();
        }
    }
}
