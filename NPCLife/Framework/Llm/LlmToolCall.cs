namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM 工具调用。LLM 请求执行某个 MCP 工具时返回。
    /// 零外部依赖。
    /// </summary>
    public class LlmToolCall
    {
        /// <summary>工具调用唯一 ID。用于后续 tool 消息关联。</summary>
        public string Id { get; set; } = "";

        /// <summary>工具名称。</summary>
        public string Name { get; set; } = "";

        /// <summary>工具参数 JSON 字符串。</summary>
        public string Arguments { get; set; } = "{}";
    }
}
