using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimLife.Cards;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld 全局状态提取 ColonyContext。
    /// 合并 ColonySnapshot.Create + TimeContext.Current 的逻辑。
    /// </summary>
    public static class ColonyContextMapper
    {
        /// <summary>
        /// 创建完整的 ColonyContext。必须在主线程上调用。
        /// </summary>
        public static ColonyContext Create(int mapId = 0)
        {
            try
            {
                var ctx = new ColonyContext();

                // === 时间 ===
                MapTime(ctx, mapId);

                // === 殖民者 ===
                MapColonists(ctx);

                // === 派系关系 ===
                MapFactionRelations(ctx);

                // === 财富 / 食物 / 电力 / 威胁 ===
                Map map = ResolveMap(mapId);
                MapMapStats(ctx, map);

                return ctx;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] ColonyContextMapper.Create failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 异步创建 ColonyContext。
        /// </summary>
        public static Task<ColonyContext> CreateAsync(int mapId = 0)
        {
            return MainThreadDispatcher.EnqueueAsync(() => Create(mapId));
        }

        // ================================================================
        // 子映射
        // ================================================================

        private static Map ResolveMap(int mapId)
        {
            if (mapId == 0) return Find.CurrentMap;
            return Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
        }

        private static void MapTime(ColonyContext ctx, int mapId)
        {
            try
            {
                ctx.CurrentTick = Find.TickManager?.TicksGame ?? -1;
                if (ctx.CurrentTick < 0) return;

                Map map = ResolveMap(mapId) ?? Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    ctx.TimeOfDay = "Unknown";
                    ctx.Hour = -1;
                    return;
                }

                int tick = ctx.CurrentTick;
                int tile = map.Tile;
                long absTick = GenDate.TickGameToAbs(tick);
                Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
                float longitude = longLat.x;

                try { ctx.Season = GenDate.Season(absTick, longLat).ToString(); } catch { ctx.Season = "Unknown"; }
                try { ctx.Quadrum = GenDate.Quadrum(absTick, longitude).ToString(); } catch { ctx.Quadrum = "Unknown"; }
                try { ctx.Year = GenDate.Year(absTick, longitude); } catch { ctx.Year = 0; }
                try { ctx.DayOfQuadrum = GenDate.DayOfQuadrum(absTick, longitude) + 1; } catch { ctx.DayOfQuadrum = 0; }
                try { ctx.Hour = GenDate.HourInteger(absTick, longitude); } catch { ctx.Hour = -1; }

                ctx.TimeOfDay = ctx.Hour >= 0 ? MapTimeOfDay(ctx.Hour) : "Unknown";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] ColonyContextMapper.MapTime failed: {e.Message}");
            }
        }

        private static void MapColonists(ColonyContext ctx)
        {
            var allColonists = PawnsFinder.AllMaps_FreeColonistsSpawned ?? new List<Pawn>();
            var colonists = new List<ColonistSummary>();
            int alive = 0, downed = 0, mentalBreak = 0;
            float totalMood = 0f;
            int moodCount = 0;

            foreach (var p in allColonists)
            {
                if (p == null) continue;
                if (!p.Dead) alive++;
                if (p.Downed) downed++;
                if (p.InMentalState) mentalBreak++;

                string moodTier = "Content";
                string painTier = "None";
                string currentJob = "";

                try { float mood = p.needs?.mood?.CurLevelPercentage ?? 0.5f; moodTier = Framework.SemanticLabels.MapMoodTier(mood); totalMood += mood; moodCount++; }
                    catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] mood {p.ThingID}: {e.Message}"); }
                try { float pain = p.health?.hediffSet?.PainTotal ?? 0f; painTier = Framework.SemanticLabels.MapPainTier(pain); }
                    catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] pain {p.ThingID}: {e.Message}"); }
                try { currentJob = p.CurJob?.def?.defName ?? ""; }
                    catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] job {p.ThingID}: {e.Message}"); }

                colonists.Add(new ColonistSummary
                {
                    ID = p.ThingID ?? "?",
                    Name = p.Name?.ToStringShort ?? p.LabelShortCap ?? "?",
                    IsDowned = p.Downed,
                    IsDead = p.Dead,
                    CurrentJob = currentJob,
                    MoodTier = moodTier,
                    PainTier = painTier,
                    PawnRelation = "OurParty"
                });
            }

            ctx.PopulationAlive = alive;
            ctx.PopulationDowned = downed;
            ctx.PopulationMentalBreak = mentalBreak;
            ctx.Colonists = colonists;
            ctx.MoraleAverage = moodCount > 0 ? totalMood / moodCount : 0.5f;
            ctx.MoraleTier = Framework.SemanticLabels.MapMoodTier(ctx.MoraleAverage);
        }

        private static void MapFactionRelations(ColonyContext ctx)
        {
            var factionRelations = new List<FactionStanding>();
            try
            {
                var playerFaction = Faction.OfPlayer;
                if (playerFaction != null && Find.FactionManager != null)
                {
                    foreach (var faction in Find.FactionManager.AllFactionsVisible)
                    {
                        if (faction == null || faction == playerFaction) continue;
                        if (faction.def != null && faction.def.isPlayer) continue;
                        float goodwill = playerFaction.GoodwillWith(faction);
                        factionRelations.Add(new FactionStanding
                        {
                            FactionName = faction.Name ?? faction.def?.label ?? "Unknown",
                            Goodwill = goodwill,
                            RelationLabel = Framework.SemanticLabels.MapOpinionTier(goodwill)
                        });
                    }
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] MapFactionRelations: {e.Message}"); }
            ctx.FactionRelations = factionRelations;
        }

        private static void MapMapStats(ColonyContext ctx, Map map)
        {
            int alive = ctx.PopulationAlive;

            // 财富
            float wealth = 0f;
            try { if (map?.wealthWatcher != null) wealth = map.wealthWatcher.WealthTotal; }
                catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] wealth: {e.Message}"); }
            ctx.WealthTotal = wealth;

            // 食物
            string foodStatus = "Adequate";
            try
            {
                if (map != null && alive > 0)
                {
                    int foodCount = 0;
                    var allThings = map.listerThings?.AllThings;
                    if (allThings != null)
                    {
                        foreach (var thing in allThings)
                        {
                            if (thing == null || thing.def == null) continue;
                            if (thing.def.IsNutritionGivingIngestible && thing.def.ingestible != null && thing.def.ingestible.HumanEdible)
                                foodCount += thing.stackCount;
                        }
                    }
                    float daysWorth = (foodCount * 0.25f) / (alive * 1.6f);
                    foodStatus = Framework.SemanticLabels.MapFoodStatus(daysWorth);
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] food: {e.Message}"); }
            ctx.FoodStatus = foodStatus;

            // 电力
            string powerStatus = "Stable";
            try
            {
                if (map?.powerNetManager != null)
                {
                    var powerNets = map.powerNetManager?.AllNetsListForReading;
                    if (powerNets != null && powerNets.Count > 0)
                    {
                        float totalSurplus = 0f;
                        foreach (var net in powerNets)
                        {
                            if (net == null) continue;
                            totalSurplus += net.CurrentStoredEnergy();
                        }
                        powerStatus = Framework.SemanticLabels.MapPowerStatus(totalSurplus);
                    }
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] power: {e.Message}"); }
            ctx.PowerStatus = powerStatus;

            // 威胁
            var threats = new List<string>();
            try
            {
                var allSpawned = map?.mapPawns?.AllPawnsSpawned;
                if (allSpawned != null)
                {
                    int hostileCount = 0;
                    foreach (var pawn in allSpawned)
                    {
                        if (pawn == null) continue;
                        if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer) && !pawn.Dead)
                            hostileCount++;
                    }
                    if (hostileCount > 0) threats.Add($"ActiveHostiles:{hostileCount}");
                }
                if (ctx.PopulationMentalBreak > 0) threats.Add($"MentalBreaks:{ctx.PopulationMentalBreak}");
                if (ctx.PopulationDowned > 0) threats.Add($"DownedColonists:{ctx.PopulationDowned}");
            }
            catch (Exception e) { Log.Warning($"[RimLife.ColonyContextMapper] threats: {e.Message}"); }
            ctx.ActiveThreats = threats;
        }

        private static string MapTimeOfDay(int hour)
        {
            if (hour >= 5 && hour < 7) return "Dawn";
            if (hour >= 7 && hour < 18) return "Day";
            if (hour >= 18 && hour < 20) return "Dusk";
            return "Night";
        }
    }
}
