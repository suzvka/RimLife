using NPCLife.Framework;
using RimLife.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NPCLife.Cards;
using RimWorld;
using Verse;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld Map/Room 提取数据并组装 EnvironmentCard。
    /// 承担全部 RimWorld 耦合。
    /// </summary>
    public static class EnvironmentCardMapper
    {
        /// <summary>
        /// 从 Pawn 当前位置创建 EnvironmentCard。必须在主线程上调用。
        /// </summary>
        public static EnvironmentCard CreateFrom(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned)
                return new EnvironmentCard { ThingSummary = new Dictionary<string, int>() };

            var map = pawn.Map;
            var pos = pawn.Position;
            var room = pawn.GetRoom();

            float temperature = GenTemperature.GetTemperatureForCell(pos, map);
            float lightLevel = map.glowGrid.GroundGlowAt(pos);

            string thermalComfort = SemanticLabels.MapThermalComfort(temperature);
            string lightLabel = SemanticLabels.MapLightLevel(lightLevel);

            EnvironmentCard card;

            if (room == null || room.PsychologicallyOutdoors)
            {
                // 室外
                card = new EnvironmentCard
                {
                    Type = "Outdoors",
                    Temperature = temperature,
                    LightLevel = lightLevel,
                    ThermalComfort = thermalComfort,
                    LightLabel = lightLabel,
                    Weather = MapWeather(map),
                    ThingSummary = SummarizeThings(GenRadial.RadialDistinctThingsAround(pos, map, 8f, true))
                };
            }
            else
            {
                // 室内
                card = new EnvironmentCard
                {
                    Type = "Indoors",
                    Temperature = temperature,
                    LightLevel = lightLevel,
                    ThermalComfort = thermalComfort,
                    LightLabel = lightLabel,
                    ThingSummary = SummarizeThings(room.ContainedAndAdjacentThings)
                };
            }

            return card;
        }

        /// <summary>
        /// 创建地图级别的环境卡片（温度、天气、光照），不依赖特定 Pawn。
        /// 用于殖民地整体环境感知，避免对多个 Pawn 重复查询相同数据。
        /// </summary>
        public static EnvironmentCard CreateForMap(int mapId = 0)
        {
            try
            {
                Map map = mapId == 0 ? Find.CurrentMap
                    : Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null)
                    return new EnvironmentCard { ThingSummary = new Dictionary<string, int>() };

                // 使用地图中心点作为参考位置
                var center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
                float temperature = GenTemperature.GetTemperatureForCell(center, map);
                float lightLevel = map.glowGrid.GroundGlowAt(center);

                return new EnvironmentCard
                {
                    Type = "Colony",
                    Temperature = temperature,
                    LightLevel = lightLevel,
                    ThermalComfort = SemanticLabels.MapThermalComfort(temperature),
                    LightLabel = SemanticLabels.MapLightLevel(lightLevel),
                    Weather = MapWeather(map),
                    ThingSummary = new Dictionary<string, int>()
                };
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] EnvironmentCardMapper.CreateForMap failed: {e.Message}");
                return new EnvironmentCard { ThingSummary = new Dictionary<string, int>() };
            }
        }

        /// <summary>
        /// 异步创建 EnvironmentCard。
        /// </summary>
        public static Task<EnvironmentCard> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new EnvironmentCard { ThingSummary = new Dictionary<string, int>() });
            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }

        // ================================================================
        // 子映射
        // ================================================================

        private static NPCLife.Cards.WeatherInfo MapWeather(Map map)
        {
            return new NPCLife.Cards.WeatherInfo
            {
                Label = map.weatherManager.CurWeatherPerceived.LabelCap,
                Description = map.weatherManager.CurWeatherPerceived.description,
                IsRain = map.weatherManager.RainRate > 0.1f,
                IsSnow = map.weatherManager.SnowRate > 0.1f,
                WindSpeed = map.windManager.WindSpeed
            };
        }

        private static Dictionary<string, int> SummarizeThings(IEnumerable<Thing> things)
        {
            var summary = new Dictionary<string, int>();
            foreach (var t in things)
            {
                if (t.def == null || !t.def.selectable) continue;
                string category = GetThingCategory(t);
                if (!string.IsNullOrEmpty(category))
                {
                    summary.TryGetValue(category, out int count);
                    summary[category] = count + 1;
                }
            }
            return summary;
        }

        private static string GetThingCategory(Thing t)
        {
            if (t is Corpse) return "Corpse";
            if (t.def.IsBed) return "Bed";
            if (t.def.IsWorkTable) return "Workbench";
            if (t.def.IsFilth) return "Filth";
            switch (t.def.category)
            {
                case ThingCategory.Building: return "Building";
                case ThingCategory.Item:
                    if (t.def.IsWeapon) return "Weapon";
                    if (t.def.IsApparel) return "Apparel";
                    return "Item";
                case ThingCategory.Plant: return "Plant";
                case ThingCategory.Pawn: return "Pawn";
                default: return null;
            }
        }
    }
}
