using NPCLife.Framework;
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

            string thermalComfort = NPCLife.Framework.SemanticLabels.MapThermalComfort(temperature);
            string lightLabel = NPCLife.Framework.SemanticLabels.MapLightLevel(lightLevel);

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
