using NPCLife.Core;
using NPCLife.Infrastructure.Mcp;
using RimLife.Infrastructure.Mcp;
using RimWorld;
using System.Linq;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 健康 section：受伤、疾病、疼痛等。
    /// </summary>
    public class HealthContentProvider : ICharacterContentProvider
    {
        public string SectionName => "health";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Health?.ToPrompt();
        }
    }

    /// <summary>
    /// 心情 section：士气、情绪状态。
    /// </summary>
    public class MoodContentProvider : ICharacterContentProvider
    {
        public string SectionName => "mood";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Mood?.ToPrompt();
        }
    }

    /// <summary>
    /// 技能 section：各项技能的等级和热情。
    /// </summary>
    public class SkillsContentProvider : ICharacterContentProvider
    {
        public string SectionName => "skills";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Skills?.ToPrompt();
        }
    }

    /// <summary>
    /// 需求 section：食物、休息、娱乐等。
    /// </summary>
    public class NeedsContentProvider : ICharacterContentProvider
    {
        public string SectionName => "needs";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Needs?.ToPrompt();
        }
    }

    /// <summary>
    /// 活动 section：当前行为、工作等。
    /// </summary>
    public class ActivityContentProvider : ICharacterContentProvider
    {
        public string SectionName => "activity";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Activity?.ToPrompt();
        }
    }

    /// <summary>
    /// 装备 section：武器、服装等。
    /// </summary>
    public class GearContentProvider : ICharacterContentProvider
    {
        public string SectionName => "gear";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Gear?.ToPrompt();
        }
    }

    /// <summary>
    /// 背景 section：童年/成年经历等。
    /// </summary>
    public class BackstoryContentProvider : ICharacterContentProvider
    {
        public string SectionName => "backstory";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Backstory?.ToPrompt();
        }
    }

    /// <summary>
    /// 社交 section：人际关系网络。
    /// </summary>
    public class SocialContentProvider : ICharacterContentProvider
    {
        public string SectionName => "social";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Social?.ToPrompt();
        }
    }

    /// <summary>
    /// 人格 section：大五人格特质计算。
    /// 注意：依赖 Patches/Traits_Spectrum_CorrectedPatch.xml 的 Trait 扩展。
    /// </summary>
    public class PsychologyContentProvider : ICharacterContentProvider
    {
        public string SectionName => "psychology";
        public string GetContent(string pawnId, string view)
        {
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            // 通过 PawnPro 访问内部序列化方法：使用无参构造+反射，避免暴露 internal 方法
            // 简化为直接内联人格计算
            return SerializePsychology(pawn);
        }

        internal static string SerializePsychology(Pawn p)
        {
            if (p?.story?.traits == null) return null;

            int openness = 0, conscientiousness = 0, extraversion = 0, agreeableness = 0, neuroticism = 0;
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
                    openness += match.openness;
                    conscientiousness += match.conscientiousness;
                    extraversion += match.extraversion;
                    agreeableness += match.agreeableness;
                    neuroticism += match.neuroticism;
                }
            }

            return $"开放性: {MapPsychologyLevel(openness)}, 尽责性: {MapPsychologyLevel(conscientiousness)}, 外向性: {MapPsychologyLevel(extraversion)}, 宜人性: {MapPsychologyLevel(agreeableness)}, 神经质: {MapPsychologyLevel(neuroticism)}";
        }

        private static string MapPsychologyLevel(int sum)
        {
            if (sum <= -4) return "极低";
            if (sum <= -1) return "低";
            if (sum == 0) return "中";
            if (sum <= 3) return "高";
            return "极高";
        }
    }

    /// <summary>
    /// 视角 section：角色如何感知周围的人和物。
    /// 仅在 dynamic/full view 时返回内容。
    /// </summary>
    public class PerspectiveContentProvider : ICharacterContentProvider
    {
        public string SectionName => "perspective";
        public string GetContent(string pawnId, string view)
        {
            if (string.Equals(view, "static", System.StringComparison.OrdinalIgnoreCase))
                return null;
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;
            return new PawnPro(pawn).Perspective?.ToPrompt();
        }
    }

    /// <summary>
    /// 记忆 section：短期/长期记忆摘要。
    /// 仅在 dynamic/full view 时返回内容；full 时返回完整流水。
    /// </summary>
    public class MemoryContentProvider : ICharacterContentProvider
    {
        public string SectionName => "memory";
        public string GetContent(string pawnId, string view)
        {
            if (string.Equals(view, "static", System.StringComparison.OrdinalIgnoreCase))
                return null;
            var pawn = PawnQueryHelper.FindPawnById(pawnId);
            if (pawn == null) return null;

            bool includeDetails = string.Equals(view, "full", System.StringComparison.OrdinalIgnoreCase);
            return SerializeMemory(pawn, includeDetails);
        }

        internal static string SerializeMemory(Pawn p, bool includeDetails)
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

                var sb = new System.Text.StringBuilder(512);
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
            catch (System.Exception e)
            {
                Verse.Log.Warning($"[RimLife.CharacterContentProvider] SerializeMemory failed: {e.Message}");
                return null;
            }
        }
    }
}
