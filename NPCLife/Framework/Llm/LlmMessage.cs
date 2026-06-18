using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM 对话消息。内部统一格式，适配器负责转换为 API 特定格式。
    /// </summary>
    public class LlmMessage
    {
        /// <summary>角色：system / user / assistant / tool。</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息文本内容。</summary>
        public string Content { get; set; } = "";

        /// <summary>工具调用 ID（tool 角色时使用）。</summary>
        public string ToolCallId { get; set; }

        /// <summary>工具调用列表（assistant 角色请求工具时使用）。</summary>
        public List<LlmToolCall> ToolCalls { get; set; }

        /// <summary>快捷构造 user 消息。</summary>
        public static LlmMessage User(string content)
        {
            return new LlmMessage { Role = "user", Content = content ?? "" };
        }

        /// <summary>快捷构造 assistant 消息。</summary>
        public static LlmMessage Assistant(string content)
        {
            return new LlmMessage { Role = "assistant", Content = content ?? "" };
        }

        /// <summary>快捷构造 system 消息。</summary>
        public static LlmMessage System(string content)
        {
            return new LlmMessage { Role = "system", Content = content ?? "" };
        }

        /// <summary>快捷构造 tool 结果消息。</summary>
        public static LlmMessage ToolResult(string toolCallId, string content)
        {
            return new LlmMessage
            {
                Role = "tool",
                Content = content ?? "",
                ToolCallId = toolCallId
            };
        }

        /// <summary>快捷构造 assistant 消息（含工具调用请求）。</summary>
        public static LlmMessage AssistantWithTools(List<LlmToolCall> toolCalls)
        {
            return new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls = toolCalls
            };
        }
    }
}
