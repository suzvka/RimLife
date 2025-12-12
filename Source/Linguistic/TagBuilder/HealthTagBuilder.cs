using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 健康模块专用：负责把 Health 相关数据转换为标签 + 语义轴。
    /// 
    /// 注意：
    /// - 这是少数允许写“健康特例”的地方之一。
    /// - 生成的结果是通用的数据结构（标签/轴），之后的流程完全模块无关。
    /// </summary>
    public static class HealthTagBuilder
    {
        /// <summary>
        /// 从 HealthNarrative 中抽取标签 + 轴，构造 SemanticProfile。
        /// 
        /// 推荐标签：
        /// - topic.health
        /// - health.injury / health.illness / health.bleeding / ...
        /// - health.injury.severe / health.injury.minor / ...
        /// - bodypart.arm / bodypart.leg / ...
        /// - polarity.negative / polarity.positive
        /// 
        /// 推荐轴：
        /// - Intensity: 伤势严重度/痛感强度等归一化值
        /// - Risk: 死亡/恶化风险
        /// - Progress: 治疗/康复进度
        /// </summary>
        public static SemanticProfile BuildSemanticProfile(HealthNarrative narrative)
        {
            if (narrative == null) throw new ArgumentNullException(nameof(narrative));

            var tags = new List<TagId>();

            // 必备领域标签
            tags.Add(new TagId("health"));
            tags.Add(new TagId("topic.health"));
            tags.Add(new TagId("health.injury"));

            // 示例：按严重度添加标签
            switch (narrative.Severity)
            {
                case SeverityAdj.Minor:
                    tags.Add(new TagId("health.injury.minor"));
                    tags.Add(new TagId("intensity.low"));
                    break;
                case SeverityAdj.Moderate:
                    tags.Add(new TagId("health.injury.moderate"));
                    tags.Add(new TagId("intensity.mid"));
                    break;
                case SeverityAdj.Severe:
                    tags.Add(new TagId("health.injury.severe"));
                    tags.Add(new TagId("intensity.high"));
                    break;
            }

            // 示例：处理状态
            if (narrative.Tending != TendingAdj.Untended)
            {
                tags.Add(new TagId("health.tended"));
                tags.Add(new TagId("health.tended." + narrative.Tending.ToString().ToLowerInvariant()));
            }

            // 示例：身体部位
            if (narrative.RelatedNouns != null &&
                narrative.RelatedNouns.TryGetValue("OnPart", out var partLabel) &&
                !string.IsNullOrWhiteSpace(partLabel))
            {
                var normalized = partLabel.Trim().ToLowerInvariant().Replace(' ', '_');
                tags.Add(new TagId($"bodypart.{normalized}"));
            }

            // 语义轴：这里只给个示例，你可以按需要更精细地映射。
            var axes = new List<AxisValue>();

            // 用严重度映射 Intensity 轴（0..1）
            float intensity = narrative.Severity switch
            {
                SeverityAdj.Minor => 0.3f,
                SeverityAdj.Moderate => 0.6f,
                SeverityAdj.Severe => 0.9f,
                _ => 0.5f
            };
            axes.Add(new AxisValue(SemanticAxisId.Intensity, intensity));

            // 用是否有生命危险/持续流血等信息映射 Risk 轴
            float risk = 0f;
            if (narrative.IsBleeding) risk += 0.4f;
            // if (narrative.MightBeFatal) risk += 0.5f;
            risk = Math.Min(1f, risk);
            axes.Add(new AxisValue(SemanticAxisId.Risk, risk));

            var tagSet = new TagSet(tags);
            return new SemanticProfile(tagSet, axes);
        }

        /// <summary>
        /// 方便函数：直接从 PawnPro + HealthNarrative 生成一个 Health Fact。
        /// 上层可以把这个 Fact 放入 Fact 池中，让 LinguisticEngine 处理。
        /// </summary>
        public static Fact BuildHealthFact(
            PawnPro pawn,
            HealthNarrative narrative,
            float salience)
        {
            var profile = BuildSemanticProfile(narrative);

            return new Fact(
                topic: FactTopic.Health,
                subtopic: "Injury", // 或根据 narrative 类型区分
                salience: salience,
                subject: pawn,
                target: null,
                semantics: profile,
                sourcePayload: narrative);
        }
    }
}
