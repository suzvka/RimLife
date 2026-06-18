using System.Collections.Generic;

namespace NPCLife.Cards
{
    /// <summary>
    /// 环境卡：描述角色所处环境的语义化快照。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class EnvironmentCard : IExtensibleCard
    {
        /// <summary>"Indoors" / "Outdoors" / "SemiOutdoors"</summary>
        public string Type;

        public float Temperature;
        public float LightLevel;

        /// <summary>热舒适度语义标签。</summary>
        public string ThermalComfort;

        /// <summary>光照语义标签。</summary>
        public string LightLabel;

        /// <summary>室外天气信息（Indoors 时为 null）。</summary>
        public WeatherInfo Weather;

        /// <summary>环境内物品分类摘要。</summary>
        public Dictionary<string, int> ThingSummary;

        /// <summary>扩展字段。</summary>
        public Dictionary<string, string> ExtensionFields { get; set; }
    }
}
