using RimLife.Data;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld 全局状态构建 GlobalStateSnapshot。
    /// 必须在主线程上调用。
    /// </summary>
    public static class GlobalStateMapper
    {
        /// <summary>
        /// 创建当前全局状态快照。
        /// </summary>
        public static GlobalStateSnapshot Create(int mapId = 0)
        {
            try
            {
                var map = ResolveMap(mapId);
                if (map == null) return null;

                var snap = new GlobalStateSnapshot();

                MapTimeWeather(snap, map);
                MapConditions(snap, map);
                MapFactionInfo(snap, map);
                MapSettlementName(snap, map);
                MapPopulationComposition(snap, map);

                return snap;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] GlobalStateMapper.Create failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将快照序列化为 JSON 字符串。
        /// </summary>
        public static string Serialize(GlobalStateSnapshot snap)
        {
            if (snap == null) return "{}";

            var w = new NPCLife.Framework.JsonWriter(512);

            w.Prop("timeWeather", snap.TimeWeather ?? "");
            if (!string.IsNullOrEmpty(snap.Conditions))
                w.Prop("conditions", snap.Conditions);
            w.Prop("playerFaction", snap.PlayerFaction ?? "");
            w.Prop("settlementName", snap.SettlementName ?? "");

            if (snap.PlayerComposition != null && snap.PlayerComposition.Count > 0)
                w.PropRaw("playerComposition", SerializeDict(snap.PlayerComposition));

            if (snap.MapFactionPresence != null && snap.MapFactionPresence.Count > 0)
                w.PropRaw("mapFactionPresence", SerializeDict(snap.MapFactionPresence));

            if (snap.ExtensionFields != null && snap.ExtensionFields.Count > 0)
                w.PropRaw("extensions", SerializeStringDict(snap.ExtensionFields));

            return w.Close();
        }

        // ================================================================
        // 子映射
        // ================================================================

        private static Map ResolveMap(int mapId)
        {
            if (mapId == 0) return Find.CurrentMap;
            return Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
        }

        private static void MapTimeWeather(GlobalStateSnapshot snap, Map map)
        {
            try
            {
                int tick = Find.TickManager?.TicksGame ?? -1;
                if (tick < 0) { snap.TimeWeather = "Unknown"; return; }

                int tile = map.Tile;
                long absTick = GenDate.TickGameToAbs(tick);
                Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
                float longitude = longLat.x;

                string season;
                try { season = GenDate.Season(absTick, longLat).ToString(); } catch { season = "Unknown"; }
                int year;
                try { year = GenDate.Year(absTick, longitude); } catch { year = 0; }
                int hour;
                try { hour = GenDate.HourInteger(absTick, longitude); } catch { hour = -1; }

                string timeStr = hour >= 0
                    ? $"第{year}年·{season}·{hour:D2}时"
                    : $"第{year}年·{season}";

                // 天气
                string weather = "";
                try { weather = map.weatherManager?.CurWeatherPerceived?.LabelCap ?? ""; } catch { }

                snap.TimeWeather = string.IsNullOrEmpty(weather)
                    ? timeStr
                    : $"{timeStr}, {weather}";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.GlobalStateMapper] TimeWeather: {e.Message}");
                snap.TimeWeather = "Unknown";
            }
        }

        private static void MapConditions(GlobalStateSnapshot snap, Map map)
        {
            try
            {
                var conditions = map?.gameConditionManager?.ActiveConditions;
                if (conditions == null || conditions.Count == 0)
                {
                    snap.Conditions = "";
                    return;
                }

                var labels = new List<string>();
                foreach (var cond in conditions)
                {
                    if (cond == null) continue;
                    // 排除永久性条件（如"永久日食"等已在时间天气中体现的）
                    try
                    {
                        string label = cond.LabelCap;
                        if (!string.IsNullOrEmpty(label))
                            labels.Add(label);
                    }
                    catch { }
                }

                snap.Conditions = labels.Count > 0 ? string.Join(", ", labels) : "";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.GlobalStateMapper] Conditions: {e.Message}");
                snap.Conditions = "";
            }
        }

        private static void MapFactionInfo(GlobalStateSnapshot snap, Map map)
        {
            try
            {
                var playerFaction = Faction.OfPlayer;
                snap.PlayerFaction = playerFaction?.Name ?? playerFaction?.def?.label ?? "Unknown";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.GlobalStateMapper] FactionInfo: {e.Message}");
                snap.PlayerFaction = "Unknown";
            }
        }

        private static void MapSettlementName(GlobalStateSnapshot snap, Map map)
        {
            try
            {
                string name = null;
                // map.Parent.Label 通常包含定居点名
                try { name = map.Parent?.Label; } catch { }

                snap.SettlementName = name ?? "";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.GlobalStateMapper] SettlementName: {e.Message}");
                snap.SettlementName = "";
            }
        }

        private static void MapPopulationComposition(GlobalStateSnapshot snap, Map map)
        {
            try
            {
                var allPawns = map?.mapPawns?.AllPawnsSpawned;
                if (allPawns == null) return;

                var playerFaction = Faction.OfPlayer;

                // 玩家派系构成
                int colonists = 0, slaves = 0, prisoners = 0, animals = 0, mechanoids = 0;
                // 派系维度统计
                var factionCounts = new Dictionary<string, int>();

                foreach (var p in allPawns)
                {
                    if (p == null || p.Dead) continue;

                    bool isPlayerFaction = p.Faction == playerFaction;

                    if (isPlayerFaction)
                    {
                        if (p.RaceProps.Animal)
                            animals++;
                        else if (p.RaceProps.IsMechanoid)
                            mechanoids++;
                        else if (p.IsSlave)
                            slaves++;
                        else if (p.IsPrisoner)
                            prisoners++;
                        else if (p.IsColonist)
                            colonists++;
                    }

                    // 按派系统计（所有存活单位）
                    string factionName = p.Faction?.Name ?? p.Faction?.def?.label ?? "无派系";
                    if (!factionCounts.ContainsKey(factionName))
                        factionCounts[factionName] = 0;
                    factionCounts[factionName]++;
                }

                snap.PlayerComposition = new Dictionary<string, int>
                {
                    ["colonists"] = colonists,
                    ["slaves"] = slaves,
                    ["prisoners"] = prisoners,
                    ["animals"] = animals,
                    ["mechanoids"] = mechanoids
                };

                snap.MapFactionPresence = factionCounts;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.GlobalStateMapper] Population: {e.Message}");
            }
        }

        // ================================================================
        // 序列化辅助
        // ================================================================

        private static string SerializeDict(Dictionary<string, int> dict)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeStringDict(Dictionary<string, string> dict)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":\"")
                  .Append(kv.Value?.Replace("\"", "\\\"") ?? "").Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
