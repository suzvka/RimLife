using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NPCLife.Agent
{
    /// <summary>
    /// Agent 循环。纯逻辑组件，零游戏引擎依赖。
    /// 通过订阅 IEventLog.OnThresholdReached 被动激活。
    ///
    /// 生命周期：
    /// 1. 池子通知阈值达到 → OnPoolChanged()
    /// 2. Drain → Prompt → LLM → 工具调用循环
    /// 3. 循环结束 → 重置状态，等待下次通知
    /// </summary>
    public class AgentLoop : IDisposable
    {
        private readonly IEventLog _pool;
        private readonly ILlmService _llm;
        private readonly ICredentialRegistry _credentialRegistry;
        private readonly ILogger _logger;
        private readonly string _systemPrompt;
        private readonly string[] _skillIds;
        private readonly int _maxRounds;
        private readonly ICardSerializer _serializer;
        private readonly Action _unsubscribe; // 取消事件订阅的委托
        private readonly Func<string> _contextProvider;
        private readonly IKnowledgeBase _knowledgeBase;
        private readonly float _temperature;
        private readonly string _modelAlias; // 关联的模型代号

        private bool _isProcessing;
        private int _round;
        private List<LlmMessage> _messages;
        private IReadOnlyList<IGameEvent> _drained;

        /// <summary>
        /// 创建 AgentLoop 并自动订阅池子的 OnThresholdReached 事件。
        /// </summary>
        /// <param name="pool">事件池。Agent 从该池 drain 事件。</param>
        /// <param name="llm">LLM 异步对话服务（无状态）。</param>
        /// <param name="credentialRegistry">凭证注册表。提供当前可用的 API 凭证列表。</param>
        /// <param name="systemPrompt">系统提示词。</param>
        /// <param name="skillIds">激活的 Skill ID 列表（MCP 工具集）。</param>
        /// <param name="maxRounds">最大工具调用轮数（防死循环）。</param>
        /// <param name="logger">日志接口。</param>
        /// <param name="serializer">Card 序列化器（可选，默认使用 CardSerializer.Default）。</param>
        /// <param name="contextProvider">动态上下文提供者（可选）。每次激活时调用，返回值追加到用户消息末尾。</param>
        /// <param name="knowledgeBase">知识库（可选）。Agent 激活时收集事件关键词去重后批量查询，命中结果注入提示词。</param>
        /// <param name="temperature">LLM 采样温度（0~2），默认 0.7。</param>
        /// <param name="modelAlias">关联的模型代号（如 "primary"），用于从 Registry 解析凭证。默认 "primary"。</param>
        public AgentLoop(
            IEventLog pool,
            ILlmService llm,
            ICredentialRegistry credentialRegistry,
            string systemPrompt,
            string[] skillIds,
            int maxRounds,
            ILogger logger,
            ICardSerializer serializer = null,
            Func<string> contextProvider = null,
            IKnowledgeBase knowledgeBase = null,
            float temperature = 0.7f,
            string modelAlias = "primary")
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _credentialRegistry = credentialRegistry ?? throw new ArgumentNullException(nameof(credentialRegistry));
            _systemPrompt = systemPrompt ?? "";
            _skillIds = skillIds ?? Array.Empty<string>();
            _maxRounds = maxRounds;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializer = serializer ?? CardSerializer.Default;
            _contextProvider = contextProvider;
            _knowledgeBase = knowledgeBase;
            _temperature = temperature;
            _modelAlias = modelAlias ?? "primary";

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

            _logger.Message($"[NPCLife.Agent] Activated with {_drained.Count} events (importance={_pool.TotalImportance})");

            _messages = new List<LlmMessage>
            {
                LlmMessage.System(_systemPrompt),
                LlmMessage.User(BuildUserMessage(_drained))
            };

            SendChat();
        }

        private async void SendChat()
        {
            try
            {
                // 从注册表获取当前激活的凭证列表（用于 fallback）
                var credentials = _credentialRegistry.GetActiveCredentials();
                if (credentials.Count == 0)
                {
                    OnError("No active credentials configured");
                    return;
                }

                var request = new LlmRequest
                {
                    Messages = new List<LlmMessage>(_messages),
                    ToolsJson = McpSkillRegistry.GetActiveToolsJson(_skillIds),
                    Temperature = _temperature
                };

                // 管道拦截：LLM 请求前
                var llmCtx = new LlmContext { Request = request };
                AgentPipeline.RunBeforeLlm(llmCtx);

                EventBus.Publish(FrameworkEvents.LlmRequestSent, EventArg.WithPayload(
                    ("round", _round.ToString()),
                    ("messageCount", _messages.Count.ToString())
                ));

                try
                {
                    var response = await _llm.ChatAsync(llmCtx.Request, credentials);
                    OnSuccess(response);
                }
                catch (Exception e)
                {
                    OnError(e.Message);
                }
            }
            catch (Exception ex)
            {
                OnError($"SendChat fatal: {ex.Message}");
            }
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
                ("contentLength", (response.Content?.Length ?? 0).ToString()),
                ("inputTokens", (response.UsageInputTokens?.ToString() ?? "")),
                ("outputTokens", (response.UsageOutputTokens?.ToString() ?? "")),
                ("cacheReadTokens", (response.UsageCacheReadTokens?.ToString() ?? "")),
                ("model", response.Model ?? "")
            ));

            // 工具调用？
            if (response.HasToolCalls)
            {
                _round++;

                if (_round >= _maxRounds)
                {
                    _logger.Warning($"[NPCLife.Agent] Reached max rounds ({_maxRounds}). Ending loop.");
                    Finish(false);
                    return;
                }

                var toolCallsForMessage = new List<LlmToolCall>();
                var toolResults = new List<(string id, string result)>();

                foreach (var tc in response.ToolCalls)
                {
                    _logger.Message($"[NPCLife.Agent] Tool call: {tc.Name}({tc.Arguments})");

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

                    _logger.Message($"[NPCLife.Agent] Tool result ({tc.Name}): {TruncateResult(result)}");
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
                _logger.Message("[NPCLife.Agent] Loop finished (stop).");
                Finish();
            }
        }

        private void OnError(string error)
        {
            _logger.Warning($"[NPCLife.Agent] LLM error: {error}. Events remain in pool for retry.");
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

            _logger.Message($"[NPCLife.Agent] Loop complete. {count} events processed.");

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

            // 收集所有事件的关键词，去重后批量查询知识库
            if (_knowledgeBase != null)
            {
                var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var evt in events)
                {
                    if (evt.Keywords != null)
                    {
                        foreach (var kw in evt.Keywords)
                        {
                            if (!string.IsNullOrEmpty(kw))
                                allKeywords.Add(kw);
                        }
                    }
                }

                if (allKeywords.Count > 0)
                {
                    var hits = new List<KnowledgeEntry>();
                    foreach (var kw in allKeywords)
                    {
                        if (_knowledgeBase.TryLookup(kw, out var entry))
                            hits.Add(entry);
                    }

                    if (hits.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("## 相关知识");
                        sb.AppendLine();
                        foreach (var entry in hits)
                        {
                            sb.Append("- **");
                            sb.Append(entry.Term ?? "");
                            sb.Append("**: ");
                            sb.AppendLine(entry.Definition ?? "");
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("请审查事件列表，挑选值得发展的事件，使用 create_workspace / branch_workspace 等工具创建剧情线工作空间。");

            if (_contextProvider != null)
            {
                sb.AppendLine();
                sb.AppendLine(_contextProvider());
            }

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
