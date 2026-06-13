using System.Collections.Generic;

namespace RimLife.Cards
{
    /// <summary>
    /// 记忆快照：从 HediffComp_PawnMemory 提取的只读视图。
    /// 用于注入 CharacterCard 和 AI prompt。
    /// 层级：消费者优先读 CurrentMindset（凌驾层），再读 ShortTermReview，
    /// 仅在需要细节时才向下钻取到 RecentMemories / KeyMemories。
    /// 纯 DTO，零 RimWorld 依赖。
    /// </summary>
    public class MemorySnapshot
    {
        /// <summary>即时心境（凌驾层）：第一人称心理描述。</summary>
        public string CurrentMindset;

        /// <summary>短期回顾：LLM 对近期事件的客观摘要。</summary>
        public string ShortTermReview;

        /// <summary>近期短期记忆摘要列表（截断至 120 字）。</summary>
        public List<string> RecentMemories;

        /// <summary>关键长期记忆摘要列表（截断至 300 字）。</summary>
        public List<string> KeyMemories;

        /// <summary>当前短期记忆总数。</summary>
        public int ShortTermCount;

        /// <summary>当前长期记忆总数。</summary>
        public int LongTermCount;

        /// <summary>最后一次巩固的游戏 tick。</summary>
        public int LastConsolidationTick;
    }
}
