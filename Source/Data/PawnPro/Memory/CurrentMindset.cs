namespace RimLife
{
    /// <summary>
    /// 即时心境：凌驾于所有记忆区之上的第一人称心理状态描述。
    /// LLM 写入时已经消化了全部下层记忆，消费者优先读取心境，
    /// 仅在需要细节时才向下钻取到 STM/LTM/Review。
    /// 由 LLM 通过 MCP 工具主动调用写入，不依赖任何自动触发。
    /// 纯 DTO，零外部依赖。序列化由 HediffComp_PawnMemory 处理。
    /// </summary>
    public class CurrentMindset
    {
        /// <summary>最后更新时刻 (游戏 tick)。</summary>
        public int LastUpdateTick;

        /// <summary>LLM 第一人称心理描述（≤200 字）。</summary>
        public string Content;

        public CurrentMindset() { }

        public CurrentMindset(int lastUpdateTick, string content)
        {
            LastUpdateTick = lastUpdateTick;
            Content = content ?? "";
        }

        public override string ToString()
        {
            string preview = string.IsNullOrEmpty(Content) ? "(empty)" :
                Content.Length <= 60 ? Content : Content.Substring(0, 60) + "…";
            return $"[Mindset t={LastUpdateTick}] {preview}";
        }
    }
}
