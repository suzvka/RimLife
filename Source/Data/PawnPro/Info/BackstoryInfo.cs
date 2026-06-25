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
        /// </summary>
        public static BackstoryInfo CreateFrom(Pawn p)
        {
            if (p?.story == null) return new BackstoryInfo(null, null);

            BackstoryEntry? childhood = p.story.Childhood != null ? new BackstoryEntry
            {
                Title = p.story.Childhood.title,
                Description = p.story.Childhood.description
            } : null;

            BackstoryEntry? adulthood = p.story.Adulthood != null ? new BackstoryEntry
            {
                Title = p.story.Adulthood.title,
                Description = p.story.Adulthood.description
            } : null;

            return new BackstoryInfo(childhood, adulthood);
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
