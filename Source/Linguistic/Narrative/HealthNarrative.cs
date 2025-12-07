using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimLife
{
    #region Enums for Linguistic Representation
    /// <summary>
    /// 形容词：严重程度
    /// </summary>
    public enum SeverityAdj
    {
        None,
        Trivial,    // 极轻微
        Minor,      // 轻微
        Moderate,   // 中等
        Severe,     // 严重
        Critical    // 危急
    }

    /// <summary>
    /// 形容词：包扎质量
    /// </summary>
    public enum TendingAdj
    {
        None,
        Untended,         // 未包扎
        PoorlyTended,     // 包扎差
        WellTended,       // 包扎良好
        SkillfullyTended, // 熟练包扎
        PerfectlyTended   // 精良包扎
    }

    /// <summary>
    /// 形容词：免疫进展
    /// </summary>
    public enum ImmunityAdj
    {
        None,
        Low,        // 免疫低
        Developing, // 正在建立
        Strong,     // 免疫强
        Immune      // 已免疫
    }

    #endregion

    /// <summary>
    /// 单个健康状态（以名词为锚点）的可用描述词集合。
    /// 采用强类型枚举以提高健壮性并简化本地化。
    /// </summary>
    public class HealthNarrative
    {
        #region Core Linguistic Properties
        /// <summary>
        /// 核心名词：健康状态的名称（来源于 Hediff 的 Label）。
        /// 例如："Gunshot", "Flu"。
        /// </summary>
        public string Noun { get; set; }

        /// <summary>
        /// 相关名词（关系客体）。常见键：
        /// - "OnPart": 受影响的身体部位（如 "Left Arm"）。
        /// </summary>
        public Dictionary<string, string> RelatedNouns { get; set; } = new();

        /// <summary>
        /// 形容词：严重程度。
        /// </summary>
        public SeverityAdj Severity { get; set; }

        /// <summary>
        /// 形容词：包扎质量。
        /// </summary>
        public TendingAdj Tending { get; set; }

        /// <summary>
        /// 形容词：免疫进展。
        /// </summary>
        public ImmunityAdj Immunity { get; set; }

        #endregion

        #region Tags for Logic and Selection
        /// <summary>
        /// 分类标签：用于模板选择或逻辑判断。
        /// </summary>
        public HealthGroupTag Group { get; set; }

        /// <summary>
        /// 状态标签：是否流血。
        /// </summary>
        public bool IsBleeding { get; set; }

        /// <summary>
        /// 状态标签：是否为永久性伤疤。
        /// </summary>
        public bool IsPermanent { get; set; }

        /// <summary>
        /// 状态标签：是否为感染。
        /// </summary>
        public bool IsInfection { get; set; }

        /// <summary>
        /// 状态标签：是否会随时间自愈消失。
        /// </summary>
        public bool CompDisappears { get; set; }
        #endregion

        public HealthNarrative() { }

        /// <summary>
        /// 从单个健康条目构造一条 HealthNarrative（将数值映射为枚举）。
        /// </summary>
        public HealthNarrative(HealthEntry e)
        {
            // 开放集：名词（直接赋值）
            Noun = e.Label ?? string.Empty;
            if (!string.IsNullOrEmpty(e.Part) && e.Part != "Whole body")
                RelatedNouns["OnPart"] = e.Part;

            // 闭集：形容词（映射到枚举）
            Severity = MapSeverityToEnum(e.Severity);
            Tending = MapTendingToEnum(e.TendQuality);
            Immunity = MapImmunityToEnum(e.Immunity);

            // 闭集：标签（直接赋值）
            Group = e.GroupTag;
            IsBleeding = e.IsBleeding;
            IsPermanent = e.IsPermanent;
            IsInfection = e.IsInfection;
            CompDisappears = e.CompDisappears;
        }

        /// <summary>
        /// 从 HealthInfo 批量构造所有可见健康状态的叙述单元。
        /// </summary>
        public static List<HealthNarrative> From(HealthInfo info)
        {
            if (info?.Injuries == null) return new List<HealthNarrative>();
            
            return info.Injuries.Select(e => new HealthNarrative(e)).ToList();
        }

        #region Mapping Rules
        private static SeverityAdj MapSeverityToEnum(float severity)
        {
            if (severity < 0.15f) return SeverityAdj.Trivial;
            if (severity < 0.35f) return SeverityAdj.Minor;
            if (severity < 0.65f) return SeverityAdj.Moderate;
            if (severity < 0.85f) return SeverityAdj.Severe;
            return SeverityAdj.Critical;
        }

        private static TendingAdj MapTendingToEnum(float tendQuality)
        {
            if (tendQuality <= 0f) return TendingAdj.Untended;
            if (tendQuality < 0.25f) return TendingAdj.PoorlyTended;
            if (tendQuality < 0.6f) return TendingAdj.WellTended;
            if (tendQuality < 0.99f) return TendingAdj.SkillfullyTended;
            return TendingAdj.PerfectlyTended;
        }

        private static ImmunityAdj MapImmunityToEnum(float immunity)
        {
            if (immunity <= 0f) return ImmunityAdj.None;
            if (immunity < 0.25f) return ImmunityAdj.Low;
            if (immunity < 0.5f) return ImmunityAdj.Developing;
            if (immunity < 0.99f) return ImmunityAdj.Strong;
            return ImmunityAdj.Immune;
        }
        #endregion
    }
}
