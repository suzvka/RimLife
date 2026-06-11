using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 殖民地宏观状态快照。
    /// 注意：此数据为快照，不保证其时序一致性。
    /// </summary>
    public class ColonySnapshot
    {
        /// <summary>时间上下文。</summary>
        public TimeContext Time { get; }

        /// <summary>存活的自由殖民者数量。</summary>
        public int PopulationAlive { get; }

        /// <summary>当前倒地的殖民者数量。</summary>
        public int PopulationDowned { get; }

        /// <summary>精神崩溃的殖民者数量。</summary>
        public int PopulationMentalBreak { get; }

        /// <summary>殖民者摘要列表（轻量）。</summary>
        public IReadOnlyList<ColonistSummary> Colonists { get; }

        /// <summary>与其他派系的关系。</summary>
        public IReadOnlyList<FactionStanding> FactionRelations { get; }

        /// <summary>殖民地总财富。</summary>
        public float WealthTotal { get; }

        /// <summary>食物储备状态: "Abundant"/"Adequate"/"Low"/"Famine"/"Starving"。</summary>
        public string FoodStatus { get; }

        /// <summary>电力状态: "Stable"/"Adequate"/"Strained"/"Blackout"。</summary>
        public string PowerStatus { get; }

        /// <summary>殖民地平均心情 (0~1)。</summary>
        public float MoraleAverage { get; }

        /// <summary>殖民地平均心情语义标签。</summary>
        public string MoraleTier { get; }

        /// <summary>当前未解决的威胁摘要列表。</summary>
        public IReadOnlyList<string> ActiveThreats { get; }

        private ColonySnapshot(
            TimeContext time,
            int populationAlive,
            int populationDowned,
            int populationMentalBreak,
            IReadOnlyList<ColonistSummary> colonists,
            IReadOnlyList<FactionStanding> factionRelations,
            float wealthTotal,
            string foodStatus,
            string powerStatus,
            float moraleAverage,
            string moraleTier,
            IReadOnlyList<string> activeThreats)
        {
            Time = time;
            PopulationAlive = populationAlive;
            PopulationDowned = populationDowned;
            PopulationMentalBreak = populationMentalBreak;
            Colonists = colonists;
            FactionRelations = factionRelations;
            WealthTotal = wealthTotal;
            FoodStatus = foodStatus;
            PowerStatus = powerStatus;
            MoraleAverage = moraleAverage;
            MoraleTier = moraleTier;
            ActiveThreats = activeThreats;
        }

        /// <summary>
        /// 创建殖民地全局快照。必须在主线程上调用。
        /// </summary>
        public static ColonySnapshot Create(int mapId = 0)
        {
            try
            {
                var time = TimeContext.Current(mapId);

                Map map = null;
                if (mapId == 0)
                    map = Find.CurrentMap;
                else
                    map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);

                // 殖民者列表
                var allColonists = PawnsFinder.AllMaps_FreeColonistsSpawned ?? new List<Pawn>();
                var colonists = new List<ColonistSummary>();
                int alive = 0, downed = 0, mentalBreak = 0;
                float totalMood = 0f;
                int moodCount = 0;

                foreach (var p in allColonists)
                {
                    if (p == null) continue;

                    bool isDowned = p.Downed;
                    bool isDead = p.Dead;
                    bool inMentalBreak = p.InMentalState;

                    if (!isDead) alive++;
                    if (isDowned) downed++;
                    if (inMentalBreak) mentalBreak++;

                    string moodTier = "Content";
                    string painTier = "None";
                    string currentJob = "";

                    try
                    {
                        float mood = p.needs?.mood?.CurLevelPercentage ?? 0.5f;
                        moodTier = SemanticLabels.MapMoodTier(mood);
                        totalMood += mood;
                        moodCount++;
                    }
                    catch { }

                    try
                    {
                        float pain = p.health?.hediffSet?.PainTotal ?? 0f;
                        painTier = SemanticLabels.MapPainTier(pain);
                    }
                    catch { }

                    try
                    {
                        currentJob = p.CurJob?.def?.defName ?? "";
                    }
                    catch { }

                    colonists.Add(new ColonistSummary
                    {
                        ID = p.ThingID ?? "?",
                        Name = p.Name?.ToStringShort ?? p.LabelShortCap ?? "?",
                        IsDowned = isDowned,
                        IsDead = isDead,
                        CurrentJob = currentJob,
                        MoodTier = moodTier,
                        PainTier = painTier,
                        PawnRelation = "OurParty"
                    });
                }

                float moraleAvg = moodCount > 0 ? totalMood / moodCount : 0.5f;
                string moraleTier = SemanticLabels.MapMoodTier(moraleAvg);

                // 派系关系
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
                                RelationLabel = SemanticLabels.MapOpinionTier(goodwill)
                            });
                        }
                    }
                }
                catch { }

                // 财富
                float wealth = 0f;
                try
                {
                    if (map?.wealthWatcher != null)
                        wealth = map.wealthWatcher.WealthTotal;
                }
                catch { }

                // 食物状态：估算食物天数
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
                                // 简单判断：可食用且是食物
                                if (thing.def.IsNutritionGivingIngestible && thing.def.ingestible != null
                                    && thing.def.ingestible.HumanEdible)
                                {
                                    foodCount += thing.stackCount;
                                }
                            }
                        }

                        // 粗略估算：每个堆叠约 0.25 nutrition，每人每天约 1.6 nutrition
                        float daysWorth = (foodCount * 0.25f) / (alive * 1.6f);
                        foodStatus = SemanticLabels.MapFoodStatus(daysWorth);
                    }
                }
                catch { }

                // 电力状态
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
                            powerStatus = SemanticLabels.MapPowerStatus(totalSurplus);
                        }
                    }
                }
                catch { }

                // 活跃威胁
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
                        if (hostileCount > 0)
                            threats.Add($"ActiveHostiles:{hostileCount}");
                    }

                    if (mentalBreak > 0)
                        threats.Add($"MentalBreaks:{mentalBreak}");
                    if (downed > 0)
                        threats.Add($"DownedColonists:{downed}");
                }
                catch { }

                return new ColonySnapshot(
                    time, alive, downed, mentalBreak,
                    colonists, factionRelations,
                    wealth, foodStatus, powerStatus,
                    moraleAvg, moraleTier, threats
                );
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] ColonySnapshot.Create failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 异步创建殖民地快照。
        /// </summary>
        public static Task<ColonySnapshot> CreateAsync(int mapId = 0)
        {
            return MainThreadDispatcher.EnqueueAsync(() => Create(mapId));
        }
    }

    // --- 配套数据结构 ---

    /// <summary>
    /// 殖民者轻量摘要（不展开子模块）。
    /// </summary>
    public struct ColonistSummary
    {
        public string ID;
        public string Name;
        public bool IsDowned;
        public bool IsDead;
        public string CurrentJob;
        public string MoodTier;
        public string PainTier;
        public string PawnRelation;
    }

    /// <summary>
    /// 派系关系快照。
    /// </summary>
    public struct FactionStanding
    {
        public string FactionName;
        public float Goodwill;
        public string RelationLabel;
    }
}
