using System.Collections.Generic;

namespace RimLife.Cards
{
    /// <summary>
    /// 殖民地全局上下文：所有卡片的共享时间/状态环境。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class ColonyContext
    {
        // ================================================================
        // 时间（来自 TimeContext）
        // ================================================================

        /// <summary>当前游戏 tick。</summary>
        public int CurrentTick;

        /// <summary>"Spring" / "Summer" / "Fall" / "Winter"</summary>
        public string Season;

        /// <summary>"Dawn" / "Day" / "Dusk" / "Night"</summary>
        public string TimeOfDay;

        /// <summary>季度标签 (例如 "Apr-Jun")。</summary>
        public string Quadrum;

        /// <summary>游戏年份。</summary>
        public int Year;

        /// <summary>季度内天数 (1~15)。</summary>
        public int DayOfQuadrum;

        /// <summary>当前小时 (0~23)。</summary>
        public int Hour;

        // ================================================================
        // 殖民地状态（来自 ColonySnapshot）
        // ================================================================

        /// <summary>存活的自由殖民者数量。</summary>
        public int PopulationAlive;

        /// <summary>当前倒地的殖民者数量。</summary>
        public int PopulationDowned;

        /// <summary>精神崩溃的殖民者数量。</summary>
        public int PopulationMentalBreak;

        /// <summary>殖民者摘要列表（轻量）。</summary>
        public IReadOnlyList<ColonistSummary> Colonists;

        /// <summary>与其他派系的关系。</summary>
        public IReadOnlyList<FactionStanding> FactionRelations;

        /// <summary>殖民地总财富。</summary>
        public float WealthTotal;

        /// <summary>"Abundant" / "Adequate" / "Low" / "Famine" / "Starving"</summary>
        public string FoodStatus;

        /// <summary>"Stable" / "Adequate" / "Strained" / "Blackout"</summary>
        public string PowerStatus;

        /// <summary>殖民地平均心情 (0~1)。</summary>
        public float MoraleAverage;

        /// <summary>殖民地平均心情语义标签。</summary>
        public string MoraleTier;

        /// <summary>当前未解决的威胁摘要列表。</summary>
        public IReadOnlyList<string> ActiveThreats;
    }

    // ================================================================
    // 配套数据结构
    // ================================================================

    /// <summary>
    /// 殖民者轻量摘要（不展开子模块）。
    /// </summary>
    public struct ColonistSummary
    {
        public string ID;
        public string Name;
        public bool IsDowned;
        public bool IsDead;
        public string CurrentJob;
        public string MoodTier;
        public string PainTier;
        public string PawnRelation;
    }

    /// <summary>
    /// 派系关系快照。
    /// </summary>
    public struct FactionStanding
    {
        public string FactionName;
        public float Goodwill;
        public string RelationLabel;
    }
}
