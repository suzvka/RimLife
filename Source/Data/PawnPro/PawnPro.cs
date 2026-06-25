using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Infrastructure.Mcp;

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
    /// </summary>
    /// <remarks>
    /// 此类是数据快照，不会自动更新。必须在主游戏线程上创建和访问。
    /// 时序一致性不被严格保证；适用于描述性或叙事性目的，不适用于需要实时验证的系统。
    /// </remarks>
    public class PawnPro
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

    }
}
