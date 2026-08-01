using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Text;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 会话追踪录制器。实现 ISessionTraceRecorder，
    /// 将 AgentLoop 全链路数据写入 SessionTraceStore，供 DashboardPage 展示。
    ///
    /// 替代原先 SessionTraceInterceptor（基于 AgentPipeline 的拦截器方式）。
    /// </summary>
    internal class SessionTraceRecorder : ISessionTraceRecorder
    {
        private readonly ILogger _logger;

        [ThreadStatic]
        private static Dictionary<string, RunTrace> _traces;

        [ThreadStatic]
        private static Dictionary<string, RoundTrace> _currentRounds;

        public SessionTraceRecorder(ILogger logger = null)
        {
            _logger = logger;
        }

        public void BeginRun(string runId, IReadOnlyList<IGameEvent> events, string userMessage)
        {
            if (!SessionTraceStore.Enabled) return;

            if (_traces == null)
                _traces = new Dictionary<string, RunTrace>();

            _traces[runId] = new RunTrace
            {
                RunId = runId,
                Role = "Agent",
                StartTime = DateTime.UtcNow,
                UserMessage = userMessage ?? ""
            };

            if (events != null)
            {
                var trace = _traces[runId];
                foreach (var evt in events)
                {
                    trace.Events.Add(new EventSummary
                    {
                        EventId = evt.EventID ?? "",
                        Type = evt.DefName ?? "",
                        Importance = evt.Importance
                    });
                }
            }
        }

        public void RecordLlmRound(string runId, int round,
            IReadOnlyList<LlmMessage> requestMessages, LlmResponse response)
        {
            if (!SessionTraceStore.Enabled) return;
            var trace = GetTrace(runId);
            if (trace == null || response == null) return;

            // 首次 LLM 调用时从 system 消息解析工作空间信息
            if (trace.Rounds.Count == 0 && requestMessages != null)
            {
                foreach (var msg in requestMessages)
                {
                    if (msg.Role == "system" && !string.IsNullOrEmpty(msg.Content))
                    {
                        ParseWorkspaceInfo(msg.Content, trace);
                        break;
                    }
                }
            }

            var roundTrace = new RoundTrace
            {
                RoundIndex = round,
                ResponseContent = response.Content ?? "",
                FinishReason = response.FinishReason ?? "",
                InputTokens = response.UsageInputTokens ?? 0,
                OutputTokens = response.UsageOutputTokens ?? 0,
                CacheReadTokens = response.UsageCacheReadTokens ?? 0,
                Model = response.Model ?? ""
            };

            if (requestMessages != null)
            {
                foreach (var msg in requestMessages)
                {
                    var snap = new MessageSnapshot
                    {
                        Role = msg.Role ?? "",
                        Content = msg.Content ?? ""
                    };
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var tc in msg.ToolCalls)
                            sb.AppendLine($"[{tc.Name}] {tc.Arguments}");
                        snap.ToolCallsJson = sb.ToString().TrimEnd();
                    }
                    roundTrace.RequestMessages.Add(snap);
                }
            }

            trace.Rounds.Add(roundTrace);

            if (_currentRounds == null)
                _currentRounds = new Dictionary<string, RoundTrace>();
            _currentRounds[runId] = roundTrace;
        }

        public void RecordToolCall(string runId, int round, string toolName,
            string arguments, string result, bool cancelled)
        {
            if (!SessionTraceStore.Enabled || _currentRounds == null) return;
            if (!_currentRounds.TryGetValue(runId, out var roundTrace)) return;

            roundTrace.ToolCalls.Add(new ToolCallTrace
            {
                ToolName = toolName ?? "",
                Arguments = arguments ?? "",
                Result = result ?? "",
                Cancelled = cancelled
            });
        }

        public void EndRun(string runId, int rounds, int eventsProcessed, bool normalCompletion)
        {
            var trace = GetTrace(runId);
            if (trace == null) return;

            trace.TotalRounds = rounds;
            trace.EventsProcessed = eventsProcessed;
            trace.NormalCompletion = normalCompletion;
            trace.EndTime = DateTime.UtcNow;

            SessionTraceStore.Add(trace);

            _traces?.Remove(runId);
            _currentRounds?.Remove(runId);
        }

        private static RunTrace GetTrace(string runId)
        {
            if (_traces == null || string.IsNullOrEmpty(runId)) return null;
            _traces.TryGetValue(runId, out var trace);
            return trace;
        }

        private static void ParseWorkspaceInfo(string systemContent, RunTrace trace)
        {
            if (string.IsNullOrEmpty(systemContent)) return;
            var lines = systemContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.StartsWith("工作空间 ID："))
                    trace.WorkspaceId = t.Substring("工作空间 ID：".Length).Trim();
                else if (t.StartsWith("工作空间："))
                    trace.WorkspaceLabel = t.Substring("工作空间：".Length).Trim();
            }
        }
    }
}
