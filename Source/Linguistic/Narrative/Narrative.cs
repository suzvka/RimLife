using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    public enum DiscourseFunction // 话语功能
    {
        Intro,          // 引入
        Elaboration,    // 阐述
        Contrast,       // 对比
        Result          // 结果
    }

    public enum Polarity // 极性
    {
        Positive,       // 积极
        Negative,       // 消极
        Neutral         // 中性
    }

    public enum SyntacticType // 句法类型
    {
        FullSentence,   // 完整句子
        Fragment,       // 片段
        Modifier        // 修饰语
    }

    public class NarrativeAtom
    {
        // 内容 (Key to XML)
        public string TemplateKey;
        public Dictionary<string, string> Params;

        // --- 语言学参数 ---

        // 1. 功能: Intro, Elaboration, Contrast, Result
        public DiscourseFunction Function;

        // 2. 极性: Positive, Negative, Neutral
        public Polarity Polarity;

        // 3. 权重: 0.0 - 1.0
        public float Weight;

        // 4. 句法类型: FullSentence, Fragment, Modifier
        public SyntacticType Type;

        // 5. 话题归属 (可自定义的字符串)
        public string Topic;
    }

    public class NarrativeTemplateDef : Def
    {
        // === 1. 元数据 (Metadata) ===
        // 对应之前的 NarrativeAtom 属性，用于 Builder 筛选
        public DiscourseFunction function = DiscourseFunction.Elaboration;
        public SyntacticType syntacticType = SyntacticType.FullSentence;
        public float baseWeight = 0.5f; // 基础权重
        public string topic; // 话题归属

        // === 2. 文本模板 (Templates) ===
        // 这里是核心：在一个 Def 里包含多种语言
        // Key: 语言代码 (e.g., "en", "zh-cn"), Value: 模板字符串
        public Dictionary<string, string> templates = new Dictionary<string, string>();

        // === 3. 可选：极性逻辑 (Polarity Logic) ===
        // 有些句子天生是负面的，有些取决于填入的词
        // 简单起见，这里可以定死，或者留空由代码动态判断
        public Polarity fixedPolarity = Polarity.Neutral;


        /// <summary>
        /// 硬约束：Fact 必须包含这些标签，否则不能使用该模板。
        /// </summary>
        public List<string> requiredTags = new List<string>();

        /// <summary>
        /// 软偏好：模板在各个标签维度上的“纯粹性/关联度”，用于打分。
        /// key: 标签字符串，例如 "health.injury" / "mood.negative"
        /// value: 0~1 或任意你喜欢的范围
        /// </summary>
        public Dictionary<string, float> tagWeights = new Dictionary<string, float>();

        public string GetTemplate()
        {
            if (templates == null || templates.Count == 0)
                return $"[{defName}_NoText]";

            var language = LanguageDatabase.activeLanguage;
            // Use legacyFolderName for a clean, predictable key (e.g., "ChineseSimplified")
            var langKey = language?.LegacyFolderName;

            if (!string.IsNullOrWhiteSpace(langKey))
            {
                // 1. Try to get the template for the current language
                if (TryGetTemplateValue(langKey, out var localized))
                    return localized;

                // 2. If not found, fall back to English
                if (TryGetTemplateValue("English", out var english))
                    return english;
            }

            // 3. If all else fails, return the first available template
            foreach (var kvp in templates)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                    return kvp.Value;
            }

            return $"[{defName}_NoText]";
        }

        private bool TryGetTemplateValue(string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key) || templates == null)
                return false;

            // First, try a direct, case-sensitive match (most efficient)
            if (templates.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct))
            {
                value = direct;
                return true;
            }

            // If that fails, try a case-insensitive match
            foreach (var kvp in templates)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }
    }
}