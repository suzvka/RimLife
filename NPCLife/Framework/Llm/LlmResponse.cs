using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM 对话响应。内部统一格式，由适配器从 API 特定格式转换而来。
    /// 零外部依赖。
    /// </summary>
    public class LlmResponse
    {
        /// <summary>LLM 返回的文本内容。tool_calls 时可能为 null/空。</summary>
        public string Content { get; set; } = "";

        /// <summary>工具调用请求列表。LLM 请求工具时非空。</summary>
        public List<LlmToolCall> ToolCalls { get; set; }

        /// <summary>
        /// 结束原因：stop（正常结束）/ tool_calls（请求工具调用）/ length（token 截断）/ error。
        /// </summary>
        public string FinishReason { get; set; } = "stop";

        /// <summary>消耗的 token 总数（含 input + output）。</summary>
        public int? UsageTotalTokens { get; set; }

        /// <summary>输入 token 数（prompt_tokens / input_tokens）。</summary>
        public int? UsageInputTokens { get; set; }

        /// <summary>输出 token 数（completion_tokens / output_tokens）。</summary>
        public int? UsageOutputTokens { get; set; }

        /// <summary>缓存命中读取的 token 数（cached_tokens / cache_read_input_tokens）。
        /// 各厂商语义不同，适配器负责统一映射到此字段。</summary>
        public int? UsageCacheReadTokens { get; set; }

        /// <summary>实际使用的模型名称（可能与请求不同）。</summary>
        public string Model { get; set; } = "";

        /// <summary>错误信息。非空表示请求失败。</summary>
        public string Error { get; set; }

        /// <summary>是否成功。</summary>
        public bool IsSuccess => string.IsNullOrEmpty(Error);

        /// <summary>是否请求工具调用。</summary>
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;

        /// <summary>构造错误响应。</summary>
        public static LlmResponse FromError(string error)
        {
            return new LlmResponse
            {
                Error = error ?? "Unknown error",
                FinishReason = "error"
            };
        }
    }
}
