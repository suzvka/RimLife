using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 可以谈论的话题。后续可以继续扩展（心情、环境、人物关系等）。
    /// </summary>
    public enum FactTopic
    {
        Health
        // Mood,
        // Environment,
        // Relationship,
        // Activity
    }

    /// <summary>
    /// 一个“可说的事实”。还没有确定具体句型，只描述“说什么”。
    /// </summary>
    public sealed class Fact
    {
        public FactTopic Topic { get; }
        public string Subtopic { get; }
        public float Salience { get; }
        public PawnPro Subject { get; }
        public PawnPro Target { get; }
        public object Payload { get; }

        /// <summary>
        /// 该事实的标签向量。key: 标签，value: 权重(0~1 或任意范围)。
        /// </summary>
        public Dictionary<string, float> Tags { get; }

        public Fact(
            FactTopic topic,
            string subtopic,
            float salience,
            PawnPro subject,
            PawnPro target,
            object payload,
            Dictionary<string, float> tags = null)
        {
            Topic = topic;
            Subtopic = subtopic ?? string.Empty;
            Salience = salience;
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Target = target;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Tags = tags ?? new Dictionary<string, float>();
        }
    }



    /// <summary>
    /// 句子计划：选好了“说哪件事，用哪个模板，大概要填哪些槽”，但还没生成具体文本。
    /// </summary>
    public sealed class SentencePlan
    {
        public Fact SourceFact { get; }
        public NarrativeTemplateDef Template { get; }
        public Dictionary<string, string> Slots { get; }
        public DiscourseFunction DiscourseFunction => Template.function;
        public SyntacticType SyntacticType => Template.syntacticType;
        public int Priority { get; }

        public SentencePlan(Fact sourceFact, NarrativeTemplateDef template, Dictionary<string, string> slots, int priority)
        {
            SourceFact = sourceFact ?? throw new ArgumentNullException(nameof(sourceFact));
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Slots = slots ?? new Dictionary<string, string>();
            Priority = priority;
        }
    }

}
