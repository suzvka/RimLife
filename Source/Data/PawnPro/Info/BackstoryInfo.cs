using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 表示Pawn背景故事信息的快照。
    /// </summary>
    /// <remarks>此数据为快照，不保证时序一致性。</remarks>
    public class BackstoryInfo
    {
        /// <summary>
        /// 童年时期的背景故事（如果可用）。
        /// </summary>
        public BackstoryEntry? Childhood { get; }

        /// <summary>
        /// 成人时期的背景故事（如果可用）。
        /// </summary>
        public BackstoryEntry? Adulthood { get; }

        private BackstoryInfo(BackstoryEntry? childhood, BackstoryEntry? adulthood)
        {
            Childhood = childhood;
            Adulthood = adulthood;
        }

        /// <summary>
        /// 从Pawn创建BackstoryInfo快照。必须在主线程中调用。
        /// 会就地展开描述中的 RimWorld 占位符（[PAWN_nameDef] → 角色名，[PAWN_pronoun] → 他/她 等），
        /// 避免将无意义的元文本送入 LLM。
        /// </summary>
        public static BackstoryInfo CreateFrom(Pawn p)
        {
            if (p?.story == null) return new BackstoryInfo(null, null);

            string pawnName = p.Name?.ToStringShort ?? p.LabelShort ?? "角色";
            string pronoun = GetPronoun(p);
            string possessive = GetPossessive(p);

            BackstoryEntry? childhood = p.story.Childhood != null ? new BackstoryEntry
            {
                Title = p.story.Childhood.title,
                Description = ResolvePlaceholders(p.story.Childhood.description, pawnName, pronoun, possessive)
            } : null;

            BackstoryEntry? adulthood = p.story.Adulthood != null ? new BackstoryEntry
            {
                Title = p.story.Adulthood.title,
                Description = ResolvePlaceholders(p.story.Adulthood.description, pawnName, pronoun, possessive)
            } : null;

            return new BackstoryInfo(childhood, adulthood);
        }

        /// <summary>
        /// 展开 RimWorld 背景故事描述中的标准占位符。
        /// </summary>
        private static string ResolvePlaceholders(string description, string name, string pronoun, string possessive)
        {
            if (string.IsNullOrEmpty(description)) return description;

            return description
                .Replace("[PAWN_nameDef]", name)
                .Replace("[PAWN_pronoun]", pronoun)
                .Replace("[PAWN_possessive]", possessive)
                .Replace("[PAWN_objective]", pronoun); // RimWorld 使用 [PAWN_objective] 作为宾格代词
        }

        private static string GetPronoun(Pawn p)
        {
            if (p.gender == Gender.Female) return "她";
            return "他";
        }

        private static string GetPossessive(Pawn p)
        {
            if (p.gender == Gender.Female) return "她的";
            return "他的";
        }

        public string ToPrompt()
        {
            if (Childhood == null && Adulthood == null) return null;

            var parts = new List<string>();
            if (Childhood.HasValue)
                parts.Add($"童年: {Childhood.Value.Title}——{Childhood.Value.Description}");
            if (Adulthood.HasValue)
                parts.Add($"成年: {Adulthood.Value.Title}——{Adulthood.Value.Description}");

            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }
    }

    public struct BackstoryEntry
    {
        /// <summary>
        /// 背景故事的标题。
        /// </summary>
        public string Title;
        /// <summary>
        /// 背景故事的描述。
        /// </summary>
        public string Description;
    }
}
