namespace RimLife
{
    /// <summary>
    /// 短期记忆条目：记录 pawn 近期经历的原始事件流水。
    /// 纯 DTO，零外部依赖。序列化由 HediffComp_PawnMemory 处理。
    /// 自然上限：巩固后清空，时间窗口 ≤ 24h，不会无限膨胀。
    /// 重要度不在此层记录——在 LLM 重写为 LTM 时自然涌现。
    /// </summary>
    public class ShortTermMemory
    {
        /// <summary>发生时刻 (游戏 tick)。</summary>
        public int Tick;

        /// <summary>类型标签：Interaction / Event / Combat / Observation / Milestone。</summary>
        public string Type;

        /// <summary>简短摘要（≤200 字）。</summary>
        public string Summary;

        /// <summary>关联角色 ThingID（可选，无关联时为 null）。</summary>
        public string RelatedPawnId;

        public ShortTermMemory() { }

        public ShortTermMemory(int tick, string type, string summary, string relatedPawnId = null)
        {
            Tick = tick;
            Type = type ?? "Observation";
            Summary = summary ?? "";
            RelatedPawnId = relatedPawnId;
        }

        /// <summary>
        /// 将摘要截断至 maxLength 字符以内。用于注入 prompt 时控制 token 消耗。
        /// </summary>
        public string TruncatedSummary(int maxLength = 200)
        {
            if (string.IsNullOrEmpty(Summary)) return "";
            return Summary.Length <= maxLength ? Summary : Summary.Substring(0, maxLength) + "…";
        }

        public override string ToString()
        {
            return $"[STM t={Tick} {Type}] {TruncatedSummary(80)}";
        }
    }
}
