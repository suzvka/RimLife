using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 表示 Pawn 健康信息的快照。
    /// 注意：此数据为快照，不保证其时序一致性。
    /// 已统一为单个类（移除了之前的只读结构体版本）。
    /// </summary>
    public class HealthInfo
    {
        /// <summary>
        /// Pawn 的总疼痛程度，通常范围为 0 到 1。
        /// </summary>
        public float SummaryPain;

        /// <summary>
        /// Pawn 的总失血率。
        /// </summary>
        public float SummaryBleedRate;

        /// <summary>
        /// Pawn 关键能力的摘要，例如“移动”和“操作”。
        /// 存储在字典中以避免硬编码。
        /// </summary>
        public IReadOnlyDictionary<string, float> Capacities { get; }

        /// <summary>
        /// 所有可见的伤害、疾病和其他健康状况（hediffs）的列表。
        /// </summary>
        public IReadOnlyList<HealthEntry> Injuries { get; }

        #region Factory Methods

        // 定义哪些能力被视为“关键”，以防止 Capacities 字典变得过大。
        private static readonly PawnCapacityDef[] KeyCapacityDefs =
        [
            PawnCapacityDefOf.Moving,
            PawnCapacityDefOf.Manipulation,
            PawnCapacityDefOf.Talking,
            PawnCapacityDefOf.Consciousness,
            PawnCapacityDefOf.Sight,
            PawnCapacityDefOf.Hearing,
            PawnCapacityDefOf.Breathing
        ];

        private HealthInfo()
        {
            Capacities = new Dictionary<string, float>();
            Injuries = new List<HealthEntry>();
        }

        private HealthInfo(float summaryPain, float summaryBleedRate, IReadOnlyDictionary<string, float> capacities, IReadOnlyList<HealthEntry> injuries)
        {
            SummaryPain = summaryPain;
            SummaryBleedRate = summaryBleedRate;
            Capacities = capacities;
            Injuries = injuries;
        }

        /// <summary>
        /// 从 Pawn 创建 HealthInfo 快照。必须在主线程上调用。
        /// </summary>
        public static HealthInfo CreateFrom(Pawn p)
        {
            if (p?.health == null) return new HealthInfo();

            // 汇总疼痛和失血率（空安全）。
            var summaryPain = p.health.hediffSet?.PainTotal ?? 0f;
            var summaryBleedRate = p.health.hediffSet?.BleedRateTotal ?? 0f;

            // 获取能力水平（限制在 0-1 之间，因为 RimWorld 有时会超过 1）。
            var capacities = new Dictionary<string, float>();
            if (p.health.capacities != null)
            {
                foreach (var def in KeyCapacityDefs)
                {
                    try
                    {
                        float level = p.health.capacities?.GetLevel(def) ?? 0f;
                        capacities[def.defName] = Mathf.Clamp01(level);
                    }
                    catch
                    {
                        // 忽略能力异常。
                    }
                }
            }
            
            // 填充伤害和其他健康状况列表。
            var injuries = new List<HealthEntry>();
            var hediffs = p.health.hediffSet?.hediffs;
            if (hediffs != null)
            {
                foreach (var h in hediffs)
                {
                    if (h == null || !h.Visible) continue;

                    var tendQuality = 0f;
                    if (h is Hediff tendable)
                    {
                        tendQuality = tendable.TendPriority;
                    }

                    var immunity = 0f;
                    var immunizable = h.TryGetComp<HediffComp_Immunizable>();
                    if (immunizable != null)
                    {
                        immunity = immunizable.Immunity;
                    }

                    var compDisappears = h.TryGetComp<HediffComp_Disappears>() != null;

                    var entry = new HealthEntry
                    {
                        Label = h.def?.label ?? h.LabelCap,
                        Part = h.Part?.Label ?? "Whole body",
                        Severity = h.Severity,
                        IsBleeding = h.Bleeding,
                        IsPermanent = h.IsPermanent(),
                        IsInfection = h.def?.isInfection ?? false,
                        GroupTag = GetHealthGroupTag(h),
                        TendQuality = tendQuality,
                        AgeTicks = h.ageTicks,
                        Immunity = immunity,
                        CompDisappears = compDisappears
                    };
                    injuries.Add(entry);
                }
            }
            
            return new HealthInfo(summaryPain, summaryBleedRate, capacities, injuries);
        }

        /// <summary>
        /// 通过将工作分派到主线程来异步创建 HealthInfo 快照。
        /// 这是一个如何从后台线程安全地收集游戏数据的示例。
        /// </summary>
        public static Task<HealthInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new HealthInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }


        private static HealthGroupTag GetHealthGroupTag(Hediff h)
        {
            if (h?.def == null) return HealthGroupTag.Other;
            if (h.def.isInfection) return HealthGroupTag.Disease;
            if (h.Bleeding) return HealthGroupTag.Trauma;
            if (h.IsPermanent()) return HealthGroupTag.Permanent;
            if (h.def.makesSickThought) return HealthGroupTag.Ill;
            return HealthGroupTag.Other;
        }

        #endregion
    }

    public enum HealthGroupTag
    {
        Other,
        Disease,
        Trauma,
        Permanent,
        Ill
    }

    public struct HealthEntry
    {
        /// <summary>
        /// 状况的名称（例如，“枪伤”）。
        /// </summary>
        public string Label;
        /// <summary>
        /// 受影响的身体部位（例如，“左臂”）。
        /// </summary>
        public string Part;
        /// <summary>
        /// 状况的严重程度。
        /// </summary>
        public float Severity;
        /// <summary>
        /// 指示状况是否正在流血。
        /// </summary>
        public bool IsBleeding;
        /// <summary>
        /// 指示状况是否是永久性的，如疤痕。
        /// </summary>
        public bool IsPermanent;
        /// <summary>
        /// 指示状况是否是感染或疾病。
        /// </summary>
        public bool IsInfection;
        /// <summary>
        /// 用于分组相似状况的标签（例如，“创伤”、“疾病”）。
        /// </summary>
        public HealthGroupTag GroupTag;
        /// <summary>
        /// 医疗处理的质量（如果有）。范围从 0 到 1。
        /// </summary>
        public float TendQuality;
        /// <summary>
        /// 状况的持续时间（以游戏刻度为单位）。
        /// </summary>
        public int AgeTicks;
        /// <summary>
        /// Pawn 对疾病的免疫力（如果适用）。范围从 0 到 1。
        /// </summary>
        public float Immunity;
        /// <summary>
        /// 指示状况是否有倒计时并会自行消失。
        /// </summary>
        public bool CompDisappears;
    }
}
