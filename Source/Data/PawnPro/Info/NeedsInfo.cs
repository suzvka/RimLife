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
    /// 表示 Pawn 需求的快照。
    /// 注意：此数据为快照，其时序一致性不被保证。
    /// </summary>
    public class NeedsInfo
    {
        // 所有的需求都放入这个列表，不再区分 Food/Rest
        public IReadOnlyList<NeedEntry> AllNeeds { get; }

        private NeedsInfo()
        {
            AllNeeds = new List<NeedEntry>();
        }

        private NeedsInfo(IReadOnlyList<NeedEntry> allNeeds)
        {
            AllNeeds = allNeeds;
        }

        public static NeedsInfo CreateFrom(Pawn p)
        {
            if (p?.needs == null) return new NeedsInfo();

            var allNeedsList = new List<NeedEntry>();
            var allNeeds = p.needs.AllNeeds;
            if (allNeeds != null)
            {
                foreach (var need in allNeeds)
                {
                    if (need == null) continue;
                    try
                    {
                        float thresholdLow = 0.3f; // 简单阈值占位
                        float cur = need.CurLevelPercentage;
                        bool critical = cur < (thresholdLow * 0.5f) || cur < 0.15f;

                        var entry = new NeedEntry
                        {
                            DefName = need.def?.defName ?? need.LabelCap,
                            Label = need.LabelCap,
                            CurLevel = cur,
                            ThresholdLow = thresholdLow,
                            IsCritical = critical
                        };
                        allNeedsList.Add(entry);
                    }
                    catch
                    {
                        // 忽略单个需求的异常
                    }
                }
            }
            return new NeedsInfo(allNeedsList);
        }

        public static Task<NeedsInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new NeedsInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }
    }



    public struct NeedEntry
    {
        public string DefName;      // 需求ID (例如 "Food", "Beauty")
        public string Label;        // 显示名
        public float CurLevel;      // 当前值 (0-1)
        public float ThresholdLow;  // 低于此值视为匮乏 (从 XML 读取)
        public bool IsCritical;     // 是否处于极低状态 (Extractor 预判)
    }
}
