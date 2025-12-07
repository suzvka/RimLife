using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 表示 Pawn 能看到的内容快照。
    /// 注意：该数据仅为快照，其时间一致性无法保证。
    /// </summary>
    public class PerspectiveInfo
    {
        public const float RecognizableRange = 13f; // 识别详细个体信息的有效距离阈值
        public const float VisualRange = 26f;       // 最大视野范围 (用于初步捕获)

        // 统一的可见 Pawn 快照列表：所有在视野与视线(LineOfSight)内的 Pawn（不再拆分）
        public IReadOnlyList<PawnRelationSnapshot> VisiblePawnSnapshots { get; }

        private PerspectiveInfo()
        {
            VisiblePawnSnapshots = new List<PawnRelationSnapshot>();
        }

        private PerspectiveInfo(IReadOnlyList<PawnRelationSnapshot> visiblePawnSnapshots)
        {
            VisiblePawnSnapshots = visiblePawnSnapshots;
        }

        // 接口：获取“可见但不可识别的物种及数量” (距离 > RecognizableRange)
        public IEnumerable<string> GetUnrecognizableSpeciesCounts()
        {
            return VisiblePawnSnapshots
                .Where(p => p.Distance > RecognizableRange)
                .GroupBy(p => p.DefName)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}:{g.Count()}");
        }

        // 接口：获取“可识别的 Pawn ID” (距离 ≤ RecognizableRange)
        public IEnumerable<string> GetRecognizablePawnIDs()
        {
            return VisiblePawnSnapshots
                .Where(p => p.Distance <= RecognizableRange)
                .Select(p => p.ID);
        }

        public static PerspectiveInfo CreateFrom(Pawn pawn)
        {
            // 1. 基础校验
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
                return new PerspectiveInfo();

            Map map = pawn.Map;

            // 2. 判定室内/室外（本地轻量判断；环境详情由上层按需调用 EnvironmentPro 获取）
            Room room = pawn.GetRoom();
            bool indoors = room != null && !room.PsychologicallyOutdoors;

            // 4. 捕获所有视野内可见 Pawn（统一放入 VisiblePawnSnapshots）
            var visiblePawns = new List<PawnRelationSnapshot>();
            var allPawns = map.mapPawns.AllPawnsSpawned;
            foreach (var target in allPawns)
            {
                if (target == pawn) continue;
                if (target?.Position == null) continue;

                float dist = target.Position.DistanceTo(pawn.Position);
                if (dist > VisualRange) continue;
                if (!GenSight.LineOfSight(pawn.Position, target.Position, map, skipFirstCell: true)) continue;

                var snap = CreatePawnSnapshot(target, dist);
                if (snap.ID != null) // skip invalid
                    visiblePawns.Add(snap);
            }

            return new PerspectiveInfo(visiblePawns);
        }

        public static Task<PerspectiveInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new PerspectiveInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }

        private static PawnRelationSnapshot CreatePawnSnapshot(Pawn target, float dist)
        {
            if (target == null) return default;
            // 某些 pawn（动物、机械体）可能没有 Name；使用 LabelShortCap 作为后备。
            string name = target.Name?.ToStringShort ?? target.LabelShortCap ?? "?";
            string defName = target.def?.defName ?? "Unknown";
            string id = target.ThingID; // 生成的 pawn 应该有 ThingID。
            return new PawnRelationSnapshot
            {
                ID = id,
                Name = name,
                DefName = defName,
                Distance = dist
            };
        }
    }

    // Pawn 基础快照：仅保存物种与基础身份信息（不含社交关系/敌对状态等动态高层语义）。
    public struct PawnRelationSnapshot
    {
        public string ID;         // ThingID
        public string Name;       // 显示名（可能用于 UI 或上层进一步索引）
        public string DefName;    // 种族定义名
        public float Distance;    // 与观察者距离
    }
}
