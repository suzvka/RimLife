using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 会话追踪拦截器。以 RunId 为键分离不同 agent 的会话，
    /// 解决 Director/Screenwriter/Improviser 交错执行时的 trace 混合。
    ///
    /// 每个 OnBeforePrompt 创建新 trace，OnLoopFinished 归档。
    /// </summary>
    public class SessionTraceInterceptor : AgentInterceptorBase
    {
        [ThreadStatic]
        private static Dictionary<string, RunTrace> _traces;

        [ThreadStatic]
        private static Dictionary<string, RoundTrace> _currentRounds;

        public static bool Enabled = true;

        public override void OnBeforePrompt(PromptContext ctx)
        {
            if (!Enabled) return;

            if (_traces == null)
                _traces = new Dictionary<string, RunTrace>();

            _traces[ctx.RunId] = new RunTrace
            {
                RunId = ctx.RunId,
                Role = "Agent",
                StartTime = DateTime.UtcNow,
                UserMessage = ctx.UserMessage ?? ""
            };

            if (ctx.Events != null)
            {
                var trace = _traces[ctx.RunId];
                foreach (var evt in ctx.Events)
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

        public override void OnAfterLlm(LlmContext ctx)
        {
            if (!Enabled) return;
            var trace = GetTrace(ctx.RunId);
            if (trace == null) return;

            var response = ctx.Response;
            if (response == null) return;

            if (trace.Rounds.Count == 0 && ctx.Request?.Messages != null)
            {
                foreach (var msg in ctx.Request.Messages)
                {
                    if (msg.Role == "system" && !string.IsNullOrEmpty(msg.Content))
                    {
                        ParseWorkspaceInfo(msg.Content, trace);
                        break;
                    }
                }
            }

            var round = new RoundTrace
            {
                RoundIndex = ctx.Round,
                ResponseContent = response.Content ?? "",
                FinishReason = response.FinishReason ?? "",
                InputTokens = response.UsageInputTokens ?? 0,
                OutputTokens = response.UsageOutputTokens ?? 0,
                CacheReadTokens = response.UsageCacheReadTokens ?? 0,
                Model = response.Model ?? ""
            };

            if (ctx.Request?.Messages != null)
            {
                foreach (var msg in ctx.Request.Messages)
                {
                    var snap = new MessageSnapshot
                    {
                        Role = msg.Role ?? "",
                        Content = msg.Content ?? ""
                    };
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var tc in msg.ToolCalls)
                            sb.AppendLine($"[{tc.Name}] {tc.Arguments}");
                        snap.ToolCallsJson = sb.ToString().TrimEnd();
                    }
                    round.RequestMessages.Add(snap);
                }
            }

            trace.Rounds.Add(round);

            if (_currentRounds == null)
                _currentRounds = new Dictionary<string, RoundTrace>();
            _currentRounds[ctx.RunId] = round;
        }

        public override void OnAfterToolCall(ToolCallContext ctx)
        {
            if (!Enabled || _currentRounds == null) return;
            if (!_currentRounds.TryGetValue(ctx.RunId, out var round)) return;

            round.ToolCalls.Add(new ToolCallTrace
            {
                ToolName = ctx.ToolName ?? "",
                Arguments = ctx.Arguments ?? "",
                Result = ctx.Result ?? "",
                Cancelled = ctx.Cancelled
            });
        }

        public override void OnLoopFinished(LoopContext ctx)
        {
            var trace = GetTrace(ctx.RunId);
            if (trace == null) return;

            trace.Role = RoleLabel(ctx.Role);
            trace.TotalRounds = ctx.Rounds;
            trace.EventsProcessed = ctx.EventsProcessed;
            trace.NormalCompletion = ctx.NormalCompletion;
            trace.EndTime = DateTime.UtcNow;

            SessionTraceStore.Add(trace);

            _traces.Remove(ctx.RunId);
            _currentRounds?.Remove(ctx.RunId);
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

        private static string RoleLabel(AgentRole role)
        {
            switch (role)
            {
                case AgentRole.Director: return "导演";
                case AgentRole.Screenwriter: return "编剧";
                case AgentRole.Improviser: return "即兴编剧";
                default: return role.ToString();
            }
        }
    }
}
