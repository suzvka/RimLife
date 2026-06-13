using System.Collections.Generic;
using RimLife;

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

        // ================================================================
        // 叙事者与难度
        // ================================================================

        /// <summary>叙事者名称（如 "Cassandra Classic"）。</summary>
        public string StorytellerName;

        /// <summary>难度等级（如 "Strive to Survive"）。</summary>
        public string Difficulty;

        // ================================================================
        // 殖民地生命周期
        // ================================================================

        /// <summary>殖民地成立的游戏 tick。首次加载时记录。</summary>
        public int ColonyStartTick;

        // ================================================================
        // 科技
        // ================================================================

        /// <summary>当前科技等级（如 "Industrial" / "Spacer"）。</summary>
        public string TechLevel;




    }
}
