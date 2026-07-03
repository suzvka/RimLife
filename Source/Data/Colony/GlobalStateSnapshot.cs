using System.Collections.Generic;

namespace RimLife.Data
{
    /// <summary>
    /// 当前全局状态快照。基础字段集合，未来会持续扩展。
    /// </summary>
    public class GlobalStateSnapshot
    {
        /// <summary>当前时间 + 天气，合并为一句。</summary>
        public string TimeWeather;

        /// <summary>当前地图级临时状态（游戏条件），逗号分隔。无则为空。</summary>
        public string Conditions;

        /// <summary>玩家派系名。</summary>
        public string PlayerFaction;

        /// <summary>当前地图定居点名。</summary>
        public string SettlementName;

        /// <summary>地图内玩家派系构成：殖民者、奴隶、囚犯、动物、机械体。</summary>
        public Dictionary<string, int> PlayerComposition;

        /// <summary>地图内总体角色构成（派系维度）。</summary>
        public Dictionary<string, int> MapFactionPresence;

        /// <summary>扩展字段（未来新增字段放这里）。</summary>
        public Dictionary<string, string> ExtensionFields;
    }
}
