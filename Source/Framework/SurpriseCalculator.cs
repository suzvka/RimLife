using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimLife.Core;

namespace RimLife.Framework
{
    /// <summary>
    /// 惊讶度计算器。供 Agent 侧使用，判断某个词条是否需要触发学习行为。
    /// 惊讶度高（>0.3）表示知识库对该词了解不足，应触发知识获取。
    /// 纯静态，零 RimWorld 依赖。
    /// </summary>
    public static class SurpriseCalculator
    {
        /// <summary>
        /// 默认惊讶度阈值。惊讶度超过此值的词条标记为"陌生"，建议触发学习。
        /// </summary>
        public const float DefaultSurpriseThreshold = 0.3f;

        /// <summary>
        /// 默认熟知阈值。Confidence 达到此值的词条视为已掌握。
        /// </summary>
        public const float DefaultFamiliarConfidence = 0.7f;

        // 匹配疑似专有名词的模式
        // 模式 1: 大驼峰/下划线命名 (RimWorld Def 风格): "MentalBreak", "RaidStrategy", "Toxic_Buildup"
        private static readonly Regex ProperNounPattern = new Regex(
            @"\b([A-Z][a-z]+(?:[A-Z][a-z]+)+)\b|" +           // PascalCase: MentalBreak
            @"\b([A-Z][a-z]+(?:_[A-Z][a-z]+)+)\b|" +          // Snake_Case: Toxic_Buildup  
            @"「([^」]+)」|" +                                   // 中文书名号: 「心灵冲击」
            @"""([^""]+)""|" +                                  // 双引号包裹
            @"[\u4e00-\u9fff]{2,8}(?:冲击|事件|感染|袭击|风暴|浪潮|瘟疫|灾难|仪式|典礼|挑战)", // 中文 RimWorld 专有名词模式
            RegexOptions.Compiled);

        /// <summary>
        /// 计算单个词条的惊讶度。
        /// </summary>
        /// <param name="term">词条名。</param>
        /// <param name="knowledgeBase">知识库实例。</param>
        /// <returns>惊讶度 (0.0~1.0)。0.0 = 完全熟悉，1.0 = 完全陌生。</returns>
        public static float CalculateSurprise(string term, IKnowledgeBase knowledgeBase)
        {
            if (string.IsNullOrEmpty(term)) return 0f;
            if (knowledgeBase == null) return 1f;

            if (knowledgeBase.TryLookup(term, out var entry))
            {
                if (entry.Confidence >= DefaultFamiliarConfidence)
                    return 0f;

                return 1f - entry.Confidence;
            }

            return 1f;
        }

        /// <summary>
        /// 从文本中提取未知词条列表。自动识别疑似专有名词，
        /// 过滤已熟知词条，返回惊讶度超过阈值的词条。
        /// </summary>
        /// <param name="text">待分析的文本。</param>
        /// <param name="knowledgeBase">知识库实例。</param>
        /// <param name="surpriseThreshold">惊讶度阈值，默认 0.3。</param>
        /// <returns>惊讶度 > 阈值的词条列表，按惊讶度降序排列。</returns>
        public static IReadOnlyList<SurpriseResult> ExtractUnknownTerms(
            string text,
            IKnowledgeBase knowledgeBase,
            float surpriseThreshold = DefaultSurpriseThreshold)
        {
            var results = new List<SurpriseResult>();
            if (string.IsNullOrEmpty(text)) return results;

            // 提取疑似专有名词
            var candidates = ExtractCandidateTerms(text);

            // 去重
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var term in candidates)
            {
                if (!seen.Add(term)) continue;

                float surprise = CalculateSurprise(term, knowledgeBase);
                if (surprise >= surpriseThreshold)
                {
                    results.Add(new SurpriseResult
                    {
                        Term = term,
                        Surprise = surprise
                    });
                }
            }

            return results.OrderByDescending(r => r.Surprise).ToList();
        }

        /// <summary>
        /// 批量计算多个词条的惊讶度。
        /// </summary>
        /// <param name="terms">词条列表。</param>
        /// <param name="knowledgeBase">知识库实例。</param>
        /// <returns>每个词条的惊讶度结果。</returns>
        public static IReadOnlyList<SurpriseResult> CalculateBatch(
            IEnumerable<string> terms,
            IKnowledgeBase knowledgeBase)
        {
            var results = new List<SurpriseResult>();
            if (terms == null) return results;

            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;

                results.Add(new SurpriseResult
                {
                    Term = term,
                    Surprise = CalculateSurprise(term, knowledgeBase)
                });
            }

            return results.OrderByDescending(r => r.Surprise).ToList();
        }

        // ================================================================
        // 内部：候选词提取
        // ================================================================

        private static IReadOnlyList<string> ExtractCandidateTerms(string text)
        {
            var terms = new List<string>();
            if (string.IsNullOrEmpty(text)) return terms;

            var matches = ProperNounPattern.Matches(text);
            foreach (Match match in matches)
            {
                // 取第一个非空捕获组
                for (int g = 1; g < match.Groups.Count; g++)
                {
                    if (match.Groups[g].Success)
                    {
                        string term = match.Groups[g].Value.Trim();
                        if (term.Length >= 2 && !IsStopWord(term))
                            terms.Add(term);
                        break;
                    }
                }
            }

            // 额外：按逗号/空格分词，捕获首字母大写的单词
            var words = text.Split(new char[] { ' ', ',', '，', '、', '\n', '\r', '\t', '。', '！', '？' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                string trimmed = word.Trim();
                if (trimmed.Length >= 3 && char.IsUpper(trimmed[0]) && !terms.Contains(trimmed))
                {
                    if (!IsStopWord(trimmed))
                        terms.Add(trimmed);
                }
            }

            return terms;
        }

        /// <summary>
        /// 判断是否为停用词（常见英文虚词/代词，不应作为专有名词）。
        /// </summary>
        private static bool IsStopWord(string word)
        {
            // 常见英文停用词
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "The", "And", "For", "Not", "But", "Was", "Has", "Had", "Are",
                "This", "That", "With", "From", "Have", "Been", "Will", "Would",
                "They", "Them", "Then", "Than", "When", "What", "Where", "Which",
                "There", "Their", "About", "After", "Before", "Could", "Should",
                "Every", "Other", "Some", "Many", "Much", "Very", "Just", "Only",
                "Also", "Even", "Still", "Already", "Always", "Never"
            };

            return stopWords.Contains(word) || word.Length < 2;
        }
    }

    /// <summary>
    /// 惊讶度计算结果。纯 DTO，零依赖。
    /// </summary>
    public struct SurpriseResult
    {
        /// <summary>词条名。</summary>
        public string Term;

        /// <summary>惊讶度 (0.0~1.0)。0.0 = 完全熟悉，1.0 = 完全陌生。</summary>
        public float Surprise;

        /// <summary>惊讶度是否超过触发学习阈值（默认 0.3）。</summary>
        public bool ShouldLearn => Surprise >= SurpriseCalculator.DefaultSurpriseThreshold;

        public override string ToString()
        {
            return $"{Term} (surprise={Surprise:F2}, learn={ShouldLearn})";
        }
    }
}
