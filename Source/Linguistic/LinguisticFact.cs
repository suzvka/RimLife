using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 事实话题类型。
    /// 尽量保持少量稳定的 Topic，避免后续爆炸。
    /// </summary>
    public enum FactTopic
    {
        Health,
        Mood,
        Environment,
        Relationship,
        Activity
    }

    /// <summary>
    /// 一个“可以被谈论的事实”。
    /// 例如：
    /// - 某个殖民者身上有一处严重伤口
    /// - 某人心情很差
    /// - 某个房间很豪华
    /// 
    /// 特例（健康/心情/环境等）由各自的 TagBuilder 负责生成 Fact。
    /// </summary>
    public sealed class Fact
    {
        public FactTopic Topic { get; }
        public string Subtopic { get; }   // 可选：更细的子话题，例如 "Injury" / "MoodGeneral"
        public float Salience { get; }    // 显著度，用于筛选/排序
        public PawnPro Subject { get; }   // 说话者/被描述者
        public PawnPro Target { get; }    // 关系目标（如描述别人）
        public TagSet Tags { get; }             // 事实的语义标签集
        public NarrativeAxisMap NarrativeAxis { get; }  // 事实的语义轴强度映射
        public object SourcePayload { get; } // 调试/追踪用的原始 Narrative 或数据结构


        // 新增：Fact 自带的“领域槽位词汇表”
        public IReadOnlyDictionary<string, string> DomainLexemes { get; }

        public Fact(
            FactTopic topic,
            string subtopic,
            float salience,
            PawnPro subject,
            PawnPro target,
            SemanticTemplate semantics,
            IReadOnlyDictionary<string, string> domainLexemes = null,
            object sourcePayload = null)
        {
            Topic = topic;
            Subtopic = subtopic ?? string.Empty;
            Salience = salience;
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Target = target;
            SourcePayload = sourcePayload;

            if (domainLexemes == null || domainLexemes.Count == 0)
            {
                DomainLexemes = EmptyDomainLexemes;
            }
            else
            {
                // 复制一份，确保内部是大小写无关 & 不被外部修改
                DomainLexemes = new Dictionary<string, string>(domainLexemes,
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        // 可以在 Fact 类里加一个静态空字典，避免每次都 new：
        private static readonly IReadOnlyDictionary<string, string> EmptyDomainLexemes =
            new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);

        public bool TryGetDomainLexeme(string slotName, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(slotName))
                return false;

            return DomainLexemes != null && DomainLexemes.TryGetValue(slotName, out value);
        }
    }

    /// <summary>
    /// 句子计划：选定的 Fact + 模板 +（可选）分数。
    /// 不包含具体文本，只是“计划说什么 + 用哪个模板”。
    /// </summary>
    public sealed class SentencePlan
    {
        public Fact SourceFact { get; }
        public NarrativeTemplateDef Template { get; }

        /// <summary>
        /// 模板声明的功能/句法类型直接透传。
        /// </summary>
        public DiscourseFunction DiscourseFunction => Template.function;
        public SyntacticType SyntacticType => Template.syntacticType;

        /// <summary>
        /// 该计划的优先级/分数。
        /// 可以由 LinguisticEngine 设定，用于排序。
        /// </summary>
        public float Score { get; }

        public SentencePlan(Fact sourceFact, NarrativeTemplateDef template, float score)
        {
            SourceFact = sourceFact ?? throw new ArgumentNullException(nameof(sourceFact));
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Score = score;
        }
    }
}
