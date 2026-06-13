using RimLife.Cards;
using RimLife.Mappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// MCP 工具共享辅助方法。提供 Pawn 查找、字符串解析、CharacterCard 构建等通用能力。
    /// 纯 static，供各 Provider 使用。
    /// </summary>
    public static class PawnQueryHelper
    {
        /// <summary>
        /// 通过 ThingID 在所有地图中查找 Pawn。
        /// </summary>
        public static Pawn FindPawnById(string id)
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
        /// 通过 ThingID 查找 Pawn 并获取其 HediffComp_PawnMemory。
        /// </summary>
        public static HediffComp_PawnMemory FindMemoryComp(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;

            var pawn = FindPawnById(pawnId);
            if (pawn?.health?.hediffSet == null) return null;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory");
            if (hediffDef == null) return null;

            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            return hediff?.TryGetComp<HediffComp_PawnMemory>();
        }

        /// <summary>
        /// 解析逗号分隔的字符串为列表。空或 null 输入返回空列表。
        /// </summary>
        public static List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 解析逗号分隔的标签字符串。空或 null 输入返回 null。
        /// </summary>
        public static IReadOnlyList<string> ParseTagList(string tags)
        {
            if (string.IsNullOrEmpty(tags)) return null;
            return tags.Split(new char[] { ',' })
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 按需构建 CharacterCard：sections 为空时全量，否则按需链式添加。
        /// </summary>
        public static CharacterCard BuildCharacterCard(Pawn pawn, string sections)
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
        /// 将 JSON 字符串列表拼接为 JSON 数组。
        /// </summary>
        public static string SerializeJsonArray(List<string> jsons)
        {
            if (jsons == null || jsons.Count == 0) return "[]";
            var sb = new StringBuilder("[");
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
