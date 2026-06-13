using System.Collections.Generic;

namespace RimLife.Cards
{
    /// <summary>
    /// 人物卡：聚合单个角色的全部可观测数据。
    /// 纯 DTO，零 RimWorld 依赖。null 的 Section 表示未采集。
    /// </summary>
    public class CharacterCard
    {
        // --- 基本元数据 ---
        public string ID;
        public string Name;
        public string FullName;
        public string DefName;
        public string FactionLabel;
        public float AgeBiologicalYears;
        public string Gender;
        public string PawnType;     // "Character" / "Animal" / "Mechanoid" / "Insect" / "Other"
        public string PawnRelation; // "OurParty" / "Ally" / "Neutral" / "Enemy" / "Other"
        public bool IsDead;
        public bool IsDowned;
        public bool IsAwake;

        // --- 按需填充的子模块 ---
        public HealthSection Health;
        public MoodSection Mood;
        public SkillsSection Skills;
        public NeedsSection Needs;
        public ActivitySection Activity;
        public GearSection Gear;
        public BackstorySection Backstory;
        public SocialSection Social;
        public PerspectiveSection Perspective;
        public PsychologySection Psychology;
        public MemorySection Memory;
    }

    // ================================================================
    // Health Section
    // ================================================================

    public class HealthSection
    {
        public float SummaryPain;
        public float SummaryBleedRate;
        public string PainTier;
        public string BleedTier;
        public IReadOnlyDictionary<string, float> Capacities;
        public IReadOnlyDictionary<string, string> CapacityTiers;
        public IReadOnlyList<HealthEntry> Injuries;
    }

    public struct HealthEntry
    {
        public string Label;
        public string Part;
        public float Severity;
        public bool IsBleeding;
        public bool IsPermanent;
        public bool IsInfection;
        public float TendQuality;
        public int AgeTicks;
        public float Immunity;
        public bool CompDisappears;
    }

    // ================================================================
    // Mood Section
    // ================================================================

    public class MoodSection
    {
        public float MoodLevel;
        public string MoodTier;
        public string MentalStateLabel;
        public IReadOnlyList<TraitEntry> Traits;
        public IReadOnlyList<ThoughtEntry> ActiveThoughts;
    }

    public struct TraitEntry
    {
        public string DefName;
        public string Label;
        public int Degree;
    }

    public struct ThoughtEntry
    {
        public string Label;
        public float MoodOffset;
        public float DurationRatio;
    }

    // ================================================================
    // Skills Section
    // ================================================================

    public class SkillsSection
    {
        public IReadOnlyList<SkillEntry> AllSkills;
    }

    public struct SkillEntry
    {
        public string DefName;
        public string Label;
        public int Level;
        public string Passion;
        public bool HasPassion;
        public bool TotallyDisabled;
    }

    // ================================================================
    // Needs Section
    // ================================================================

    public class NeedsSection
    {
        public IReadOnlyList<NeedEntry> AllNeeds;
    }

    public struct NeedEntry
    {
        public string DefName;
        public string Label;
        public float CurLevel;
        public float ThresholdLow;
        public bool IsCritical;
        public string NeedUrgency;
    }

    // ================================================================
    // Activity Section
    // ================================================================

    public class ActivitySection
    {
        public string Posture;
        public IReadOnlyList<ActivityEntry> Activities;
    }

    public struct ActivityEntry
    {
        public string JobDefName;
        public string JobReport;
    }

    // ================================================================
    // Gear Section
    // ================================================================

    public class GearSection
    {
        public IReadOnlyList<GearItem> WornGear;
        public IReadOnlyList<GearItem> Inventory;
    }

    public struct GearItem
    {
        public string Name;
        public string Quality;
        public float Durability;
        public string ConditionLabel;
        public int Count;
    }

    // ================================================================
    // Backstory Section
    // ================================================================

    public class BackstorySection
    {
        public BackstoryEntry? Childhood;
        public BackstoryEntry? Adulthood;
    }

    public struct BackstoryEntry
    {
        public string Title;
        public string Description;
    }

    // ================================================================
    // Social Section
    // ================================================================

    public class SocialSection
    {
        public IReadOnlyList<SocialRelation> Relations;
        public float ColonyOpinionAverage;
    }

    public struct SocialRelation
    {
        public string OtherID;
        public string OtherName;
        public string RelationType;
        public float Opinion;
        public string OpinionTier;
        public bool IsReciprocal;
    }

    /// <summary>
    /// 社交互动流水记录。用于 InteractionHistoryStore 的 append-only 存储。
    /// 每条记录描述一次已发生的角色间互动。
    /// </summary>
    public struct InteractionRecord
    {
        /// <summary>发生时刻 (游戏 tick)。</summary>
        public int Tick;

        /// <summary>互动发起者 ID。</summary>
        public string InitiatorID;

        /// <summary>互动接受者 ID。</summary>
        public string RecipientID;

        /// <summary>互动定义名 (如 "Insult", "Chat")。</summary>
        public string InteractionDef;

        /// <summary>互动结果标签。</summary>
        public string Outcome;
    }

    // ================================================================
    // Perspective Section
    // ================================================================

    public class PerspectiveSection
    {
        public IReadOnlyList<PawnRelationSnapshot> VisiblePawnSnapshots;
    }

    public struct PawnRelationSnapshot
    {
        public string ID;
        public string Name;
        public string DefName;
        public float Distance;
    }

    // ================================================================
    // Psychology Section
    // ================================================================

    public class PsychologySection
    {
        public string Openness;
        public string Conscientiousness;
        public string Extraversion;
        public string Agreeableness;
        public string Neuroticism;
        public BigFiveVector BaseVector;
        public BigFiveVector TotalVector;
        public IReadOnlyDictionary<string, BigFiveVector> ExternalVectors;
    }

    public struct BigFiveVector
    {
        public int Openness;
        public int Conscientiousness;
        public int Extraversion;
        public int Agreeableness;
        public int Neuroticism;

        public static BigFiveVector Zero => new BigFiveVector();

        public BigFiveVector(int o, int c, int e, int a, int n)
        {
            Openness = o;
            Conscientiousness = c;
            Extraversion = e;
            Agreeableness = a;
            Neuroticism = n;
        }

        public bool IsZero()
        {
            return Openness == 0 && Conscientiousness == 0 && Extraversion == 0
                && Agreeableness == 0 && Neuroticism == 0;
        }

        public override string ToString()
        {
            return $"O={Openness} C={Conscientiousness} E={Extraversion} A={Agreeableness} N={Neuroticism}";
        }
    }

    // ================================================================
    // Memory Section
    // ================================================================

    /// <summary>
    /// 记忆 Section：封装从 HediffComp_PawnMemory 提取的快照视图。
    /// null 表示未采集。
    /// </summary>
    public class MemorySection
    {
        /// <summary>记忆快照数据。</summary>
        public MemorySnapshot Snapshot;
    }
}
