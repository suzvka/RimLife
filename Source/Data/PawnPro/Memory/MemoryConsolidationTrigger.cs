using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 记忆巩固触发器。作为 GameComponent 周期性检查每个 Pawn 的睡眠状态，
    /// 并在满足条件时触发短期记忆 → 长期记忆巩固。
    /// 
    /// 触发条件：
    /// 1. 睡眠触发：连续深度睡眠超过 7500 ticks (3h)
    /// 2. 定时触发：距上次巩固 ≥ 60000 ticks (24h)
    /// </summary>
    public class MemoryConsolidationTrigger : GameComponent
    {
        /// <summary>检查间隔 (ticks)。约 1 游戏小时检查一次，平衡性能与及时性。</summary>
        private const int CheckIntervalTicks = 250;

        /// <summary>下次检查的 tick。</summary>
        private int _nextCheckTick;

        public MemoryConsolidationTrigger(Game game) : base()
        {
            _nextCheckTick = Find.TickManager?.TicksGame ?? 0 + CheckIntervalTicks;
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick < _nextCheckTick) return;
            _nextCheckTick = currentTick + CheckIntervalTicks;

            CheckAllPawns(currentTick);
        }

        /// <summary>
        /// 遍历所有 Spawned Pawn，对每个持有 PawnProMemory Hediff 的 Pawn
        /// 检查睡眠状态并通知记忆 comp。
        /// </summary>
        private static void CheckAllPawns(int currentTick)
        {
            // 收集所有地图上的 pawn
            var allPawns = new List<Pawn>();
            foreach (var map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                allPawns.AddRange(map.mapPawns.AllPawnsSpawned);
            }

            foreach (var pawn in allPawns)
            {
                if (pawn?.health?.hediffSet == null) continue;

                // 查找 PawnProMemory hediff
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(
                    DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory"));
                if (hediff == null) continue;

                var comp = hediff.TryGetComp<HediffComp_PawnMemory>();
                if (comp == null) continue;

                // 检查睡眠状态
                bool isSleeping = pawn.jobs?.curDriver?.asleep ?? false;

                comp.NotifySleepTick(isSleeping, currentTick);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _nextCheckTick, "nextCheckTick", 0);
        }
    }
}
