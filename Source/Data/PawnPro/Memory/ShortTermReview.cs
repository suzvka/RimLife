namespace RimLife
{
    /// <summary>
    /// 短期回顾：LLM 对近期 STM 流水的客观摘要。
    /// 巩固时生成，严格限制字数（≤300 字）。
    /// 纯 DTO，零外部依赖。序列化由 HediffComp_PawnMemory 处理。
    /// </summary>
    public class ShortTermReview
    {
        /// <summary>最后更新时刻 (游戏 tick)。</summary>
        public int LastUpdateTick;

        /// <summary>LLM 生成的客观摘要（≤300 字）。</summary>
        public string Content;

        public ShortTermReview() { }

        public ShortTermReview(int lastUpdateTick, string content)
        {
            LastUpdateTick = lastUpdateTick;
            Content = content ?? "";
        }

        public override string ToString()
        {
            string preview = string.IsNullOrEmpty(Content) ? "(empty)" :
                Content.Length <= 60 ? Content : Content.Substring(0, 60) + "…";
            return $"[Review t={LastUpdateTick}] {preview}";
        }
    }
}
