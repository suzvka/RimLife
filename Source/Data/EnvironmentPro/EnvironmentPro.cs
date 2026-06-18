using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using NPCLife.Framework;

namespace RimLife
{
    public enum EnvironmentType
    {
        Indoors,        // 室内
        Outdoors,       // 野外完全室外
        SemiOutdoors    // 半室外（有屋顶但开放，或无屋顶的房间结构）
    }

    /// <summary>
    /// 封装环境信息，提供对房间或室外区域的语义化描述。
    /// </summary>
    public class EnvironmentPro
    {
        // --- 基础元数据 ---
        public EnvironmentType Type { get; private set; }
        public float Temperature { get; private set; }
        public float LightLevel { get; private set; } // 0-1

        // --- 语义标签 ---
        public string ThermalComfort { get; private set; }
        public string LightLabel { get; private set; }

        // --- 室内特有 (Indoors / SemiOutdoors) ---
        // 如果是 Outdoors，这些通常为 null 或默认值
        public RoomInfo Room { get; private set; }

        // --- 室外特有 (Outdoors) ---
        public WeatherInfo Weather { get; private set; }

        // --- 环境内物品摘要 (通用) ---
        public Dictionary<string, int> ThingSummary { get; private set; } = new Dictionary<string, int>();

        // --- 构造函数 ---
        // 需要传入 Pawn 以确定其位置，但扫描的是环境而非 Pawn 本身
        public EnvironmentPro(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return;

            var map = pawn.Map;
            var pos = pawn.Position;
            var room = pawn.GetRoom();

            // 1. 基础物理属性
            Temperature = GenTemperature.GetTemperatureForCell(pos, map);
            LightLevel = map.glowGrid.GroundGlowAt(pos);

            // 1b. 语义标签
            ThermalComfort = SemanticLabels.MapThermalComfort(Temperature);
            LightLabel = SemanticLabels.MapLightLevel(LightLevel);

            // 2. 判定环境类型
            if (room == null || room.PsychologicallyOutdoors)
            {
                Type = EnvironmentType.Outdoors;
                Weather = EnvExtractor.ExtractWeather(map);
            }
            else
            {
                Type = EnvironmentType.Indoors;
                Room = EnvExtractor.ExtractRoom(room);
                // 室内环境的摘要直接用房间的
                if (Room?.ThingSummary != null)
                    ThingSummary = Room.ThingSummary;
            }

            // 3. 室外环境摘要：扫描周围物品
            if (Type == EnvironmentType.Outdoors)
            {
                var outdoorThings = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 8f, true);
                ThingSummary = EnvExtractor.SummarizeThings(outdoorThings);
            }
        }

        public static class EnvExtractor
        {
            public static WeatherInfo ExtractWeather(Map map)
            {
                return new WeatherInfo
                {
                    Label = map.weatherManager.CurWeatherPerceived.LabelCap,
                    Description = map.weatherManager.CurWeatherPerceived.description,
                    IsRain = map.weatherManager.RainRate > 0.1f,
                    IsSnow = map.weatherManager.SnowRate > 0.1f,
                    WindSpeed = map.windManager.WindSpeed
                };
            }

            public static RoomInfo ExtractRoom(Room room)
            {
                // RimWorld 的 Room 类已经缓存了大量统计数据，直接读取开销很低
                var roomInfo = new RoomInfo
                {
                    RoleLabel = room.Role?.label ?? "Unknown",
                    BaseStats = new RoomStats
                    {
                        Impressiveness = room.GetStat(RoomStatDefOf.Impressiveness),
                        Beauty = room.GetStat(RoomStatDefOf.Beauty),
                        Wealth = room.GetStat(RoomStatDefOf.Wealth),
                        Space = room.GetStat(RoomStatDefOf.Space),
                        Cleanliness = room.GetStat(RoomStatDefOf.Cleanliness)
                    },
                    ThingSummary = SummarizeThings(room.ContainedAndAdjacentThings)
                };
                return roomInfo;
            }

            public static Dictionary<string, int> SummarizeThings(IEnumerable<Thing> things)
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
                // 优先使用一些特殊的 Def 来分类
                if (t is Corpse) return "Corpse";
                if (t.def.IsBed) return "Bed";
                if (t.def.IsWorkTable) return "Workbench";
                if (t.def.IsFilth) return "Filth";

                // 然后根据 ThingCategory 分类
                switch (t.def.category)
                {
                    case ThingCategory.Building:
                        return "Building";
                    case ThingCategory.Item:
                        if (t.def.IsWeapon) return "Weapon";
                        if (t.def.IsApparel) return "Apparel";
                        return "Item";
                    case ThingCategory.Plant:
                        return "Plant";
                    case ThingCategory.Pawn:
                        return "Pawn";
                    default:
                        return null; // 不计入摘要
                }
            }
        }
    }

    // --- 配套数据结构 ---

    public class RoomInfo
    {
        public string RoleLabel;    // 卧室、餐厅、监狱...
        public RoomStats BaseStats; // 数值统计
        public List<string> Tags;   // 语义标签
        public Dictionary<string, int> ThingSummary = new Dictionary<string, int>(); // 物品摘要
    }

    public struct RoomStats
    {
        public float Impressiveness;
        public float Beauty;
        public float Wealth;
        public float Space;
        public float Cleanliness;
    }

    public struct WeatherInfo
    {
        public string Label;
        public string Description;
        public bool IsRain;
        public bool IsSnow;
        public float WindSpeed;
    }
}
