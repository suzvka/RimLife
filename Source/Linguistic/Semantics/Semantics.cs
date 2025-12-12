using System;
using System.Collections.Generic;
using System.Linq;

namespace RimLife
{
    /// <summary>
    /// 系统中所有“可计算”的语义轴。
    /// 这些轴是跨模块的：健康/心情/环境都可以映射到这些维度。
    /// 目前设计：
    /// - Intensity: 强度/程度，统一用 0..1 表示（0=极弱, 1=极强）
    /// - Risk: 风险/危害程度 (0=几乎无风险, 1=极高风险)
    /// - Progress: 进展/好转程度 (0=未好转甚至恶化, 1=完全恢复)
    /// 如果未来需要，可以在这里继续扩展。
    /// </summary>
    public enum SemanticAxisId
    {
        Intensity,
        Risk,
        Progress,
    }

    /// <summary>
    /// 某个语义轴上的数值。
    /// 统一使用 0..1 范围（由各模块的 TagBuilder 负责映射）：
    /// - 例如 Health 严重度 -> Axis(Intensity, 0.8)
    /// - Mood 值很低 -> Axis(Intensity, 0.7)
    /// - Room impressiveness 很好 -> Axis(Intensity, 0.9) + polarity.positive tag
    /// </summary>
    public readonly struct AxisValue
    {
        public SemanticAxisId Axis { get; }
        public float Value { get; }

        public AxisValue(SemanticAxisId axis, float value)
        {
            Axis = axis;
            Value = value;
        }
    }

    /// <summary>
    /// 统一的标签 ID。
    /// 内部仍然使用 string，但统一管理，避免魔法字符串散落各处。
    /// 
    /// 推荐命名约定：
    /// - topic.*          : 领域，例如 topic.health / topic.mood / topic.environment
    /// - polarity.*       : 极性，例如 polarity.positive / polarity.negative / polarity.neutral
    /// - intensity.*      : 分桶后的强度标签，例如 intensity.low / intensity.mid / intensity.high
    /// - health.*         : 医疗相关，例如 health.injury / health.injury.severe / health.injury.multiple
    /// - mood.*           : 心情相关
    /// - env.*            : 环境相关
    /// - bodypart.*       : 身体部位，例如 bodypart.arm / bodypart.leg / bodypart.head
    /// - risk.*           : 风险等级，例如 risk.low / risk.mid / risk.high
    /// 
    /// 这些只是推荐命名，不限制扩展。
    /// </summary>
    public readonly struct TagId : IEquatable<TagId>
    {
        public string Value { get; }

        public TagId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override string ToString() => Value;

        public bool Equals(TagId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is TagId other && Equals(other);

        public override int GetHashCode() =>
            Value?.GetHashCode() ?? 0;

        public static implicit operator TagId(string value) => new TagId(value);
    }

    /// <summary>
    /// 标签集合的轻量封装，方便做交集/包含判断。
    /// 用于：
    /// - Fact 的标签（来自 TagBuilder）
    /// - 模板 requiredTags / preferredTags
    /// - 词汇自身的标签（LexicalItem）
    /// 
    /// 当前版本不携带“每个标签的权重”，
    /// 数值信息统一放在 SemanticAxis 中。
    /// </summary>
    public sealed class TagSet
    {
        private readonly HashSet<TagId> _tags;

        public IReadOnlyCollection<TagId> Tags => _tags;

        public TagSet(IEnumerable<TagId> tags)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            _tags = new HashSet<TagId>(tags);
        }

        /// <summary>
        /// 工具方法：从 string 集合构建。
        /// </summary>
        public static TagSet FromStrings(IEnumerable<string> tags)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            return new TagSet(tags.Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Select(s => new TagId(s)));
        }

        /// <summary>
        /// 是否包含指定标签。
        /// </summary>
        public bool Contains(TagId tag) => _tags.Contains(tag);

        /// <summary>
        /// 是否与另一个标签集合有交集。
        /// 常用于“词汇标签是否与 Fact 标签匹配”。
        /// </summary>
        public bool Intersects(TagSet other)
        {
            if (other == null) return false;
            return other._tags.Any(t => _tags.Contains(t));
        }

        /// <summary>
        /// 是否包含 other 中的所有标签。
        /// 常用于“Fact 是否满足模板 requiredTags”。
        /// </summary>
        public bool ContainsAll(TagSet other)
        {
            if (other == null) return true;
            return other._tags.All(t => _tags.Contains(t));
        }
    }

    /// <summary>
    /// 一个语义 profile，描述某个 Fact 的“语义视图”：
    /// - Tags: 离散特征标签（topic/polarity/domain-specific 等）
    /// - Axes: 可计算的语义轴强度（Intensity/Risk/Progress 等）
    /// 
    /// 它是 Fact 的“语言学视角”，供模板选择和词汇选择使用。
    /// </summary>
    public sealed class SemanticProfile
    {
        public TagSet Tags { get; }
        public IReadOnlyList<AxisValue> Axes { get; }

        public SemanticProfile(TagSet tags, IReadOnlyList<AxisValue> axes)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Axes = axes ?? throw new ArgumentNullException(nameof(axes));
        }

        /// <summary>
        /// 读取某个轴的数值，如果不存在则返回 null。
        /// </summary>
        public float? GetAxis(SemanticAxisId axisId)
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
        public TagId GetIntensityBucketTag(SemanticAxisId axisId)
        {
            var value = GetAxis(axisId) ?? 0f;
            if (value < 0.33f) return new TagId("intensity.low");
            if (value < 0.66f) return new TagId("intensity.mid");
            return new TagId("intensity.high");
        }
    }
}
