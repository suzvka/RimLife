using System.Collections.Generic;

namespace RimLife.Cards
{
    /// <summary>
    /// 环境卡：描述角色所处环境的语义化快照。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class EnvironmentCard
    {
        /// <summary>"Indoors" / "Outdoors" / "SemiOutdoors"</summary>
        public string Type;

        public float Temperature;
        public float LightLevel;

        /// <summary>热舒适度语义标签。</summary>
        public string ThermalComfort;

        /// <summary>光照语义标签。</summary>
        public string LightLabel;

        /// <summary>室内信息（Outdoors 时为 null）。</summary>
        public RoomSection Room;

        /// <summary>室外天气信息（Indoors 时为 null）。</summary>
        public WeatherSection Weather;

        /// <summary>环境内物品分类摘要。</summary>
        public Dictionary<string, int> ThingSummary;
    }

    // ================================================================
    // Room Section
    // ================================================================

    public class RoomSection
    {
        /// <summary>房间角色标签（卧室、餐厅、监狱...）。</summary>
        public string RoleLabel;

        /// <summary>房间数值统计。</summary>
        public RoomStats BaseStats;

        /// <summary>语义标签列表。</summary>
        public IReadOnlyList<string> Tags;

        /// <summary>房间内物品分类摘要。</summary>
        public Dictionary<string, int> ThingSummary;
    }

    public struct RoomStats
    {
        public float Impressiveness;
        public float Beauty;
        public float Wealth;
        public float Space;
        public float Cleanliness;
    }

    // ================================================================
    // Weather Section
    // ================================================================

    public struct WeatherSection
    {
        public string Label;
        public string Description;
        public bool IsRain;
        public bool IsSnow;
        public float WindSpeed;
    }
}
