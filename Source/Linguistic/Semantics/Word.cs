using System;
using System.Collections.Generic;

namespace RimLife
{
    /// <summary>
    /// 全局允许的槽位类型。
    /// 
    /// 约定：
    /// - 模板中的 {槽位名} 必须与这些枚举名一致（或通过简单映射）。
    /// - 各模块（健康/心情/环境）共用这同一套槽位类型。
    /// 
    /// 语义说明：
    /// - SubjectNp    : 主语名词短语（我/他/她/某个殖民者），带人称/名字。
    /// - DomainNp     : 领域对象名词短语（伤口/心情/房间/天气/...），可包含所有格，如“我的伤口”。
    /// - AdjIntensity : 表示强度的形容词（严重的/轻微的/非常好/...）。主要依赖 Intensity + Polarity。
    /// - AdjQuality   : 表示好坏/质量的形容词（糟糕的/不错的/恶心的/...）。主要依赖 Polarity。
    /// - AdvDegree    : 程度副词（有点/很/非常/...）。主要依赖 Intensity。
    /// - StateVerb    : 状态/感受相关的谓语或谓语短语（很疼/很糟/让我很难受/...）。
    /// - RiskClause   : 风险/结果从句（可能会恶化/可能要命/...）。
    /// - TimeAdverb   : 时间相关副词（现在/刚才/一阵子以来/...）。
    /// - Connective   : 连接词/话语标记（不过/而且/但是/...），可以用于多句模板。
    /// </summary>
    public enum WordType
    {
        SubjectNp,
        DomainNp,
        AdjIntensity,
        AdjQuality,
        AdvDegree,
        StateVerb,
        RiskClause,
        TimeAdverb,
        Connective,
    }

    public enum WordErrorCode
    {
        None,
        TypeMismatch,
        UnresolvedSlot,
    }

    public class WordSlot
    {
        private string WordText = string.Empty;

        private static readonly Dictionary<string, WordType> _globalRules = BuildRules();

        private static Dictionary<string, WordType> BuildRules()
        {
            var rules = new Dictionary<string, WordType>(StringComparer.OrdinalIgnoreCase);

            void Map(WordType type, params string[] aliases)
            {
                foreach (var alias in aliases) rules[alias] = type;
            }

            // 原始规则逻辑完全保留在这里
            Map(WordType.SubjectNp, "SUBJECT_Label", "SUBJECT_Name", "SUBJECT_Primary");
            Map(WordType.DomainNp, "NOUN_Injury", "NOUN_Part");
            Map(WordType.AdjIntensity, "ADJ_Severity");

            return rules;
        }

        /// <summary>
        /// 这个插槽期望的类型（解析后的）
        /// </summary>
        public WordType TargetType { get; }

        /// <summary>
        /// 原始的标签名（用于调试或回溯，例如 "SUBJECT_Label"）
        /// </summary>
        public string OriginalTag { get; }

        /// <summary>
        /// 构造函数：初始化插槽时就解析好类型
        /// </summary>
        /// <param name="tagName">输入的标签，如 "NOUN_Injury" 或 "SubjectNp"</param>
        public WordSlot(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

            OriginalTag = tagName;
            TargetType = ResolveType(tagName);
        }

        public WordErrorCode Fill(Word realText)
        {
            if (realText.Type != TargetType)
            {
                return WordErrorCode.TypeMismatch;
            }

            WordText = realText.Text;

            return WordErrorCode.None;
        }

        public WordErrorCode Fill(Fact fact)
        {
            if (fact == null)
                throw new ArgumentNullException(nameof(fact));

        }

        /// <summary>
        /// 解析逻辑：优先查表，其次查 Enum
        /// </summary>
        private static WordType ResolveType(string name, WordErrorCode error = WordErrorCode.None)
        {
            // 1. 优先检查自定义规则 (WordSlot.Rules)
            if (_globalRules.TryGetValue(name, out var typeFromRule))
            {
                return typeFromRule;
            }

            // 2. 检查是否直接匹配 Enum 名称
            if (Enum.TryParse(name, ignoreCase: true, out WordType typeFromEnum))
            {
                return typeFromEnum;
            }

            error = WordErrorCode.UnresolvedSlot;
            return default;
        }
    }

    public readonly struct Word
    {
        public string Text { get; }

        public WordType Type { get; }

        public TagSet Tags { get; }

        public Word(string text, WordType type, TagSet tags)
        {
            Text = text;
            Type = type;
            Tags = tags;
        }

        public override string ToString() => $"{Text} ({Type})";
    }
}
