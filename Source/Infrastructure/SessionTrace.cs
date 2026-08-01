using System;
using System.Collections.Generic;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 单次 Agent Run 的完整交互追踪。
    /// 包含事件、Prompt、LLM 请求/响应、工具调用等全文记录，用于离线分析和调优。
    /// </summary>
    public class RunTrace
    {
        /// <summary>运行唯一标识（如 "run-3"）。</summary>
        public string RunId;

        /// <summary>Agent 角色显示名（导演/编剧/即兴编剧）。</summary>
        public string Role;

        /// <summary>工作空间 ID。从 system prompt 中解析，用于区分同一 run 内的不同 agent。</summary>
        public string WorkspaceId;

        /// <summary>工作空间标签（如 "Improviser"、"Unnamed"）。</summary>
        public string WorkspaceLabel;

        /// <summary>运行开始时间。</summary>
        public DateTime StartTime;

        /// <summary>运行结束时间。</summary>
        public DateTime EndTime;

        /// <summary>触发本次运行的事件摘要。</summary>
        public List<EventSummary> Events = new List<EventSummary>();

        /// <summary>构造后的完整用户消息（Prompt 全文）。</summary>
        public string UserMessage;

        /// <summary>LLM 交互轮次记录（含请求和响应全文）。</summary>
        public List<RoundTrace> Rounds = new List<RoundTrace>();

        /// <summary>总工具调用轮数。</summary>
        public int TotalRounds;

        /// <summary>处理的事件数。</summary>
        public int EventsProcessed;

        /// <summary>是否正常完成（false = 异常或达到最大轮数）。</summary>
        public bool NormalCompletion;

        /// <summary>运行时长（毫秒）。</summary>
        public long DurationMs => EndTime > StartTime
            ? (long)((EndTime - StartTime).TotalMilliseconds)
            : 0;

        /// <summary>累计输入 Token。</summary>
        public int TotalInputTokens
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Rounds.Count; i++)
                    sum += Rounds[i].InputTokens;
                return sum;
            }
        }

        /// <summary>累计输出 Token。</summary>
        public int TotalOutputTokens
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Rounds.Count; i++)
                    sum += Rounds[i].OutputTokens;
                return sum;
            }
        }
    }

    /// <summary>触发事件摘要。</summary>
    public class EventSummary
    {
        /// <summary>事件 ID。</summary>
        public string EventId;

        /// <summary>事件类型（如 Raid, SocialInteraction, Death 等）。</summary>
        public string Type;

        /// <summary>事件重要度。</summary>
        public float Importance;
    }

    /// <summary>单轮 LLM 交互记录。</summary>
    public class RoundTrace
    {
        /// <summary>轮次序号（从 0 开始）。</summary>
        public int RoundIndex;

        /// <summary>LLM 请求中的消息列表摘要（role → content 全文）。</summary>
        public List<MessageSnapshot> RequestMessages = new List<MessageSnapshot>();

        /// <summary>LLM 响应文本（可能为空，当有工具调用时）。</summary>
        public string ResponseContent;

        /// <summary>结束原因（stop / tool_calls / length）。</summary>
        public string FinishReason;

        /// <summary>输入 Token 数。</summary>
        public int InputTokens;

        /// <summary>输出 Token 数。</summary>
        public int OutputTokens;

        /// <summary>缓存命中 Token 数。</summary>
        public int CacheReadTokens;

        /// <summary>实际使用的模型名称。</summary>
        public string Model;

        /// <summary>本轮工具调用明细。</summary>
        public List<ToolCallTrace> ToolCalls = new List<ToolCallTrace>();
    }

    /// <summary>LLM 消息快照。</summary>
    public class MessageSnapshot
    {
        /// <summary>消息角色（system / user / assistant / tool）。</summary>
        public string Role;

        /// <summary>消息全文内容。</summary>
        public string Content;

        /// <summary>工具调用列表（assistant 消息可能包含）。</summary>
        public string ToolCallsJson;
    }

    /// <summary>工具调用记录。</summary>
    public class ToolCallTrace
    {
        /// <summary>工具名称。</summary>
        public string ToolName;

        /// <summary>工具参数（JSON 全文）。</summary>
        public string Arguments;

        /// <summary>工具执行结果（JSON 全文）。</summary>
        public string Result;

        /// <summary>是否被拦截器取消。</summary>
        public bool Cancelled;
    }

    /// <summary>
    /// 会话追踪存储。线程安全，保留最近 N 条 RunTrace。
    /// 由 SessionTraceRecorder 写入，由 DashboardPage UI 读取。
    /// </summary>
    public static class SessionTraceStore
    {
        /// <summary>最大保留的 RunTrace 数量。</summary>
        public const int MaxTraces = 50;

        /// <summary>是否启用会话追踪。</summary>
        public static bool Enabled = true;

        private static readonly List<RunTrace> _traces = new List<RunTrace>();
        private static readonly object _lock = new object();

        /// <summary>添加一条完成的 RunTrace。超出容量时移除最旧的。</summary>
        public static void Add(RunTrace trace)
        {
            if (trace == null) return;
            lock (_lock)
            {
                _traces.Add(trace);
                while (_traces.Count > MaxTraces)
                    _traces.RemoveAt(0);
            }
        }

        /// <summary>获取所有 RunTrace 的快照（从新到旧）。</summary>
        public static List<RunTrace> GetAll()
        {
            lock (_lock)
            {
                var copy = new List<RunTrace>(_traces);
                copy.Reverse();
                return copy;
            }
        }

        /// <summary>获取指定 RunId 的 RunTrace。</summary>
        public static RunTrace GetByRunId(string runId)
        {
            if (runId == null) return null;
            lock (_lock)
            {
                for (int i = _traces.Count - 1; i >= 0; i--)
                {
                    if (_traces[i].RunId == runId)
                        return _traces[i];
                }
                return null;
            }
        }

        /// <summary>获取当前追踪数量。</summary>
        public static int Count
        {
            get { lock (_lock) { return _traces.Count; } }
        }

        /// <summary>清空所有追踪。</summary>
        public static void Clear()
        {
            lock (_lock) { _traces.Clear(); }
        }
    }
}
