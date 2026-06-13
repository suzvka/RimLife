using RimLife.Core;
using RimLife.Framework;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IPawnPromptProvider 游戏侧实现。
    /// 将 RimWorld Pawn 的各维度数据转化为自然语言纯文本（非 JSON），
    /// 可直接嵌入 prompt 或传给 LLM。
    /// </summary>
    public class PawnPromptProvider : IPawnPromptProvider
    {
        public string GetCharacterPrompt(string pawnId, string view)
        {
            var pawn = ResolvePawn(pawnId);
            if (pawn == null) return null;

            bool isDynamic = string.Equals(view, "dynamic", StringComparison.OrdinalIgnoreCase);
            bool isFull = string.Equals(view, "full", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder(4096);

            // === Static layer (always included) ===
            AppendIfNotNull(sb, "【健康】", SerializeHealth(pawn));
            AppendIfNotNull(sb, "【心情】", SerializeMood(pawn));
            AppendIfNotNull(sb, "【技能】", SerializeSkills(pawn));
            AppendIfNotNull(sb, "【需求】", SerializeNeeds(pawn));
            AppendIfNotNull(sb, "【活动】", SerializeActivity(pawn));
            AppendIfNotNull(sb, "【装备】", SerializeGear(pawn));
            AppendIfNotNull(sb, "【背景】", SerializeBackstory(pawn));
            AppendIfNotNull(sb, "【社交】", SerializeSocial(pawn));
            AppendIfNotNull(sb, "【人格】", SerializePsychology(pawn));

            // === Dynamic layer ===
            if (isDynamic || isFull)
            {
                AppendIfNotNull(sb, "【视野】", SerializePerspective(pawn));
                AppendIfNotNull(sb, "【记忆】", SerializeMemory(pawn, isFull));
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
        }

        public string GetSocialPrompt(string pawnId)
        {
            var pawn = ResolvePawn(pawnId);
            if (pawn == null) return null;
            return SerializeSocial(pawn);
        }

        private static Pawn ResolvePawn(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;
            return Mcp.PawnQueryHelper.FindPawnById(pawnId);
        }

        private static void AppendIfNotNull(StringBuilder sb, string header, string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(header);
                sb.Append(text);
            }
        }

        // ================================================================
        // Health
        // ================================================================

        private static readonly PawnCapacityDef[] KeyCapacityDefs =
        {
            PawnCapacityDefOf.Moving,
            PawnCapacityDefOf.Manipulation,
            PawnCapacityDefOf.Talking,
            PawnCapacityDefOf.Consciousness,
            PawnCapacityDefOf.Sight,
            PawnCapacityDefOf.Hearing,
            PawnCapacityDefOf.Breathing
        };

        private static string SerializeHealth(Pawn p)
        {
            if (p?.health == null) return null;

            var sb = new StringBuilder(256);
            float pain = p.health.hediffSet?.PainTotal ?? 0f;
            float bleed = p.health.hediffSet?.BleedRateTotal ?? 0f;

            sb.Append("疼痛: ");
            sb.Append(SemanticLabels.MapPainTier(pain));
            sb.Append(", 流血: ");
            sb.Append(SemanticLabels.MapBleedSeverity(bleed));

            // 关键能力
            if (p.health.capacities != null)
            {
                foreach (var def in KeyCapacityDefs)
                {
                    try
                    {
                        float level = p.health.capacities.GetLevel(def);
                        string tier = SemanticLabels.MapCapacityTier(Mathf.Clamp01(level));
                        if (tier != "Normal" || level < 0.9f)
                        {
                            sb.Append(", ");
                            sb.Append(def.label ?? def.defName);
                            sb.Append(": ");
                            sb.Append(tier);
                        }
                    }
                    catch { }
                }
            }

            // 受伤
            var hediffs = p.health.hediffSet?.hediffs;
            if (hediffs != null)
            {
                var injuries = new List<string>();
                foreach (var h in hediffs)
                {
                    if (h == null || !h.Visible) continue;
                    string part = h.Part?.Label ?? "";
                    string label = h.def?.label ?? h.LabelCap;
                    string flags = "";
                    if (h.Bleeding) flags += "🩸";
                    if (h.IsPermanent()) flags += "永久";
                    if (h.def?.isInfection ?? false) flags += "感染";

                    string entry = string.IsNullOrEmpty(flags)
                        ? $"{part}·{label}"
                        : $"{part}·{label}({flags})";
                    injuries.Add(entry);
                }
                if (injuries.Count > 0)
                {
                    sb.Append("; 受伤: ");
                    sb.Append(string.Join(", ", injuries));
                }
            }

            return sb.ToString();
        }

        // ================================================================
        // Mood
        // ================================================================

        private static string SerializeMood(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike) return null;

            var sb = new StringBuilder(256);
            float moodLevel = p.needs?.mood?.CurLevelPercentage ?? 0f;
            sb.Append("心情: ");
            sb.Append(SemanticLabels.MapMoodTier(moodLevel));

            if (p.InMentalState)
            {
                string msLabel = p.MentalState?.def?.label ?? p.MentalState?.InspectLine;
                if (!string.IsNullOrEmpty(msLabel))
                {
                    sb.Append(" [");
                    sb.Append(msLabel);
                    sb.Append(']');
                }
            }

            // 特性
            var storyTraits = p.story?.traits?.allTraits;
            if (storyTraits != null && storyTraits.Any())
            {
                var traitStrs = new List<string>();
                foreach (var t in storyTraits)
                {
                    if (t == null) continue;
                    traitStrs.Add($"{t.LabelCap}[{t.Degree:+0;-0}]");
                }
                sb.Append("; 特性: ");
                sb.Append(string.Join(", ", traitStrs));
            }

            // 活跃想法
            var allThoughts = new List<Thought>();
            try { p.needs?.mood?.thoughts?.GetAllMoodThoughts(allThoughts); }
            catch { }
            if (allThoughts.Count > 0)
            {
                var thoughtStrs = allThoughts.Take(5).Select(t =>
                {
                    float offset = 0f;
                    try { offset = t.MoodOffset(); } catch { }
                    return $"{t.LabelCap}({offset:+0.#;-0.#})";
                });
                sb.Append("; 想法: ");
                sb.Append(string.Join(", ", thoughtStrs));
            }

            return sb.ToString();
        }

        // ================================================================
        // Skills
        // ================================================================

        private static string SerializeSkills(Pawn p)
        {
            if (p?.skills == null) return null;

            var skills = p.skills.skills;
            if (skills == null || skills.Count == 0) return null;

            var parts = new List<string>();
            foreach (var sr in skills)
            {
                if (sr == null || sr.def == null) continue;
                string passionStr;
                try
                {
                    passionStr = sr.passion switch
                    {
                        Passion.Major => "🔥",
                        Passion.Minor => "🔥?",
                        _ => ""
                    };
                }
                catch { passionStr = ""; }

                string entry = sr.TotallyDisabled
                    ? $"{sr.def.label ?? sr.def.defName} 禁用"
                    : $"{sr.def.label ?? sr.def.defName} {sr.Level}{passionStr}";
                parts.Add(entry);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // ================================================================
        // Needs
        // ================================================================

        private static string SerializeNeeds(Pawn p)
        {
            if (p?.needs == null) return null;

            var allNeeds = p.needs.AllNeeds;
            if (allNeeds == null || allNeeds.Count == 0) return null;

            var parts = new List<string>();
            foreach (var need in allNeeds)
            {
                if (need == null) continue;
                try
                {
                    float cur = need.CurLevelPercentage;
                    string urgency = SemanticLabels.MapNeedUrgency(need.def?.defName, cur);
                    if (urgency != "Normal" || cur < 0.5f)
                    {
                        parts.Add($"{need.LabelCap}: {urgency}");
                    }
                }
                catch { }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // ================================================================
        // Activity
        // ================================================================

        private static string SerializeActivity(Pawn p)
        {
            if (p == null || p.jobs == null) return null;

            var sb = new StringBuilder(128);
            try
            {
                sb.Append("姿态: ");
                sb.Append(p.GetPosture().ToString());
            }
            catch { sb.Append("姿态: ?"); }

            var curJob = p.CurJob;
            if (curJob != null)
            {
                try
                {
                    sb.Append(", 当前: ");
                    sb.Append(curJob.GetReport(p));
                }
                catch { }
            }

            var jobQueue = p.jobs.jobQueue;
            if (jobQueue != null && jobQueue.Count > 0)
            {
                var queueParts = new List<string>();
                foreach (var j in jobQueue)
                {
                    if (j?.job == null) continue;
                    try { queueParts.Add(j.job.GetReport(p)); }
                    catch { }
                }
                if (queueParts.Count > 0)
                {
                    sb.Append(", 队列: ");
                    sb.Append(string.Join(" → ", queueParts.Take(3)));
                }
            }

            return sb.ToString();
        }

        // ================================================================
        // Gear
        // ================================================================

        private static string SerializeGear(Pawn p)
        {
            if (p == null) return null;

            var sb = new StringBuilder(256);

            var worn = p.apparel?.WornApparel;
            if (worn != null && worn.Any())
            {
                var parts = worn.Select(t => FormatGearItem(t));
                sb.Append("穿着: ");
                sb.Append(string.Join(", ", parts));
            }

            var inv = p.inventory?.innerContainer;
            if (inv != null && inv.Any())
            {
                if (sb.Length > 0) sb.Append("; ");
                var parts = inv.Select(t => FormatGearItem(t));
                sb.Append("背包: ");
                sb.Append(string.Join(", ", parts));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string FormatGearItem(Thing thing)
        {
            string quality = thing.TryGetQuality(out var qc) ? qc.ToString() : "";
            float durability = thing.def.useHitPoints ? (float)thing.HitPoints / thing.MaxHitPoints : 1f;
            string condition = SemanticLabels.MapGearCondition(durability);

            string label = thing.LabelCap;
            string extra = !string.IsNullOrEmpty(quality) ? $"{quality}, {condition}" : condition;

            if (thing.stackCount > 1)
                return $"{label} ×{thing.stackCount}({extra})";
            else
                return $"{label}({extra})";
        }

        // ================================================================
        // Backstory
        // ================================================================

        private static string SerializeBackstory(Pawn p)
        {
            if (p?.story == null) return null;
            if (p.story.Childhood == null && p.story.Adulthood == null) return null;

            var parts = new List<string>();
            if (p.story.Childhood != null)
                parts.Add($"童年: {p.story.Childhood.title}——{p.story.Childhood.description}");
            if (p.story.Adulthood != null)
                parts.Add($"成年: {p.story.Adulthood.title}——{p.story.Adulthood.description}");

            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        // ================================================================
        // Social
        // ================================================================

        private static string SerializeSocial(Pawn p)
        {
            if (p?.relations == null) return null;

            var directRelations = p.relations.DirectRelations;
            if (directRelations == null || directRelations.Count == 0) return null;

            var parts = new List<string>();
            foreach (var dr in directRelations)
            {
                if (dr?.otherPawn == null) continue;
                try
                {
                    var other = dr.otherPawn;
                    float opinion = p.relations.OpinionOf(other);
                    string tier = SemanticLabels.MapOpinionTier(opinion);
                    string name = other.Name?.ToStringShort ?? other.LabelShortCap ?? "?";

                    parts.Add($"{name}: {dr.def?.defName ?? "?"}({tier})");
                }
                catch { }
            }

            if (parts.Count == 0) return null;

            // 殖民地平均好感
            float colonyAvg = CalculateColonyOpinionAverage(p);
            string avgTier = SemanticLabels.MapOpinionTier(colonyAvg);
            parts.Add($"殖民地平均: {avgTier}");

            return string.Join(", ", parts);
        }

        private static float CalculateColonyOpinionAverage(Pawn p)
        {
            try
            {
                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists == null || colonists.Count == 0) return 0f;
                float sum = 0f;
                int count = 0;
                foreach (var c in colonists)
                {
                    if (c == p) continue;
                    if (c?.relations == null) continue;
                    try { sum += c.relations.OpinionOf(p); count++; }
                    catch { }
                }
                return count > 0 ? sum / count : 0f;
            }
            catch { return 0f; }
        }

        // ================================================================
        // Perspective
        // ================================================================

        private static string SerializePerspective(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null) return null;

            Map map = pawn.Map;
            var visible = new List<(string name, float dist)>();
            var allPawns = map.mapPawns.AllPawnsSpawned;
            foreach (var target in allPawns)
            {
                if (target == pawn) continue;
                if (target?.Position == null) continue;
                float dist = target.Position.DistanceTo(pawn.Position);
                if (dist > 26f) continue;
                if (!GenSight.LineOfSight(pawn.Position, target.Position, map, skipFirstCell: true)) continue;

                string name = target.Name?.ToStringShort ?? target.LabelShortCap ?? "?";
                visible.Add((name, dist));
            }

            if (visible.Count == 0) return null;

            var parts = visible
                .OrderBy(v => v.dist)
                .Take(10)
                .Select(v => $"{v.name}({v.dist:F1}m)");
            return "视野内: " + string.Join(", ", parts);
        }

        // ================================================================
        // Psychology
        // ================================================================

        private static string SerializePsychology(Pawn p)
        {
            if (p?.story?.traits == null) return null;

            // 从 Traits 计算基础五维向量
            var baseVec = new Cards.BigFiveVector();
            var storyTraits = p.story.traits.allTraits;
            if (storyTraits != null)
            {
                foreach (var trait in storyTraits)
                {
                    if (trait?.def?.defName == null) continue;
                    TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(trait.def.defName);
                    if (def == null) continue;
                    var ext = def.GetModExtension<PersonalityExtension>();
                    if (ext == null) continue;
                    PersonalityEntry match = ext.GetByDegree(trait.Degree);
                    if (match == null) continue;
                    baseVec = new Cards.BigFiveVector(
                        baseVec.Openness + match.openness,
                        baseVec.Conscientiousness + match.conscientiousness,
                        baseVec.Extraversion + match.extraversion,
                        baseVec.Agreeableness + match.agreeableness,
                        baseVec.Neuroticism + match.neuroticism);
                }
            }

            string openness = MapPsychologyLevel(baseVec.Openness);
            string conscientiousness = MapPsychologyLevel(baseVec.Conscientiousness);
            string extraversion = MapPsychologyLevel(baseVec.Extraversion);
            string agreeableness = MapPsychologyLevel(baseVec.Agreeableness);
            string neuroticism = MapPsychologyLevel(baseVec.Neuroticism);

            return $"开放性: {openness}, 尽责性: {conscientiousness}, 外向性: {extraversion}, 宜人性: {agreeableness}, 神经质: {neuroticism}";
        }

        private static string MapPsychologyLevel(int sum)
        {
            if (sum <= -4) return "极低";
            if (sum <= -1) return "低";
            if (sum == 0) return "中";
            if (sum <= 3) return "高";
            return "极高";
        }

        // ================================================================
        // Memory
        // ================================================================

        private static string SerializeMemory(Pawn p, bool includeDetails)
        {
            if (p?.health?.hediffSet == null) return null;

            try
            {
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory");
                if (hediffDef == null) return null;

                var hediff = p.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (hediff == null) return null;

                var comp = hediff.TryGetComp<HediffComp_PawnMemory>();
                if (comp == null) return null;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                var snapshot = comp.CreateSnapshot(currentTick);
                if (snapshot == null) return null;

                var sb = new StringBuilder(512);
                sb.Append("心态: ");
                sb.Append(snapshot.CurrentMindset ?? "?");

                if (!string.IsNullOrEmpty(snapshot.ShortTermReview))
                {
                    sb.Append("; 回顾: ");
                    sb.Append(snapshot.ShortTermReview);
                }

                if (snapshot.RecentMemories != null && snapshot.RecentMemories.Count > 0)
                {
                    sb.Append("; 最近: ");
                    sb.Append(snapshot.RecentMemories[0]);
                }

                sb.Append("; STM: ");
                sb.Append(snapshot.ShortTermCount);
                sb.Append(", LTM: ");
                sb.Append(snapshot.LongTermCount);

                if (includeDetails)
                {
                    var stmList = comp.ShortTermMemories;
                    if (stmList != null && stmList.Count > 0)
                    {
                        sb.Append("\n  [STM详情] ");
                        var stmParts = stmList.Take(10).Select(stm =>
                            $"[{stm.Tick}] {stm.Type}: {stm.Summary}");
                        sb.Append(string.Join(" | ", stmParts));
                    }

                    var ltmList = comp.LongTermMemories;
                    if (ltmList != null && ltmList.Count > 0)
                    {
                        sb.Append("\n  [LTM详情] ");
                        var ltmParts = ltmList.Take(10).Select(ltm =>
                            $"[{ltm.ConsolidatedTick}] {ltm.Topic}: {ltm.Summary}");
                        sb.Append(string.Join(" | ", ltmParts));
                    }
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.PawnPromptProvider] SerializeMemory failed: {e.Message}");
                return null;
            }
        }
    }
}
