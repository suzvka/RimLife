using System;
using Verse;

namespace RimLife
{

    public sealed class SlotRealizer : ISlotRealizer
    {
        public string RealizeSlot(Word slot, Fact fact)
        {
            switch (slot.Type)
            {
                case WordType.SubjectNp:
                    return RealizeSubject(slot, fact);

                case WordType.DomainNp:
                    return RealizeDomain(slot, fact);

                case WordType.AdjIntensity:
                    return RealizeAdjIntensity(slot, fact);

                case WordType.AdjQuality:
                    return RealizeAdjQuality(slot, fact);

                case WordType.AdvDegree:
                    return RealizeAdvDegree(slot, fact);

                case WordType.StateVerb:
                    return RealizeStateVerb(slot, fact);

                case WordType.RiskClause:
                    return RealizeRiskClause(slot, fact);

                case WordType.TimeAdverb:
                    return RealizeTimeAdverb(slot, fact);

                case WordType.Connective:
                    return RealizeConnective(slot, fact);

                default:
                    return string.Empty;
            }
        }

        #region 基础 helper

        private static string TranslateOrEmpty(string key)
        {
            return Tool.TryTranslate(key, out var localized)
                ? localized
                : string.Empty;
        }

        private static string TranslateOrDefault(string key, string fallback)
        {
            return Tool.TryTranslate(key, out var localized)
                ? localized
                : fallback;
        }

        private enum PolarityKind
        {
            Negative,
            Positive,
            Neutral
        }

        private enum IntensityLevel
        {
            Low,
            Mid,
            High
        }

        private static PolarityKind GetPolarity(Fact fact)
        {
            var tags = fact?.Tags;
            if (tags == null)
                return PolarityKind.Neutral;

            // 按照约定：TagId 可以用字符串构造
            if (tags.Contains(new TagId("polarity.negative")))
                return PolarityKind.Negative;

            if (tags.Contains(new TagId("polarity.positive")))
                return PolarityKind.Positive;

            return PolarityKind.Neutral;
        }

        private static IntensityLevel GetIntensityLevel(Fact fact, NarrativeAxisId axisId)
        {
            var value = fact.NarrativeAxis.Axes[axisId];

            if (value < 0.33f)
                return IntensityLevel.Low;
            if (value < 0.66f)
                return IntensityLevel.Mid;
            return IntensityLevel.High;
        }

        #endregion

        #region 已有实现：Subject / Domain

        private string RealizeSubject(Word slot, Fact fact)
        {
            var pawn = fact?.Subject;
            if (pawn == null)
                return string.Empty;

            return !string.IsNullOrWhiteSpace(pawn.Name)
                ? pawn.Name
                : pawn.FullName ?? string.Empty;
        }

