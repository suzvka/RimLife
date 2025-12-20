using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 表示 Pawn 装备和库存信息的快照。
    /// 注意：此数据为快照，其时序一致性不被保证。
    /// </summary>
    public class GearInfo
    {
        /// <summary>
        /// 当前穿戴的服装和装备列表。
        /// </summary>
        public IReadOnlyList<GearItem> WornGear { get; }

        /// <summary>
        /// Pawn 库存中的物品列表。
        /// </summary>
        public IReadOnlyList<GearItem> Inventory { get; }

        private GearInfo(IReadOnlyList<GearItem> worn, IReadOnlyList<GearItem> inventory)
        {
            WornGear = worn;
            Inventory = inventory;
        }

        /// <summary>
        /// 从 Pawn 创建 GearInfo 快照。必须在主线程上调用。
        /// </summary>
        public static GearInfo CreateFrom(Pawn p)
        {
            if (p == null) return new GearInfo(new List<GearItem>(), new List<GearItem>());

            var worn = p.apparel?.WornApparel.Select(CreateGearItem).ToList() ?? new List<GearItem>();
            var inventory = p.inventory?.innerContainer.Select(CreateGearItem).ToList() ?? new List<GearItem>();

            return new GearInfo(worn, inventory);
        }

        private static GearItem CreateGearItem(Thing thing)
        {
            return new GearItem
            {
                Name = thing.LabelCap,
                Quality = thing.TryGetQuality(out var qc) ? qc.ToString() : "Normal",
                Durability = thing.def.useHitPoints ? (float)thing.HitPoints / thing.MaxHitPoints : 1f,
                Count = thing.stackCount
            };
        }
    }

    public struct GearItem
    {
        /// <summary>
        /// 装备物品的名称。
        /// </summary>
        public string Name;
        /// <summary>
        /// 装备物品的质量（例如，“糟糕”、“普通”、“优秀”）。
        /// </summary>
        public string Quality;
        /// <summary>
        /// 装备物品的耐久度，范围从 0 到 1。
        /// </summary>
        public float Durability;
        /// <summary>
        /// 装备物品的堆叠数量。
        /// </summary>
        public int Count;
    }
}
