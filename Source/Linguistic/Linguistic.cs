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
        /// 想聊的事实话题，比如 Health / Work / Needs / ...
        /// </summary>
        public FactTopic Topic { get; set; } = FactTopic.Health;

        /// <summary>
        /// 希望这句话在篇章中扮演的功能：引入、补充、总结等。
        /// </summary>
        public DiscourseFunction Function { get; set; } = DiscourseFunction.Intro;

        /// <summary>
        /// 限制可选 Fact 的显著度下界（可空）。
        /// </summary>
        public float? MinSalience { get; set; }

        /// <summary>
        /// 限制可选 Fact 的显著度上界（可空）。
        /// </summary>
        public float? MaxSalience { get; set; }

        /// <summary>
        /// 硬约束：Fact 必须包含这些标签（标签来自 Fact.Tags）。
        /// 比如 "health.injury"、"work.cooking"。
        /// </summary>
        public List<string> RequiredFactTags { get; } = new List<string>();

        /// <summary>
        /// 软偏好：希望 Fact 在这些标签上权重更高。
        /// key: 标签；value: 偏好权重。
        /// </summary>
        public Dictionary<string, float> PreferredFactTags { get; } =
            new Dictionary<string, float>();

        /// <summary>
        /// 硬约束：模板本身必须声称关注这些标签（标签来自模板的 tagWeights）。
        /// 比如 "health.tended"。
        /// </summary>
        public List<string> RequiredTemplateTags { get; } = new List<string>();

        /// <summary>
        /// 软偏好：希望模板在这些标签上的权重大。
        /// key: 标签；value: 偏好权重。
        /// </summary>
        public Dictionary<string, float> PreferredTemplateTags { get; } =
            new Dictionary<string, float>();
    }

    /// <summary>
    /// Linguistic 核心：给一堆事实 + 一堆句子需求，选出对应的 SentencePlan。
    /// 不直接接触 Pawn / Hediff 等游戏对象，只依赖 Fact / NarrativeTemplateDef。
    /// </summary>
    public static class LinguisticEngine
    {
        /// <summary>
        /// 构造一组句子计划：
        /// - facts: 世界适配层构造好的“可说事实”池；
        /// - constraints: 上层剧本模块给出的句子槽位需求（一个对象对应一句）。
        /// 返回的 SentencePlan 列表顺序与 constraints 保持一致（过滤掉无法满足的槽位）。
        /// </summary>
        public static List<SentencePlan> BuildPlans(
            IReadOnlyList<Fact> facts,
            IReadOnlyList<SentenceConstraint> constraints)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));

            var plans = new List<SentencePlan>(constraints.Count);

            foreach (var constraint in constraints)
            {
                if (constraint == null)
                    continue;

                var fact = SelectBestFact(facts, constraint);
                if (fact == null)
                    continue;

                var template = TemplateSelector.PickBestTemplateFor(fact, constraint);
                if (template == null)
                    continue;

                var slots = BuildSlots(fact, template);

                var plan = new SentencePlan(
                    sourceFact: fact,
                    template: template,
                    slots: slots,
                    priority: (int)Math.Round(fact.Salience));

                plans.Add(plan);
            }

            return plans;
        }

        /// <summary>
        /// 在 Fact 池中，为一个句子约束挑选最合适的那个 Fact。
        /// 只看 Topic / 显著度 / 事实标签 + PreferredFactTags。
        /// </summary>
        private static Fact SelectBestFact(
            IReadOnlyList<Fact> facts,
            SentenceConstraint c)
        {
            // 先按 Topic 过滤
            IEnumerable<Fact> candidates = facts.Where(f => f.Topic == c.Topic);

            // 再按显著度范围过滤
            if (c.MinSalience.HasValue)
                candidates = candidates.Where(f => f.Salience >= c.MinSalience.Value);
            if (c.MaxSalience.HasValue)
                candidates = candidates.Where(f => f.Salience <= c.MaxSalience.Value);

            // 按 RequiredFactTags 做硬过滤
            if (c.RequiredFactTags.Count > 0)
            {
                candidates = candidates.Where(f =>
                    c.RequiredFactTags.All(tag =>
                        !string.IsNullOrEmpty(tag) && f.Tags.ContainsKey(tag)));
            }

            Fact best = null;
            float bestScore = float.MinValue;

            foreach (var fact in candidates)
            {
                float score = fact.Salience; // 显著度本身就是一个自然的排序依据

                // 对 PreferredFactTags 做一点加成
                foreach (var kv in c.PreferredFactTags)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (!fact.Tags.TryGetValue(kv.Key, out var wFact)) continue;

                    score += wFact * kv.Value;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = fact;
                }
            }

            return best;
        }

        /// <summary>
        /// 根据 Fact.Topic 和模板的语篇功能，调用不同的 SlotFiller。
        /// 当前只实现 Health 的两个分支：Intro / Elaboration。
        /// </summary>
        private static Dictionary<string, string> BuildSlots(
            Fact fact,
            NarrativeTemplateDef template)
        {
            // 未来扩展其他 Topic 时，在这里加分支即可。
            switch (fact.Topic)
            {
                case FactTopic.Health:
                    {
                        if (fact.Payload is not HealthNarrative hn)
                            return new Dictionary<string, string>();

                        // 简单区分：Intro → 描述伤情；Elaboration → 描述处理情况
                        return template.function == DiscourseFunction.Elaboration
                            ? SlotFiller.FillHealthTendingSlots(fact.Subject, hn)
                            : SlotFiller.FillHealthSeveritySlots(fact.Subject, hn);
                    }

                default:
                    return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>
    /// 通用模板选择器：只依赖 Fact.Tags 和 NarrativeTemplateDef 的 topic / requiredTags / tagWeights，
    /// 再结合上层给的 SentenceConstraint 做过滤和打分。
    /// </summary>
    internal static class TemplateSelector
    {
        public static NarrativeTemplateDef PickBestTemplateFor(
            Fact fact,
            SentenceConstraint constraint)
        {
            if (fact == null) throw new ArgumentNullException(nameof(fact));
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));

            var allDefs = DefDatabase<NarrativeTemplateDef>.AllDefsListForReading;

            NarrativeTemplateDef best = null;
            float bestScore = float.MinValue;

            foreach (var def in allDefs)
            {
                if (!IsCandidate(def, fact, constraint))
                    continue;

                float score = ScoreTemplate(def, fact, constraint);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                }
            }

            return best;
        }

        private static bool IsCandidate(
            NarrativeTemplateDef def,
            Fact fact,
            SentenceConstraint c)
        {
            if (def == null) return false;

            // 如果模板声明了 topic，则要求与 Fact.Topic 名称匹配（忽略大小写）
            if (!string.IsNullOrEmpty(def.topic))
            {
                var topicName = fact.Topic.ToString();
                if (!string.Equals(def.topic, topicName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 语篇功能必须匹配
            if (def.function != c.Function)
                return false;

            // 模板自己的 requiredTags：Fact 必须包含
            if (def.requiredTags != null && def.requiredTags.Count > 0)
            {
                foreach (var tag in def.requiredTags)
                {
                    if (string.IsNullOrEmpty(tag))
                        continue;
                    if (!fact.Tags.ContainsKey(tag))
                        return false;
                }
            }

            // 约束中的 RequiredFactTags：再加一层安全限制（虽然在 SelectBestFact 已经过滤过）
            if (c.RequiredFactTags.Count > 0)
            {
                foreach (var tag in c.RequiredFactTags)
                {
                    if (string.IsNullOrEmpty(tag))
                        continue;
                    if (!fact.Tags.ContainsKey(tag))
                        return false;
                }
            }

            // 约束中的 RequiredTemplateTags：要求模板在 tagWeights 中显式声明这些标签
            if (c.RequiredTemplateTags.Count > 0)
            {
                if (def.tagWeights == null || def.tagWeights.Count == 0)
                    return false;

                foreach (var tag in c.RequiredTemplateTags)
                {
                    if (string.IsNullOrEmpty(tag))
                        continue;
                    if (!def.tagWeights.TryGetValue(tag, out var w) || w <= 0f)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 模板打分：
        /// - 基础：Fact.Tags 与模板 tagWeights 的点积；
        /// - 加成：对约束中 PreferredFactTags / PreferredTemplateTags 的匹配；
        /// - 再乘上模板的 baseWeight，最后加一点 Fact.Salience 打破平局。
        /// </summary>
        private static float ScoreTemplate(
            NarrativeTemplateDef def,
            Fact fact,
            SentenceConstraint c)
        {
            float score = 0f;

            // 1) Fact 标签与模板标签的相似度
            if (def.tagWeights != null && def.tagWeights.Count > 0)
            {
                foreach (var kv in def.tagWeights)
                {
                    if (string.IsNullOrEmpty(kv.Key))
                        continue;

                    if (!fact.Tags.TryGetValue(kv.Key, out var wFact))
                        continue;

                    score += wFact * kv.Value;
                }
            }

            // 2) 剧本偏好的 Fact 标签（可能不是模板声明的标签）
            foreach (var kv in c.PreferredFactTags)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (!fact.Tags.TryGetValue(kv.Key, out var wFact)) continue;

                score += wFact * kv.Value;
            }

            // 3) 剧本偏好的模板标签
            if (def.tagWeights != null && def.tagWeights.Count > 0)
            {
                foreach (var kv in c.PreferredTemplateTags)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (!def.tagWeights.TryGetValue(kv.Key, out var wTemplate)) continue;

                    score += wTemplate * kv.Value;
                }
            }

            // 4) 模板自身权重
            score *= Math.Max(0.01f, def.baseWeight);

            // 5) 略微加入 Fact 显著度，打破平局
            score += fact.Salience * 0.01f;

            return score;
        }
    }

    /// <summary>
    /// 把 SentencePlan + NarrativeTemplateDef 转成最终字符串。
    /// </summary>
    public static class SurfaceRealizer
    {
        public static string Realize(SentencePlan plan)
        {
            if (plan == null || plan.Template == null)
                return string.Empty;

            var templateText = plan.Template.GetTemplate();
            if (string.IsNullOrEmpty(templateText))
                return string.Empty;

            return TemplateEngine.Fill(templateText, plan.Slots);
        }
    }

    /// <summary>
    /// 一个极简模板引擎：用 {SLOT_NAME} 占位符做替换。
    /// </summary>
    public static class TemplateEngine
    {
        private static readonly Regex SlotRegex =
            new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        public static string Fill(string template, IReadOnlyDictionary<string, string> slots)
        {
            if (string.IsNullOrEmpty(template) || slots == null || slots.Count == 0)
                return template;

            return SlotRegex.Replace(template, match =>
            {
                var key = match.Groups[1].Value;
                if (slots.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                    return value;

                // 没有值就保留原样，方便调试。
                return match.Value;
            });
        }
    }

    /// <summary>
    /// 槽位填充逻辑：把 HealthNarrative 等 payload 映射到模板中的具体插槽。
    /// 目前只实现了 Health 相关两个分支。
    /// </summary>
    public static class SlotFiller
    {
        public static Dictionary<string, string> FillHealthSeveritySlots(PawnPro speaker, HealthNarrative narrative)
        {
            if (speaker == null) throw new ArgumentNullException(nameof(speaker));
            if (narrative == null) throw new ArgumentNullException(nameof(narrative));

            var dict = new Dictionary<string, string>();

            var label = !string.IsNullOrWhiteSpace(speaker.FullName)
                ? speaker.FullName
                : speaker.Name;

            if (string.IsNullOrWhiteSpace(label))
                label = "Unknown";

            dict["SUBJECT_Label"] = label;
            dict["SUBJECT_Possessive"] = label + "'s";

            dict["NOUN_Injury"] = narrative.Noun ?? string.Empty;

            if (narrative.RelatedNouns != null &&
                narrative.RelatedNouns.TryGetValue("OnPart", out var part) &&
                !string.IsNullOrWhiteSpace(part))
            {
                dict["NOUN_Part"] = part;
            }

            // 目前直接使用枚举名，你之后可以改成调用翻译表或自定义映射。
            dict["ADJ_Severity"] = narrative.Severity.ToString();

            return dict;
        }

        public static Dictionary<string, string> FillHealthTendingSlots(PawnPro speaker, HealthNarrative narrative)
        {
            if (speaker == null) throw new ArgumentNullException(nameof(speaker));
            if (narrative == null) throw new ArgumentNullException(nameof(narrative));

            var dict = new Dictionary<string, string>();

            var label = !string.IsNullOrWhiteSpace(speaker.FullName)
                ? speaker.FullName
                : speaker.Name;

            if (string.IsNullOrWhiteSpace(label))
                label = "Unknown";

            dict["SUBJECT_Label"] = label;
            dict["SUBJECT_Possessive"] = label + "'s";
            dict["ADJ_Tending"] = narrative.Tending.ToString();

            return dict;
        }
    }
}
