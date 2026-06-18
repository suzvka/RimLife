using System;

namespace NPCLife.Framework
{
    /// <summary>
    /// 运行时度量拦截器。实现 IAgentInterceptor，在 Agent 循环的关键拦截点采集：
    /// - MCP 工具调用频率和成功率
    /// - Agent 循环统计（轮数、事件数、角色）
    ///
    /// Token 消耗通过 EventBus 订阅 llm.response_received 独立采集，
    /// 知识库命中率通过 KnowledgeBaseChain.OnLookupResult 回调采集。
    ///
    /// 框架侧纯逻辑组件，零 RimWorld 依赖。
    /// </summary>
    public class MetricsInterceptor : AgentInterceptorBase
    {
        private readonly AgentRole _role;

        [ThreadStatic]
        private static string _currentSessionId;

        [ThreadStatic]
        private static int _llmCallCountInSession;

        /// <summary>
        /// 创建度量拦截器。
        /// </summary>
        /// <param name="role">所属 Agent 角色。</param>
        public MetricsInterceptor(AgentRole role)
        {
            _role = role;
        }

        // ================================================================
        // IAgentInterceptor
        // ================================================================

        /// <summary>
        /// 在每个 LLM 请求发送前调用。检测是否需要开始新会话。
        /// </summary>
        public override void OnBeforeLlm(LlmContext ctx)
        {
            if (_currentSessionId == null)
            {
                _currentSessionId = RuntimeMetrics.BeginSession(_role);
                _llmCallCountInSession = 0;
            }
            _llmCallCountInSession++;
        }

        /// <summary>
        /// 工具调用前：记录工具名。
        /// 实际计数在 AfterToolCall 中完成（需判断成功/失败）。
        /// </summary>
        public override void OnBeforeToolCall(ToolCallContext ctx)
        {
            // 不做计数，等 AfterToolCall 再判断结果
        }

        /// <summary>
        /// 工具调用后：记录调用次数和成功/失败。
        /// </summary>
        public override void OnAfterToolCall(ToolCallContext ctx)
        {
            if (_currentSessionId == null || ctx?.ToolName == null) return;

            bool success = !IsErrorResult(ctx.Result);
            RuntimeMetrics.RecordToolCall(_currentSessionId, ctx.ToolName, success);
        }

        /// <summary>
        /// Agent 循环结束：记录统计并结束会话。
        /// </summary>
        public override void OnLoopFinished(LoopContext ctx)
        {
            RuntimeMetrics.RecordLoopFinished(
                ctx.Rounds,
                ctx.EventsProcessed,
                ctx.NormalCompletion,
                _role);

            if (_currentSessionId != null)
            {
                RuntimeMetrics.EndSession(_currentSessionId);
                _currentSessionId = null;
                _llmCallCountInSession = 0;
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static bool IsErrorResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return false;
            // 检查是否包含 error JSON 标记
            string trimmed = result.TrimStart();
            return trimmed.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("{\"error:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取当前会话 ID（调试用）。
        /// </summary>
        public static string CurrentSessionId => _currentSessionId;

        /// <summary>
        /// 获取当前角色（调试用）。
        /// </summary>
        public AgentRole Role => _role;
    }
}
