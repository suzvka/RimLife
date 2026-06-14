using RimLife.Cards;
using RimLife.Core;
using RimLife.Driver;
using RimLife.Framework;
using RimLife.Framework.Llm;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Text;

namespace RimLife.Agent
{
    /// <summary>
    /// Agent 循环。纯逻辑组件，零游戏引擎依赖。
    /// 通过订阅 AgentEventPool.OnThresholdReached 被动激活。
    ///
    /// 生命周期：
    /// 1. 池子通知阈值达到 → OnPoolChanged()
    /// 2. Drain → Prompt → LLM → 工具调用循环
    /// 3. 循环结束 → 重置状态，等待下次通知
    /// </summary>
    public class AgentLoop : IDisposable
    {
        private readonly IEventLog _pool;
        private readonly ILlmChatService _llm;
        private readonly ILogger _logger;
        private readonly string _systemPrompt;
        private readonly string[] _skillIds;
        private readonly int _maxRounds;
        private readonly ICardSerializer _serializer;
        private readonly Action _unsubscribe; // 取消事件订阅的委托

        private bool _isProcessing;
        private int _round;
        private List<LlmMessage> _messages;
        private IReadOnlyList<IGameEvent> _drained;

        /// <summary>
        /// 创建 AgentLoop 并自动订阅池子的 OnThresholdReached 事件。
        /// </summary>
        /// <param name="pool">事件池。Agent 从该池 drain 事件。</param>
        /// <param name="llm">LLM 异步对话服务。</param>
        /// <param name="systemPrompt">系统提示词。</param>
        /// <param name="skillIds">激活的 Skill ID 列表（MCP 工具集）。</param>
        /// <param name="maxRounds">最大工具调用轮数（防死循环）。</param>
        /// <param name="logger">日志接口。</param>
        /// <param name="serializer">Card 序列化器（可选，默认使用 CardSerializer.Default）。</param>
        public AgentLoop(
            IEventLog pool,
            ILlmChatService llm,
            string systemPrompt,
            string[] skillIds,
            int maxRounds,
            ILogger logger,
            ICardSerializer serializer = null)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _systemPrompt = systemPrompt ?? "";
            _skillIds = skillIds ?? Array.Empty<string>();
            _maxRounds = maxRounds;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializer = serializer ?? CardSerializer.Default;

            // 订阅池子事件——唯一激活路径
            _pool.OnThresholdReached += OnPoolChanged;
            _unsubscribe = () => _pool.OnThresholdReached -= OnPoolChanged;
        }

        // ================================================================
        // 唯一入口
        // ================================================================

        private void OnPoolChanged()
        {
            if (_isProcessing) return;
            if (_pool.PendingCount == 0) return;

            // 开始请求链路追踪
            ErrorHandler.BeginTrace();
            EventBus.Publish(FrameworkEvents.AgentActivated, EventArg.WithPayload(
                ("pendingCount", _pool.PendingCount.ToString()),
                ("totalImportance", _pool.TotalImportance.ToString())
            ));

            Activate();
        }

        // ================================================================
        // Agent Loop
        // ================================================================

        private void Activate()
        {
            _isProcessing = true;
            _round = 0;
            _drained = _pool.DrainPending();

            if (_drained.Count == 0)
            {
                _isProcessing = false;
                return;
            }

            _logger.Message($"[RimLife.Agent] Activated with {_drained.Count} events (importance={_pool.TotalImportance})");

            _messages = new List<LlmMessage>
            {
                LlmMessage.System(_systemPrompt),
                LlmMessage.User(BuildUserMessage(_drained))
            };

            SendChat();
        }

        private void SendChat()
        {
            var request = new LlmRequest
            {
                Messages = new List<LlmMessage>(_messages),
                ToolsJson = McpSkillRegistry.GetActiveToolsJson(_skillIds),
                MaxTokens = 4096,
                Temperature = 0.7f
            };

            // 管道拦截：LLM 请求前
            var llmCtx = new LlmContext { Request = request };
            AgentPipeline.RunBeforeLlm(llmCtx);

            EventBus.Publish(FrameworkEvents.LlmRequestSent, EventArg.WithPayload(
                ("round", _round.ToString()),
                ("messageCount", _messages.Count.ToString())
            ));

            _llm.ChatAsync(llmCtx.Request, OnSuccess, OnError);
        }

