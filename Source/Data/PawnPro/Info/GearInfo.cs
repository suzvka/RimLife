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
    /// <summary>
    /// 表示 Pawn 装备和库存信息的快照。
    /// </summary>
    /// <remarks>此数据为快照，不保证时序一致性。</remarks>
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
            // 当物品没有质量组件时，使用 QualityCategory.Normal 的本地化标签而非英文硬编码。
            string quality = thing.TryGetQuality(out var qc)
                ? qc.ToString()
                : QualityCategory.Normal.ToString();

            float durability = thing.def.useHitPoints ? (float)thing.HitPoints / thing.MaxHitPoints : 1f;

            return new GearItem
            {
                Name = thing.LabelCap,
                Quality = quality,
                Durability = durability,
                ConditionLabel = SemanticLabels.MapGearCondition(durability),
                Count = thing.stackCount
            };
        }

        public string ToPrompt()
        {
            var sb = new StringBuilder(256);

            if (WornGear != null && WornGear.Count > 0)
            {
                var parts = WornGear.Select(g => FormatGearPrompt(g));
                sb.Append("穿着: ");
                sb.Append(string.Join(", ", parts));
            }

            if (Inventory != null && Inventory.Count > 0)
            {
                if (sb.Length > 0) sb.Append("; ");
                var parts = Inventory.Select(g => FormatGearPrompt(g));
                sb.Append("背包: ");
                sb.Append(string.Join(", ", parts));
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string FormatGearPrompt(GearItem g)
        {
            string extra = !string.IsNullOrEmpty(g.Quality) ? $"{g.Quality}, {g.ConditionLabel}" : g.ConditionLabel;
            if (g.Count > 1)
                return $"{g.Name} ×{g.Count}({extra})";
            else
                return $"{g.Name}({extra})";
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
        /// 装备物品的耐久语义标签 (例如 "Pristine" / "Broken")。
        /// </summary>
        public string ConditionLabel;
        /// <summary>
        /// 装备物品的堆叠数量。
        /// </summary>
        public int Count;
    }
}
