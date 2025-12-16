using System;
using System.Collections.Generic;
using System.Linq;

namespace RimLife
{
    /// <summary>
    /// 统一的语义入口，封装标签、语义轴以及常用派生工具。
    /// 其他组件（如 SlotRealizer/模板选择）可以直接依赖该对象，避免到处散落的语义计算。
    /// </summary>
    public sealed class SemanticProfile
    {
        public TagSet Tags { get; }
        public NarrativeAxisMap Axes { get; }
        public SemanticTemplate Template { get; }

        public SemanticProfile(TagSet tags, NarrativeAxisMap axes)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Axes = axes ?? throw new ArgumentNullException(nameof(axes));

            var axisValues = axes.Axes.Select(pair => new NarrativeAxisValue(pair.Key, pair.Value))
                                      .ToList();
            Template = new SemanticTemplate(tags, axisValues);
        }

        /// <summary>
        /// 获取指定语义轴的数值。如果轴未配置，则返回 fallback。
        /// </summary>
        public float GetAxisOrDefault(NarrativeAxisId axisId, float fallback = 0.5f)
        {
            if (Axes.Axes.TryGetValue(axisId, out var value))
                return value;

            return fallback;
        }

        public TagId GetIntensityBucket(NarrativeAxisId axisId) => Template.GetIntensityBucketTag(axisId);

        public bool HasTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId))
                return false;

            return Tags.Contains(new TagId(tagId));
        }

        /// <summary>
        /// 根据 polarity.* 标签推断语义极性。
        /// </summary>
        public SemanticPolarity GetPolarity()
        {
            if (HasTag("polarity.negative"))
                return SemanticPolarity.Negative;

            if (HasTag("polarity.positive"))
                return SemanticPolarity.Positive;

            return SemanticPolarity.Neutral;
        }

        /// <summary>
        /// 一个示例：展示如何构建语义 profile，供调试或单元测试使用。
        /// </summary>
        public static SemanticProfile CreateExample()
        {
            var tags = TagSet.FromStrings(new[]
            {
                "topic.health",
                "polarity.negative",
                "risk.high"
            });

            var axes = new NarrativeAxisMap();
            axes.Axes[NarrativeAxisId.Intensity] = 0.78f;
            axes.Axes[NarrativeAxisId.Risk] = 0.64f;
            axes.Axes[NarrativeAxisId.Progress] = 0.15f;

            return new SemanticProfile(tags, axes);
        }
    }

    public enum SemanticPolarity
    {
        Negative,
        Neutral,
        Positive
    }
}
