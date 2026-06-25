using System.Collections.Generic;

namespace RimLife
{
    /// <summary>
    /// 长期记忆条目：由 LLM 在旧存档基础上重写的叙述性记忆。
    /// 纯 DTO，零外部依赖。序列化由 HediffComp_PawnMemory 处理。
    /// </summary>
    /// <remarks>重要度通过篇幅自然体现，无需显式打分字段。</remarks>
    public class LongTermMemory
    {
        /// <summary>最后重写时刻 (游戏 tick)。</summary>
        public int ConsolidatedTick;

        /// <summary>主题标签，如 "relationship:Alice"、"combat"、"colony_life"。
        /// 用于 LLM 分组重写时的定位，也用于查询过滤。</summary>
        public string Topic;

        /// <summary>叙述正文（≤500 字）。由 LLM 融合新旧事实生成。</summary>
        public string Summary;

        /// <summary>所有关联角色 ThingID（可能多个，从多条 STM 合并而来）。</summary>
        public List<string> RelatedPawnIds;

        public LongTermMemory()
        {
            RelatedPawnIds = new List<string>();
        }

        public LongTermMemory(int consolidatedTick, string topic, string summary, List<string> relatedPawnIds)
        {
            ConsolidatedTick = consolidatedTick;
            Topic = topic ?? "";
            Summary = summary ?? "";
            RelatedPawnIds = relatedPawnIds ?? new List<string>();
        }

        /// <summary>
        /// 将长期记忆摘要截断至指定长度。
        /// </summary>
        public string TruncatedSummary(int maxLength = 500)
        {
            if (string.IsNullOrEmpty(Summary)) return "";
            return Summary.Length <= maxLength ? Summary : Summary.Substring(0, maxLength) + "…";
        }

        public override string ToString()
        {
            return $"[LTM t={ConsolidatedTick} topic={Topic}] {TruncatedSummary(80)}";
        }
    }
}
