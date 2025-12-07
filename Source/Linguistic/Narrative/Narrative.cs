using System;
using System.Collections.Generic;
using System.IO.Ports;
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

        // 获取当前语言模板的便捷方法
        public string GetTemplate()
        {
            // 1. 尝试获取当前激活语言
            string curLang = LanguageDatabase.activeLanguage.folderName.ToLower();
            if (templates.TryGetValue(curLang, out string text) && !string.IsNullOrWhiteSpace(text))
                return text;

            // 2. 返回第一个非空模板（按语言代码排序）
            foreach (var kvp in templates.OrderBy(k => k.Key))
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                    return kvp.Value;
            }

            // 3. 都没有，返回 DefName 以便调试
            return $"[{defName}_NoText]";
        }
    }
}