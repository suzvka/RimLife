using System;
using System.Collections.Generic;
using System.Linq;

namespace RimLife
{
    

    /// <summary>
    /// 一个语义 profile，描述某个 Fact 的“语义视图”：
    /// - Tags: 离散特征标签（topic/polarity/domain-specific 等）
    /// - Axes: 可计算的语义轴强度（Intensity/Risk/Progress 等）
    /// 
    /// 它是 Fact 的“语言学视角”，供模板选择和词汇选择使用。
    /// </summary>
    public sealed class SemanticTemplate
    {
        public TagSet Tags { get; }
        public IReadOnlyList<NarrativeAxisValue> Axes { get; }

        public SemanticTemplate(TagSet tags, IReadOnlyList<NarrativeAxisValue> axes)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Axes = axes ?? throw new ArgumentNullException(nameof(axes));
        }

        /// <summary>
        /// 读取某个轴的数值，如果不存在则返回 null。
        /// </summary>
        public float? GetAxis(NarrativeAxisId axisId)
        {
            foreach (var axis in Axes)
            {
                if (axis.Axis == axisId)
                    return axis.Value;
            }
            return null;
        }

        /// <summary>
        /// 工具函数：根据数值返回离散强度标签（intensity.low/mid/high）。
        /// 分桶逻辑可按需要调整。
        /// </summary>
        public TagId GetIntensityBucketTag(NarrativeAxisId axisId)
        {
            var value = GetAxis(axisId) ?? 0f;
            if (value < 0.33f) return new TagId("intensity.low");
            if (value < 0.66f) return new TagId("intensity.mid");
            return new TagId("intensity.high");
        }
    }
}
