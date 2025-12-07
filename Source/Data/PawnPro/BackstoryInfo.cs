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
    /// 注意：此数据是快照，其时间一致性无法保证。
    /// </summary>
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
