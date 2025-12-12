using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 上层剧本模块用来描述“我想要的一句话”的筛选条件。
    /// 一个 SentenceConstraint 对应一个句子槽位。
    /// </summary>
    public sealed class SentenceConstraint
    {
        /// <summary>
        /// 想聊的事实话题，比如 Health / Mood / Environment / Relationship。
        /// </summary>
        public FactTopic Topic { get; set; }

        /// <summary>
        /// 可选：子话题字符串，例如 "Injury" / "Tending" / "OverallMood"。
        /// 可以由 TagBuilder 约定使用。
        /// </summary>
        public string Subtopic { get; set; }

        /// <summary>
        /// 希望的显著度范围。
        /// 用于从 Fact 池中挑选更重要/更不重要的事实。
        /// </summary>
        public float? MinSalience { get; set; }
        public float? MaxSalience { get; set; }

        /// <summary>
        /// 要求 Fact 必须包含的标签（硬约束）。
        /// 例如：health.injury / health.injury.severe。
        /// </summary>
        public List<string> RequiredFactTags { get; } = new List<string>();

        /// <summary>
        /// 偏好标签（软约束）。
        /// 例如“优先选严重伤口，但不强制”。
        /// </summary>
        public List<string> PreferredFactTags { get; } = new List<string>();

        /// <summary>
        /// 话语功能：Intro / Elaboration / Contrast / Result。
        /// 用于后续排序/风格控制。
        /// </summary>
        public DiscourseFunction? FunctionHint { get; set; }

        /// <summary>
        /// 句法类型：完整句子 / 碎片 / 修饰语。
        /// </summary>
        public SyntacticType? SyntacticTypeHint { get; set; }

        /// <summary>
        /// 约束在模板选取时的权重。
        /// </summary>
        public float Weight { get; set; } = 1f;
    }

    /// <summary>
    /// 语言引擎：负责从 Fact 池 + 模板 Def 中，选出一组 SentencePlan。
    /// 不负责具体文本生成。
    /// </summary>
    public sealed class LinguisticEngine
    {
        private readonly IEnumerable<NarrativeTemplateDef> _templates;

        public LinguisticEngine(IEnumerable<NarrativeTemplateDef> templates)
        {
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        }

        /// <summary>
        /// 根据一组 SentenceConstraint，从 Fact 池中挑选对应的 SentencePlan 列表。
        /// 每个 constraint 至多生成一个 SentencePlan（也可以允许多个，视需求而定）。
        /// </summary>
        public IList<SentencePlan> BuildPlans(
            IReadOnlyList<Fact> facts,
            IReadOnlyList<SentenceConstraint> constraints)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));

            var result = new List<SentencePlan>();

            for (int i = 0; i < constraints.Count; i++)
            {
                var c = constraints[i];
                var fact = SelectBestFact(facts, c);
                if (fact == null)
                    continue;

                var template = SelectBestTemplate(fact, c);
                if (template == null)
                    continue;

                // 可以根据约束顺序 + 模板/Fact 打分综合成一个 score。
                float score = c.Weight;
                var plan = new SentencePlan(fact, template, score);
                result.Add(plan);
            }

            return result;
        }

        /// <summary>
        /// 从 Fact 列表中挑选最符合约束的一条。
        /// </summary>
        private Fact SelectBestFact(
            IReadOnlyList<Fact> facts,
            SentenceConstraint c)
        {
            // 1. 按 topic + subtopic 粗过滤
            IEnumerable<Fact> candidates = facts
                .Where(f => f.Topic == c.Topic);

            if (!string.IsNullOrWhiteSpace(c.Subtopic))
            {
                candidates = candidates.Where(f =>
                    string.Equals(f.Subtopic, c.Subtopic, StringComparison.OrdinalIgnoreCase));
            }

            // 2. 按显著度范围过滤
            if (c.MinSalience.HasValue)
                candidates = candidates.Where(f => f.Salience >= c.MinSalience.Value);
            if (c.MaxSalience.HasValue)
                candidates = candidates.Where(f => f.Salience <= c.MaxSalience.Value);

            // 3. 按 RequiredFactTags 做硬过滤
            if (c.RequiredFactTags.Count > 0)
            {
                candidates = candidates.Where(f =>
                {
                    var tagSet = f.Tags;
                    foreach (var tagStr in c.RequiredFactTags)
                    {
                        if (string.IsNullOrWhiteSpace(tagStr)) continue;
                        var tag = new TagId(tagStr);
                        if (!tagSet.Contains(tag))
                            return false;
                    }
                    return true;
                });
            }

            // 4. 在剩余候选中，根据 PreferredFactTags + Salience 打一个简单分数
            Fact best = null;
            float bestScore = float.MinValue;

            foreach (var f in candidates)
            {
                float score = f.Salience;

                // 偏好标签加分
                if (c.PreferredFactTags.Count > 0)
                {
                    foreach (var tagStr in c.PreferredFactTags)
                    {
                        if (string.IsNullOrWhiteSpace(tagStr)) continue;
                        var tag = new TagId(tagStr);
                        if (f.Tags.Contains(tag))
                        {
                            score += 0.1f; // 简单加分系数，之后可以调参
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = f;
                }
            }

            return best;
        }

        /// <summary>
        /// 从 NarrativeTemplateDef 列表中挑选最适合某个 Fact + 约束的模板。
        /// 使用：
        /// - def.topic
        /// - def.requiredTags
        /// - def.tagWeights
        /// 等字段。
        /// </summary>
        private NarrativeTemplateDef SelectBestTemplate(
            Fact fact,
            SentenceConstraint c)
        {
            NarrativeTemplateDef best = null;
            float bestScore = float.MinValue;

            foreach (var def in _templates)
            {
                if (!IsCandidate(def, fact, c))
                    continue;

                float score = ScoreTemplate(def, fact, c);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                }
            }

            return best;
        }

        private bool IsCandidate(
            NarrativeTemplateDef def,
            Fact fact,
            SentenceConstraint c)
        {
            if (def == null) return false;

            // 1. topic 兼容性
            if (!string.IsNullOrEmpty(def.topic))
            {
                var topicName = fact.Topic.ToString();
                if (!string.Equals(def.topic, topicName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 2. 话语功能 hint
            if (c.FunctionHint.HasValue && def.function != c.FunctionHint.Value)
                return false;

            // 3. 句法类型 hint
            if (c.SyntacticTypeHint.HasValue && def.syntacticType != c.SyntacticTypeHint.Value)
                return false;

            // 4. 模板 requiredTags
            if (def.requiredTags != null && def.requiredTags.Count > 0)
            {
                foreach (var tagStr in def.requiredTags)
                {
                    if (string.IsNullOrWhiteSpace(tagStr)) continue;
                    var tag = new TagId(tagStr);
                    if (!fact.Tags.Contains(tag))
                        return false;
                }
            }

            return true;
        }

        private float ScoreTemplate(
            NarrativeTemplateDef def,
            Fact fact,
            SentenceConstraint c)
        {
            float score = def.baseWeight;

            // 1. 模板自己的 tagWeights 与 Fact.Tags 的匹配
            if (def.tagWeights != null && def.tagWeights.Count > 0)
            {
                foreach (var kv in def.tagWeights)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                        continue;
                    if (kv.Value == 0f)
                        continue;

                    var tag = new TagId(kv.Key);
                    if (fact.Tags.Contains(tag))
                    {
                        score += kv.Value;
                    }
                }
            }

            // 2. 约束的 PreferredFactTags 也可以小幅加分
            if (c.PreferredFactTags.Count > 0)
            {
                foreach (var tagStr in c.PreferredFactTags)
                {
                    if (string.IsNullOrWhiteSpace(tagStr)) continue;
                    var tag = new TagId(tagStr);
                    if (fact.Tags.Contains(tag))
                    {
                        score += 0.1f;
                    }
                }
            }

            return score;
        }
    }

    /// <summary>
    /// 模板引擎：负责把 SentencePlan + ISlotRealizer 转成最终字符串。
    /// 只关心模板字符串和槽位，不关心游戏细节。
    /// </summary>
    public sealed class TemplateEngine
    {
        private static readonly Regex SlotRegex =
            new Regex(@"{(?<name>[^{}]+)}", RegexOptions.Compiled);

        private readonly ISlotRealizer _slotRealizer;

        public TemplateEngine(ISlotRealizer slotRealizer)
        {
            _slotRealizer = slotRealizer ?? throw new ArgumentNullException(nameof(slotRealizer));
        }

        public string Realize(
            SentencePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var templateText = plan.Template.GetTemplate();
            if (string.IsNullOrWhiteSpace(templateText))
                return $"[{plan.Template.defName}_EmptyTemplate]";

            return RealizeTemplate(templateText, plan.SourceFact);
        }

        /// <summary>
        /// 将模板文本中的 {SLOT} 替换为具体词汇。
        /// </summary>
        public string RealizeTemplate(
            string templateText,
            Fact fact)
        {
            if (templateText == null) throw new ArgumentNullException(nameof(templateText));
            if (fact == null) throw new ArgumentNullException(nameof(fact));

            // 解析所有 {SlotName}
            var result = SlotRegex.Replace(templateText, match =>
            {
                var name = match.Groups["name"].Value;
                if (!SlotRequest.TryParse(name, out var request))
                {
                    // 未识别槽位，保持原样或返回空字符串
                    return match.Value;
                }

                var text = _slotRealizer.RealizeSlot(request, fact);
                return text ?? string.Empty;
            });

            return result;
        }
    }
}
