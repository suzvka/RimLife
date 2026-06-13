using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using UnityEngine;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Infrastructure.Mcp;

namespace RimLife
{
    /// <summary>
    /// 定义 Pawn 的宽泛类别。
    /// </summary>
    public enum PawnType
    {
        Character,
        Animal,
        Mechanoid,
        Insect,
        Corpse,
        Other
    }

    /// <summary>
    /// 表示 Pawn 所在派系与玩家派系的关系。
    /// </summary>
    public enum PawnRelation
    {
        OurParty, // 玩家派系成员
        Ally,
        Neutral,
        Enemy,
        Other
    }

    /// <summary>
    /// 为 Pawn 提供一个轻量级代理，包含用于获取详细信息的延迟加载模块。
    /// 创建成本低；昂贵的计算被推迟到访问特定属性（例如 .Perspective）时才执行。
    /// 注意：此类是数据快照，不会自动更新。必须在主游戏线程上创建和访问。
    /// 数据的时序一致性不被严格保证；它适用于描述性或叙事性目的，不适用于需要实时验证的系统。
    /// </summary>
    public class PawnPro : IPawnPromptProvider
    {
        // 原始的 Pawn 引用，用于按需提取数据。
        private readonly Pawn _sourcePawn;

        // --- 1. 基本元数据 ---
        public string ID { get; }
        public string Name { get; }
        public string FullName { get; }
        public string DefName { get; }
        public string FactionLabel { get; }
        public float AgeBiologicalYears { get; }
        public string Gender { get; }
        public PawnType PawnType { get; }
        public PawnRelation PawnRelation { get; }

        public bool IsDead => _sourcePawn.Dead;
        public bool IsDowned => _sourcePawn.Downed;
        // 对意识状态的空安全检查。
        public bool IsAwake => _sourcePawn.jobs?.curDriver?.asleep == false;

        // --- 构造函数 ---
        public PawnPro(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            _sourcePawn = pawn;

            // 针对机械体/动物的空安全初始化回退。
            ID = pawn.ThingID;
            Name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? pawn.LabelShort ?? "?";
            FullName = pawn.Name?.ToStringFull ?? pawn.LabelCap ?? Name;
            DefName = pawn.def?.defName ?? "UnknownDef";
            FactionLabel = pawn.Faction?.Name ?? "Unknown";
            AgeBiologicalYears = pawn.ageTracker?.AgeBiologicalYearsFloat ??0f;
            Gender = pawn.gender.ToString();
            PawnType = GetPawnType(pawn);
            PawnRelation = GetPawnRelation(pawn);
        }

        // 无参构造：仅用于 IPawnPromptProvider 单例注册，_sourcePawn 为 null
        internal PawnPro() { }

        // --- 2. 延迟加载模块 ---

        private HealthInfo _health;
        public HealthInfo Health => _health ??= HealthInfo.CreateFrom(_sourcePawn);

        private NeedsInfo _needs;
        public NeedsInfo Needs => _needs ??= NeedsInfo.CreateFrom(_sourcePawn);

        private MoodInfo _mood;
        // 使用空值合并赋值运算符进行缓存。
        public MoodInfo Mood => _mood ??= (PawnType == PawnType.Character ? MoodInfo.CreateFrom(_sourcePawn) : null);

        private SkillsInfo _skills;
        public SkillsInfo Skills => _skills ??= SkillsInfo.CreateFrom(_sourcePawn);

        private ActivityInfo _activity;
        public ActivityInfo Activity => _activity ??= ActivityInfo.CreateFrom(_sourcePawn);

        private Perspective _perspective;
        public Perspective Perspective => _perspective ??= Perspective.CreateFrom(_sourcePawn);

        private GearInfo _gear;
        public GearInfo Gear => _gear ??= GearInfo.CreateFrom(_sourcePawn);

        private BackstoryInfo _backstory;
        public BackstoryInfo Backstory => _backstory ??= BackstoryInfo.CreateFrom(_sourcePawn);

        private SocialInfo _social;
        public SocialInfo Social => _social ??= SocialInfo.CreateFrom(_sourcePawn);

        // --- 辅助方法 ---
        private static PawnRelation GetPawnRelation(Pawn p)
        {
            if (p.Faction == null) return PawnRelation.Other;
            if (p.Faction == Faction.OfPlayer) return PawnRelation.OurParty;
            var rel = p.Faction.PlayerRelationKind;
            switch (rel)
            {
                case FactionRelationKind.Ally: return PawnRelation.Ally;
                case FactionRelationKind.Hostile: return PawnRelation.Enemy;
                default: return PawnRelation.Neutral;
            }
        }

        private static PawnType GetPawnType(Pawn p)
        {
            if (p.RaceProps.Humanlike) return PawnType.Character;
            if (p.RaceProps.Animal) return PawnType.Animal;
            if (p.RaceProps.IsMechanoid) return PawnType.Mechanoid;
            if (p.RaceProps.Insect) return PawnType.Insect;
            return PawnType.Other;
        }

        // ================================================================
        // IPawnPromptProvider implementation
        // ================================================================

        public string GetCharacterPrompt(string pawnId, string view)
        {
            var pawn = ResolvePawn(pawnId);
            if (pawn == null) return null;

            bool isDynamic = string.Equals(view, "dynamic", StringComparison.OrdinalIgnoreCase);
            bool isFull = string.Equals(view, "full", StringComparison.OrdinalIgnoreCase);

            var pp = new PawnPro(pawn);
            var sb = new StringBuilder(4096);

            AppendIfNotNull(sb, "【健康】", pp.Health?.ToPrompt());
            AppendIfNotNull(sb, "【心情】", pp.Mood?.ToPrompt());
            AppendIfNotNull(sb, "【技能】", pp.Skills?.ToPrompt());
            AppendIfNotNull(sb, "【需求】", pp.Needs?.ToPrompt());
            AppendIfNotNull(sb, "【活动】", pp.Activity?.ToPrompt());
            AppendIfNotNull(sb, "【装备】", pp.Gear?.ToPrompt());
            AppendIfNotNull(sb, "【背景】", pp.Backstory?.ToPrompt());
            AppendIfNotNull(sb, "【社交】", pp.Social?.ToPrompt());
            AppendIfNotNull(sb, "【人格】", SerializePsychology(pawn));

            if (isDynamic || isFull)
            {
                AppendIfNotNull(sb, "【视野】", pp.Perspective?.ToPrompt());
                AppendIfNotNull(sb, "【记忆】", SerializeMemory(pawn, isFull));
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
        }

        public string GetSocialPrompt(string pawnId)
        {
            var pawn = ResolvePawn(pawnId);
            if (pawn == null) return null;
            var pp = new PawnPro(pawn);
            return pp.Social?.ToPrompt();
        }

        private static Pawn ResolvePawn(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return null;
            return PawnQueryHelper.FindPawnById(pawnId);
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
        // Psychology
        // ================================================================

        private static string SerializePsychology(Pawn p)
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
                Log.Warning($"[RimLife.PawnPro] SerializeMemory failed: {e.Message}");
                return null;
            }
        }
    }
}