        private void OnSuccess(LlmResponse response)
        {
            if (response == null || !response.IsSuccess)
            {
                OnError(response?.Error ?? "null response");
                return;
            }

            // 将 LLM 回复加入消息历史
            if (!string.IsNullOrEmpty(response.Content))
                _messages.Add(LlmMessage.Assistant(response.Content));

            EventBus.Publish(FrameworkEvents.LlmResponseReceived, EventArg.WithPayload(
                ("hasToolCalls", response.HasToolCalls.ToString()),
                ("contentLength", (response.Content?.Length ?? 0).ToString())
            ));

            // 工具调用？
            if (response.HasToolCalls)
            {
                _round++;

                if (_round >= _maxRounds)
                {
                    _logger.Warning($"[RimLife.Agent] Reached max rounds ({_maxRounds}). Ending loop.");
                    Finish(false);
                    return;
                }

                var toolCallsForMessage = new List<LlmToolCall>();
                var toolResults = new List<(string id, string result)>();

                foreach (var tc in response.ToolCalls)
                {
                    _logger.Message($"[RimLife.Agent] Tool call: {tc.Name}({tc.Arguments})");

                    // 管道拦截：工具调用前
                    var toolCtx = new ToolCallContext { ToolName = tc.Name, Arguments = tc.Arguments };
                    AgentPipeline.RunBeforeToolCall(toolCtx);

                    EventBus.Publish(FrameworkEvents.ToolInvoking, EventArg.WithPayload(
                        ("toolName", tc.Name), ("round", _round.ToString())
                    ));

                    string result;
                    if (toolCtx.Cancelled)
                    {
                        result = "{\"error\":\"cancelled by interceptor\"}";
                    }
                    else
                    {
                        result = McpSkillRegistry.InvokeTool(_skillIds, tc.Name, tc.Arguments);
                        toolCtx.Result = result;
                    }

                    // 管道拦截：工具调用后
                    AgentPipeline.RunAfterToolCall(toolCtx);

                    EventBus.Publish(FrameworkEvents.ToolInvoked, EventArg.WithPayload(
                        ("toolName", tc.Name), ("resultLength", (result?.Length ?? 0).ToString())
                    ));

                    toolCallsForMessage.Add(tc);
                    toolResults.Add((tc.Id, result));

                    _logger.Message($"[RimLife.Agent] Tool result ({tc.Name}): {TruncateResult(result)}");
                }

                // 添加含 tool_calls 的 assistant 消息
                var assistantMsg = new LlmMessage
                {
                    Role = "assistant",
                    Content = response.Content ?? "",
                    ToolCalls = toolCallsForMessage
                };
                _messages.Add(assistantMsg);

                // 为每个工具调用添加 tool 结果消息
                foreach (var (id, result) in toolResults)
                {
                    _messages.Add(LlmMessage.ToolResult(id, result));
                }

                EventBus.Publish(FrameworkEvents.AgentRoundComplete, EventArg.WithPayload(
                    ("round", _round.ToString()),
                    ("toolCallCount", response.ToolCalls.Count.ToString())
                ));

                // 继续下一轮
                SendChat();
            }
            else
            {
                // 无工具调用：Agent 完成决策
                _logger.Message("[RimLife.Agent] Loop finished (stop).");
                Finish();
            }
        }

        private void OnError(string error)
        {
            _logger.Warning($"[RimLife.Agent] LLM error: {error}. Events remain in pool for retry.");
            ErrorHandler.ReportError("AgentLoop", error, new System.Collections.Generic.Dictionary<string, string>
            {
                {"round", _round.ToString()},
                {"drainedCount", (_drained?.Count ?? 0).ToString()}
            });

            if (_drained != null)
            {
                foreach (var evt in _drained)
                    _pool.Append(evt);
            }
            _drained = null;
            _messages = null;
            _isProcessing = false;

            ErrorHandler.EndTrace();
            EventBus.Publish(FrameworkEvents.AgentLoopFinished, EventArg.WithPayload(
                ("rounds", _round.ToString()),
                ("error", error ?? "unknown")
            ));
        }

        private void Finish(bool normalCompletion = true)
        {
            int count = _drained?.Count ?? 0;
            int rounds = _round;
            _drained = null;
            _messages = null;
            _isProcessing = false;

            _logger.Message($"[RimLife.Agent] Loop complete. {count} events processed.");

            // 管道拦截：循环结束
            AgentPipeline.RunLoopFinished(new LoopContext
            {
                Rounds = rounds,
                EventsProcessed = count,
                NormalCompletion = normalCompletion
            });

            ErrorHandler.EndTrace();
            EventBus.Publish(FrameworkEvents.AgentLoopFinished, EventArg.WithPayload(
                ("rounds", rounds.ToString()),
                ("eventsProcessed", count.ToString()),
                ("normalCompletion", normalCompletion.ToString())
            ));
        }

        // ================================================================
        // Prompt 构造
        // ================================================================

        private string BuildUserMessage(IReadOnlyList<IGameEvent> events)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 待处理事件");
            sb.AppendLine();
            sb.AppendLine(_serializer.SerializeEventList(events));
            sb.AppendLine();
            sb.AppendLine("请审查事件列表，挑选值得发展的事件，使用 create_workspace / branch_workspace 等工具创建剧情线工作空间。");
            return sb.ToString();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static string TruncateResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return "(empty)";
            return result.Length > 200 ? result.Substring(0, 200) + "..." : result;
        }

        /// <summary>获取当前处理状态（调试用）。</summary>
        public bool IsProcessing => _isProcessing;

        /// <summary>获取当前 Agent 轮数（调试用）。</summary>
        public int CurrentRound => _round;

        // ================================================================
        // IDisposable
        // ================================================================

        /// <summary>取消事件订阅、清空状态。</summary>
        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _drained = null;
            _messages = null;
            _isProcessing = false;
        }
    }
}
