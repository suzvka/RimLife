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
    /// 表示 Pawn 需求的快照。
    /// </summary>
    /// <remarks>此数据为快照，不保证时序一致性。</remarks>
    public class NeedsInfo
    {
        /// <summary>所有需求的统一列表，含紧急度语义标签。</summary>
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
                        float cur = need.CurLevelPercentage;

                        // baseLevel 是该需求的自然平衡点，低于此值表示匮乏状态。
                        float thresholdLow = need.def?.baseLevel > 0f
                            ? need.def.baseLevel
                            : 0.3f;

                        // 紧急条件：严重低于平衡点，或濒临耗尽。
                        bool critical = cur < thresholdLow * 0.5f || cur < 0.1f;

                        var entry = new NeedEntry
                        {
                            DefName = need.def?.defName ?? need.LabelCap,
                            Label = need.LabelCap,
                            CurLevel = cur,
                            ThresholdLow = thresholdLow,
                            IsCritical = critical,
                            NeedUrgency = SemanticLabels.MapNeedUrgency(need.def?.defName, cur)
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

        public string ToPrompt()
        {
            if (AllNeeds == null || AllNeeds.Count == 0) return null;

            var parts = new List<string>();
            foreach (var need in AllNeeds)
            {
                if (need.NeedUrgency != "Normal" || need.CurLevel < 0.5f)
                {
                    parts.Add($"{need.Label}: {need.NeedUrgency}");
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        public static Task<NeedsInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new NeedsInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }
    }



    public struct NeedEntry
    {
        /// <summary>需求 defName，如 "Food"、"Beauty"。</summary>
        public string DefName;
        /// <summary>显示名称。</summary>
        public string Label;
        /// <summary>当前值 (0-1)。</summary>
        public float CurLevel;
        /// <summary>匮乏阈值：优先读取 NeedDef.baseLevel，无则回退 0.3。</summary>
        public float ThresholdLow;
        /// <summary>是否处于极低状态（创建快照时预判）。</summary>
        public bool IsCritical;
        /// <summary>语义紧急标签，如 "Starving"、"Rested"。</summary>
        public string NeedUrgency;
    }
}
