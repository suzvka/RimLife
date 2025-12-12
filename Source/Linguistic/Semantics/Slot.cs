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
    public enum SlotType
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

    /// <summary>
    /// 模板中的槽位请求。
    /// 
    /// 用途：
    /// - TemplateEngine 在解析模板时，把 {SLOT_NAME} 解析为 SlotRequest，
    ///   其中包含 SlotType 和原始名称（方便调试）。
    /// </summary>
    public readonly struct SlotRequest
    {
        public string Name { get; }
        public SlotType Type { get; }

        public SlotRequest(string name, SlotType type)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
        }

        private static readonly Dictionary<string, SlotType> AliasToSlotType =
            new Dictionary<string, SlotType>(StringComparer.OrdinalIgnoreCase)
            {
                { "SUBJECT_Label", SlotType.SubjectNp },
                { "SUBJECT_Name", SlotType.SubjectNp },
                { "SUBJECT_Primary", SlotType.SubjectNp },
                { "NOUN_Injury", SlotType.DomainNp },
                { "NOUN_Part", SlotType.DomainNp },
                { "ADJ_Severity", SlotType.AdjIntensity },
                { "ADJ_Tending", SlotType.AdjQuality },
            };

        /// <summary>
        /// 通过槽位名解析 SlotType。
        /// 约定：模板写 {AdjIntensity}，则 name = "AdjIntensity"，
        ///       可以使用 Enum.Parse 转成 SlotType。
        /// 如果未来想支持别名，可以在这里加映射表。
        /// </summary>
        public static bool TryParse(string name, out SlotRequest request)
        {
            request = default;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (Enum.TryParse(name, ignoreCase: true, out SlotType type))
            {
                request = new SlotRequest(name, type);
                return true;
            }

            if (AliasToSlotType.TryGetValue(name, out type))
            {
                request = new SlotRequest(name, type);
                return true;
            }

            return false;
        }
    }
}
