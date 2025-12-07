using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

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

        // --- 室内特有 (Indoors / SemiOutdoors) ---
        // 如果是 Outdoors，这些通常为 null 或默认值
        public RoomInfo Room { get; private set; }

        // --- 室外特有 (Outdoors) ---
        public WeatherInfo Weather { get; private set; }

        // --- 显著特征 (通用) ---
        // 环境中最引人注目的事物（最美的雕塑、最恶心的尸体、最多的垃圾）
        public List<FeatureSnapshot> KeyFeatures { get; private set; } = new List<FeatureSnapshot>();

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

            // 2. 判定环境类型
            if (room == null || room.PsychologicallyOutdoors)
            {
                Type = EnvironmentType.Outdoors;
                // 即使在室外，如果是在某个定义的区域内（如无屋顶的围墙内），room 实例可能依然存在
                // 但逻辑上我们按室外处理，或者提取 Biome 信息
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

            // 3. 提取显著特征 (Key Features) 和环境摘要
            // 这是性能优化的重点，根据 Type 决定扫描策略
            var (features, summary) = EnvExtractor.ExtractFeaturesAndSummary(pawn, room, Type);
            KeyFeatures = features;

            // 如果是室外，我们使用独立的摘要
            if (Type == EnvironmentType.Outdoors)
            {
                ThingSummary = summary;
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

            public static (List<FeatureSnapshot> features, Dictionary<string, int> summary) ExtractFeaturesAndSummary(Pawn viewer, Room room, EnvironmentType type)
            {
                // 策略：
                // 室内 -> 扫描房间内所有物品 (ContainedAndAdjacentThings)，开销取决于房间大小。
                // 室外 -> 扫描周围一定半径 (e.g., 10格)，避免全图扫描。

                var candidates = new List<Thing>();
                var features = new List<FeatureSnapshot>();

                if (type == EnvironmentType.Indoors && room != null)
                {
                    // 室内：直接取房间列表，通常已被游戏缓存
                    candidates.AddRange(room.ContainedAndAdjacentThings);
                }
                else
                {
                    // 室外：只看脚边及近处 (Radius 10)
                    // 使用 GenRadial 可能会比较慢，但对于少量物体尚可
                    // 优化：如果只是想看“有没有血/垃圾”，只检查周围 5 格
                    candidates.AddRange(GenRadial.RadialDistinctThingsAround(viewer.Position, viewer.Map, 8f, true));
                }

                // 生成摘要
                var summary = (type == EnvironmentType.Outdoors) ? SummarizeThings(candidates) : new Dictionary<string, int>();


                // 筛选器：找出最美、最丑、最脏的东西
                Thing mostBeautiful = null;
                Thing ugliest = null;
                Thing mostFilthy = null; // 最大的污秽堆
                float maxBeauty = 100f; // 阈值，太普通的不要
                float minBeauty = -10f;
                int maxFilthStack = 0;

                foreach (var t in candidates)
                {
                    if (!t.def.selectable) continue;

                    // 检查污秽
                    if (t.def.IsFilth)
                    {
                        if (t.stackCount > maxFilthStack)
                        {
                            maxFilthStack = t.stackCount;
                            mostFilthy = t;
                        }
                        continue; // 污物没有美观度，继续下一个
                    }

                    // 检查美观度
                    float beauty = t.GetStatValue(StatDefOf.Beauty);

                    if (beauty > maxBeauty) { maxBeauty = beauty; mostBeautiful = t; }
                    if (beauty < minBeauty) { minBeauty = beauty; ugliest = t; }
                }

                // 转化为快照
                if (mostBeautiful != null) features.Add(new FeatureSnapshot(mostBeautiful, "Attraction"));
                if (ugliest != null) features.Add(new FeatureSnapshot(ugliest, "Eyesore"));
                if (mostFilthy != null) features.Add(new FeatureSnapshot(mostFilthy, "Filth"));

                // 特殊检查：尸体 (单独逻辑，因为尸体对心情影响极大)
                var corpses = candidates.Where(t => t is Corpse).Cast<Corpse>();
                foreach (var corpse in corpses)
                {
                    if (corpse.InnerPawn?.RaceProps?.Humanlike ?? false)
                    {
                        if (corpse.GetRotStage() == RotStage.Fresh)
                            features.Add(new FeatureSnapshot(corpse, "FreshHumanCorpse"));
                        else
                            features.Add(new FeatureSnapshot(corpse, "RottingHumanCorpse"));
                    }
                    else
                    {
                        features.Add(new FeatureSnapshot(corpse, "AnimalCorpse"));
                    }
                }

                // 特殊检查：血液
                var blood = candidates.FirstOrDefault(t => t.def.defName == "Filth_Blood");
                if (blood != null) features.Add(new FeatureSnapshot(blood, "Blood"));

                return (features, summary);
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

    public struct FeatureSnapshot
    {
        public string Label;
        public string DefName;
        public string CategoryTag; // "Attraction", "Eyesore", "Corpse"
        public string Description; // 简短描述 (e.g. "Legendary quality")

        public FeatureSnapshot(Thing t, string tag)
        {
            Label = t.LabelCap;
            DefName = t.def.defName;
            CategoryTag = tag;
            Description = "";

            if (t.TryGetQuality(out QualityCategory qc))
                Description = qc.ToString();
            else if (t.def.IsFilth)
                Description = "Disgusting";
        }
    }
}