        private string RealizeDomain(Word slot, Fact fact)
        {
            if (fact != null &&
                !string.IsNullOrWhiteSpace(slot.Name) &&
                fact.TryGetDomainLexeme(slot.Name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            // 不再做任何领域特例，全部由 Fact 构建时决定
            return string.Empty;
        }

        #endregion

        #region AdjIntensity：强度形容词

        private string RealizeAdjIntensity(Word slot, Fact fact)
        {
            if (fact == null)
                return string.Empty;

            var polarity = GetPolarity(fact);
            var level = GetIntensityLevel(fact, NarrativeAxisId.Intensity);

            // 映射到 Lexicon key 片段
            string polarityPart = polarity switch
            {
                PolarityKind.Negative => "Negative",
                PolarityKind.Positive => "Positive",
                _ => "Neutral"
            };

            string levelPart = level switch
            {
                IntensityLevel.Low => "Low",
                IntensityLevel.Mid => "Mid",
                IntensityLevel.High => "High",
                _ => "Mid"
            };

            // 示例 key：
            // RimLife.Lexicon.AdjIntensity.Negative.High
            string key = $"RimLife.Lexicon.AdjIntensity.{polarityPart}.{levelPart}";
            return TranslateOrEmpty(key);
        }

        #endregion

        #region AdvDegree：程度副词（稍微/很/非常）

        private string RealizeAdvDegree(Word slot, Fact fact)
        {
            if (fact == null)
                return string.Empty;

            var polarity = GetPolarity(fact);
            var level = GetIntensityLevel(fact, NarrativeAxisId.Intensity);

            string polarityPart = polarity switch
            {
                PolarityKind.Negative => "Negative",
                PolarityKind.Positive => "Positive",
                _ => "Neutral"
            };

            string levelPart = level switch
            {
                IntensityLevel.Low => "Low",
                IntensityLevel.Mid => "Mid",
                IntensityLevel.High => "High",
                _ => "Mid"
            };

            // 示例 key：
            // RimLife.Lexicon.AdvDegree.Negative.High
            string key = $"RimLife.Lexicon.AdvDegree.{polarityPart}.{levelPart}";
            return TranslateOrEmpty(key);
        }

        #endregion

        #region AdjQuality：好/坏/一般 的评价

        private string RealizeAdjQuality(Word slot, Fact fact)
        {
            if (fact == null)
                return string.Empty;

            var polarity = GetPolarity(fact);
            var intensityLevel = GetIntensityLevel(fact, NarrativeAxisId.Intensity);

            string polarityPart = polarity switch
            {
                PolarityKind.Negative => "Negative",
                PolarityKind.Positive => "Positive",
                _ => "Neutral"
            };

            // 简单把强度二分为 Weak/Strong
            string strengthPart = intensityLevel switch
            {
                IntensityLevel.Low => "Weak",
                IntensityLevel.Mid => "Weak",
                IntensityLevel.High => "Strong",
                _ => "Weak"
            };

            // 示例 key：
            // RimLife.Lexicon.AdjQuality.Negative.Strong
            string key = $"RimLife.Lexicon.AdjQuality.{polarityPart}.{strengthPart}";
            return TranslateOrEmpty(key);
        }

        #endregion

        #region RiskClause：风险从句

        private string RealizeRiskClause(Word slot, Fact fact)
        {
            if (fact == null)
                return string.Empty;

            var risk = fact.NarrativeAxis.Axes[NarrativeAxisId.Risk];

            // 非常低的风险就不说，以免啰嗦
            if (risk <= 0.1f)
                return string.Empty;

            string bucket;
            if (risk < 0.4f)
                bucket = "Low";
            else if (risk < 0.75f)
                bucket = "Mid";
            else
                bucket = "High";

            // 示例 key：
            // RimLife.Lexicon.RiskClause.Mid
            var key = $"RimLife.Lexicon.RiskClause.{bucket}";
            return TranslateOrEmpty(key);
        }

        #endregion

        #region TimeAdverb：时间副词

        private string RealizeTimeAdverb(Word slot, Fact fact)
        {
            // 这里有两种信息来源：
            // 1) Fact 的标签（例如 temporal.now / temporal.recent）
            // 2) slot.Name（例如 "TIME_Now" / "TIME_Recent"）
            // 你可以两者都支持，优先看 slot.Name。

            // 先看 slot.Name 是否指定了更细粒度意图
            var name = slot.Name ?? string.Empty;

            if (name.Equals("TIME_Now", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.Now");

            if (name.Equals("TIME_Recent", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.Recently");

            if (name.Equals("TIME_Often", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.Often");

            if (name.Equals("TIME_Sometimes", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.Sometimes");

            if (name.Equals("TIME_Still", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.Still");

            if (name.Equals("TIME_NoLonger", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Time.NoLonger");

            // 然后根据标签做一个非常粗略的 fallback
            var tags = fact?.Tags;
            if (tags != null)
            {
                if (tags.Contains(new TagId("temporal.now")))
                    return TranslateOrEmpty("RimLife.Lexicon.Time.Now");

                if (tags.Contains(new TagId("temporal.recent")))
                    return TranslateOrEmpty("RimLife.Lexicon.Time.Recently");

                if (tags.Contains(new TagId("temporal.habitual")))
                    return TranslateOrEmpty("RimLife.Lexicon.Time.Often");
            }

            // 没有时间信号，就不要硬塞
            return string.Empty;
        }

        #endregion

        #region Connective：连接词（而且/但是/所以/因为）

        private string RealizeConnective(Word slot, Fact fact)
        {
            // 同样优先看 slot.Name 指定的连接类型
            var name = slot.Name ?? string.Empty;

            if (name.Equals("CONN_Additive", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Connective.And");

            if (name.Equals("CONN_Contrast", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Connective.But");

            if (name.Equals("CONN_Cause", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Connective.Because");

            if (name.Equals("CONN_Result", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Connective.So");

            if (name.Equals("CONN_Summary", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.Connective.Overall");

            // 如果模板没指明，就用一个最安全的并列
            return TranslateOrEmpty("RimLife.Lexicon.Connective.And");
        }

        #endregion

        #region StateVerb：状态动词（是/有/看起来）

        private string RealizeStateVerb(Word slot, Fact fact)
        {
            // 最简单的策略：
            // - 如果 DomainNp 像“伤口/情绪/问题”这一类，一般用“有”
            // - 有时你也可以在 slot.Name 里指定特殊形式

            var name = slot.Name ?? string.Empty;

            if (name.Equals("STATE_Is", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.StateVerb.Is");

            if (name.Equals("STATE_Has", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.StateVerb.Has");

            if (name.Equals("STATE_Seems", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.StateVerb.Seems");

            if (name.Equals("STATE_Becomes", StringComparison.OrdinalIgnoreCase))
                return TranslateOrEmpty("RimLife.Lexicon.StateVerb.Becomes");

            // 默认：描述某人“有”一个状态/问题（很适合 Health/Mood）
            return TranslateOrEmpty("RimLife.Lexicon.StateVerb.Has");
        }

        #endregion
    }
}
