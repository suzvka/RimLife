using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 知识来源枚举。区分知识的获取途径，影响信心度评估和合并优先级。
    /// </summary>
    public enum KnowledgeSource
    {
        /// <summary>来自外部 LLM 的推理释义。</summary>
        LLM,

        /// <summary>来自 RimWorld Def 数据库的官方定义。</summary>
        GameDef,

        /// <summary>来自 Agent 自身的推理/演绎。</summary>
        AgentDeduction,

        /// <summary>来自外部 Wiki 或文档。</summary>
        Wiki,

        /// <summary>从旧版缓存迁移，无明确来源。</summary>
        LegacyCache
    }

    /// <summary>
    /// 知识库中的单条知识条目。纯 DTO，零 RimWorld 依赖。
    /// Term 作为主键索引。
    /// </summary>
    public class KnowledgeEntry
    {
        /// <summary>词条名（索引键，大小写不敏感）。</summary>
        public string Term;

        /// <summary>释义文本。可为空（表示仅标记未知但未学习）。</summary>
        public string Definition;

        /// <summary>知识来源。</summary>
        public KnowledgeSource Source;

        /// <summary>信心度 (0.0~1.0)。GameDef 来源天然为 1.0，LLM 来源通常 0.6~0.9。</summary>
        public float Confidence;

        /// <summary>关联的语义标签（如 Combat、Faction、Lore）。用于按领域过滤。</summary>
        public List<string> ContextTags;

        /// <summary>创建时的 Agent 访问序列号。纯事件驱动，不依赖游戏 tick 或时钟。</summary>
        public long CreatedSeq;

        /// <summary>上次被命中时的 Agent 访问序列号。</summary>
        public long LastAccessedSeq;

        /// <summary>累计被访问次数（用于热度/淘汰策略）。</summary>
        public int AccessCount;

        /// <summary>
        /// 记录一次访问：更新 LastAccessedSeq 并递增 AccessCount。
        /// </summary>
        /// <param name="currentSeq">当前全局访问序列号。</param>
        public void RecordAccess(long currentSeq)
        {
            LastAccessedSeq = currentSeq;
            AccessCount++;
        }

        /// <summary>
        /// 计算该条目的"热度"分数，用于 LRU 淘汰。
        /// 纯事件驱动：基于 Agent 访问次数与序列间隔，不依赖任何定时器。
        /// </summary>
        /// <param name="currentSeq">当前全局访问序列号。</param>
        /// <returns>热度分（越高越热）。</returns>
        public float GetHeatScore(long currentSeq)
        {
            long seqAge = currentSeq - LastAccessedSeq;
            if (seqAge < 0) seqAge = 0;

            // 序列间隔越小 = Agent 最近刚访问过，热度加成越高
            float recency = seqAge < 10 ? 2.0f : (seqAge < 50 ? 1.5f : 1.0f);
            return AccessCount * recency;
        }
    }
}
