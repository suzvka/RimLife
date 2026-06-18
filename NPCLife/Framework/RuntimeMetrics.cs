using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NPCLife.Framework
{
    /// <summary>
    /// Agent 角色标识（轻量，不依赖 Workspace 命名空间）。
    /// </summary>
    public enum AgentRole
    {
        Director,
        Screenwriter,
        Freelancer
    }

    /// <summary>
    /// 运行时度量容器。纯静态，零 RimWorld 依赖。
    /// 所有采集通过 MetricsInterceptor 和 EventBus 订阅驱动，不侵入核心循环。
    ///
    /// 使用方式：
    ///   RuntimeMetrics.BeginSession(AgentRole.Director);
    ///   RuntimeMetrics.RecordTokenUsage(sessionId, input, output, cacheRead, model);
    ///   RuntimeMetrics.RecordToolCall(sessionId, toolName, success);
    ///   RuntimeMetrics.EndSession(sessionId);
    ///   var snap = RuntimeMetrics.GetSnapshot();
    /// </summary>
    public static class RuntimeMetrics
    {
        // ================================================================
        // Session
        // ================================================================

        private static long _sessionSeq;
        private static readonly Dictionary<string, SessionInfo> _activeSessions
            = new Dictionary<string, SessionInfo>();
        private static readonly List<SessionInfo> _completedSessions
            = new List<SessionInfo>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 开始一个 Agent 会话。返回会话 ID。
        /// </summary>
        public static string BeginSession(AgentRole role)
        {
            var sessionId = $"sess-{Interlocked.Increment(ref _sessionSeq)}";
            var info = new SessionInfo
            {
                SessionId = sessionId,
                Role = role,
                StartTime = DateTime.UtcNow
            };
            lock (_lock)
            {
                _activeSessions[sessionId] = info;
            }
            return sessionId;
        }

        /// <summary>
        /// 结束 Agent 会话。将度量移入已完成列表。
        /// </summary>
        public static void EndSession(string sessionId)
        {
            if (sessionId == null) return;
            lock (_lock)
            {
                if (_activeSessions.TryGetValue(sessionId, out var info))
                {
                    info.EndTime = DateTime.UtcNow;
                    _completedSessions.Add(info);
                    _activeSessions.Remove(sessionId);
                }
            }
        }

        // ================================================================
        // Token
        // ================================================================

        /// <summary>
        /// 记录一次 LLM 请求的 Token 消耗。
        /// </summary>
        public static void RecordTokenUsage(string sessionId,
            int inputTokens, int outputTokens, int cacheReadTokens, string model)
        {
            if (sessionId == null) return;
            lock (_lock)
            {
                if (!_activeSessions.TryGetValue(sessionId, out var info)) return;
                info.TotalInputTokens += inputTokens;
                info.TotalOutputTokens += outputTokens;
                info.TotalCacheReadTokens += cacheReadTokens;
                if (!string.IsNullOrEmpty(model))
                    info.LastModel = model;

                // 按角色分桶
                roleTotalInput[(int)info.Role] += inputTokens;
                roleTotalOutput[(int)info.Role] += outputTokens;
                roleTotalCacheRead[(int)info.Role] += cacheReadTokens;
                roleLlmCalls[(int)info.Role]++;
            }
        }

        // ================================================================
        // MCP Tool
        // ================================================================

        /// <summary>
        /// 记录一次 MCP 工具调用。
        /// </summary>
        public static void RecordToolCall(string sessionId, string toolName, bool success)
        {
            if (sessionId == null || toolName == null) return;
            lock (_lock)
            {
                if (!_activeSessions.TryGetValue(sessionId, out var info)) return;
                if (!info.ToolCalls.TryGetValue(toolName, out int count))
                    count = 0;
                info.ToolCalls[toolName] = count + 1;

                if (!success)
                {
                    if (!info.ToolErrors.TryGetValue(toolName, out int errCount))
                        errCount = 0;
                    info.ToolErrors[toolName] = errCount + 1;
                }

                // 全局工具调用计数
                if (!globalToolCalls.TryGetValue(toolName, out int gc))
                    gc = 0;
                globalToolCalls[toolName] = gc + 1;
                if (!success)
                {
                    if (!globalToolErrors.TryGetValue(toolName, out int ge))
                        ge = 0;
                    globalToolErrors[toolName] = ge + 1;
                }
            }
        }

        // ================================================================
        // Knowledge Base
        // ================================================================

        /// <summary>
        /// 记录一次知识库词条查询结果。
        /// hitLayer: 0=L1, 1=L2, ..., -1=miss
        /// </summary>
        public static void RecordKnowledgeLookup(string term, int hitLayer, string sessionId)
        {
            if (term == null) return;
            lock (_lock)
            {
                // 全局术语访问计数
                if (!globalTermAccess.TryGetValue(term, out var entry))
                {
                    entry = new TermAccessInfo();
                    globalTermAccess[term] = entry;
                }
                entry.TotalAccesses++;

                if (hitLayer >= 0)
                {
                    entry.HitCount++;
                    if (hitLayer == 0) entry.L1HitCount++;
                }

                // 按 session 的批量查询计数（每个 session 一次 batch 算一次 KB 访问）
                if (sessionId != null && _activeSessions.TryGetValue(sessionId, out var info))
                {
                    info.KbQueries++;
                    if (hitLayer >= 0)
                        info.KbHits++;
                }
            }
        }

        /// <summary>
        /// 获取全局知识库总批量查询次数（= 所有 session 的 KbQueries 之和）。
        /// </summary>
        public static int KbTotalBatches
        {
            get
            {
                lock (_lock)
                {
                    int total = 0;
                    foreach (var s in _completedSessions)
                        total += s.KbQueries > 0 ? 1 : 0;
                    foreach (var kv in _activeSessions)
                    {
                        if (kv.Value.KbQueries > 0)
                            total++;
                    }
                    return total;
                }
            }
        }

        // ================================================================
        // Agent Loop
        // ================================================================

        /// <summary>
        /// 记录一轮 Agent 循环完成。
        /// </summary>
        public static void RecordLoopFinished(int rounds, int eventsProcessed, bool normalCompletion,
            AgentRole role)
        {
            lock (_lock)
            {
                roleActivations[(int)role]++;
                roleTotalRounds[(int)role] += rounds;
                roleTotalEvents[(int)role] += eventsProcessed;
                if (!normalCompletion)
                    roleErrors[(int)role]++;
            }
        }

        // ================================================================
        // Workspace
        // ================================================================

        /// <summary>
        /// 记录工作空间操作。
        /// </summary>
        public static void RecordWorkspaceOperation(string operation)
        {
            lock (_lock)
            {
                if (!workspaceOps.TryGetValue(operation, out int count))
                    count = 0;
                workspaceOps[operation] = count + 1;
            }
        }

        // ================================================================
        // 聚合查询
        // ================================================================

        /// <summary>
        /// 获取当前度量快照。
        /// </summary>
        public static MetricsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                var snap = new MetricsSnapshot();

                // Session
                snap.TotalSessions = _completedSessions.Count + _activeSessions.Count;
                snap.ActiveSessions = _activeSessions.Count;

                // Token per role
                snap.Tokens = new MetricsSnapshot.TokenBreakdown
                {
                    InputByRole = new Dictionary<string, int>
                    {
                        ["director"] = roleTotalInput[(int)AgentRole.Director],
                        ["screenwriter"] = roleTotalInput[(int)AgentRole.Screenwriter],
                        ["freelancer"] = roleTotalInput[(int)AgentRole.Freelancer]
                    },
                    OutputByRole = new Dictionary<string, int>
                    {
                        ["director"] = roleTotalOutput[(int)AgentRole.Director],
                        ["screenwriter"] = roleTotalOutput[(int)AgentRole.Screenwriter],
                        ["freelancer"] = roleTotalOutput[(int)AgentRole.Freelancer]
                    },
                    CacheReadByRole = new Dictionary<string, int>
                    {
                        ["director"] = roleTotalCacheRead[(int)AgentRole.Director],
                        ["screenwriter"] = roleTotalCacheRead[(int)AgentRole.Screenwriter],
                        ["freelancer"] = roleTotalCacheRead[(int)AgentRole.Freelancer]
                    },
                    LlmCallsByRole = new Dictionary<string, int>
                    {
                        ["director"] = roleLlmCalls[(int)AgentRole.Director],
                        ["screenwriter"] = roleLlmCalls[(int)AgentRole.Screenwriter],
                        ["freelancer"] = roleLlmCalls[(int)AgentRole.Freelancer]
                    }
                };
                snap.Tokens.TotalInput = roleTotalInput.Sum();
                snap.Tokens.TotalOutput = roleTotalOutput.Sum();
                snap.Tokens.TotalCacheRead = roleTotalCacheRead.Sum();

                // Tools
                snap.Tools = new List<MetricsSnapshot.ToolStat>();
                foreach (var kv in globalToolCalls.OrderByDescending(kv => kv.Value))
                {
                    globalToolErrors.TryGetValue(kv.Key, out int errors);
                    snap.Tools.Add(new MetricsSnapshot.ToolStat
                    {
                        Name = kv.Key,
                        Calls = kv.Value,
                        Errors = errors
                    });
                }

                // Knowledge Base
                int totalBatches = KbTotalBatches;
                snap.Knowledge = new MetricsSnapshot.KnowledgeStat
                {
                    TotalBatches = totalBatches,
                    Terms = new List<MetricsSnapshot.TermStat>()
                };
                foreach (var kv in globalTermAccess.OrderByDescending(kv => kv.Value.TotalAccesses))
                {
                    snap.Knowledge.Terms.Add(new MetricsSnapshot.TermStat
                    {
                        Term = kv.Key,
                        TotalAccesses = kv.Value.TotalAccesses,
                        HitCount = kv.Value.HitCount,
                        L1HitCount = kv.Value.L1HitCount,
                        AccessRate = totalBatches > 0
                            ? (double)kv.Value.TotalAccesses / totalBatches
                            : 0
                    });
                }

                // Agent Loop
                int totalActivations = roleActivations[(int)AgentRole.Director]
                    + roleActivations[(int)AgentRole.Screenwriter]
                    + roleActivations[(int)AgentRole.Freelancer];
                snap.AgentLoops = new MetricsSnapshot.AgentLoopStat
                {
                    ActivationsByRole = new Dictionary<string, int>
                    {
                        ["director"] = roleActivations[(int)AgentRole.Director],
                        ["screenwriter"] = roleActivations[(int)AgentRole.Screenwriter],
                        ["freelancer"] = roleActivations[(int)AgentRole.Freelancer]
                    },
                    TotalRounds = roleTotalRounds.Sum(),
                    AvgRoundsPerActivation = totalActivations > 0
                        ? (double)roleTotalRounds.Sum() / totalActivations
                        : 0,
                    TotalEventsProcessed = roleTotalEvents.Sum(),
                    TotalErrors = roleErrors.Sum()
                };

                // Workspace
                snap.WorkspaceOperations = new Dictionary<string, int>(workspaceOps);

                // Top sessions
                snap.RecentSessions = _completedSessions
                    .OrderByDescending(s => s.StartTime)
                    .Take(20)
                    .Select(s => new MetricsSnapshot.SessionSummary
                    {
                        SessionId = s.SessionId,
                        Role = s.Role.ToString().ToLowerInvariant(),
                        Rounds = s.ToolCalls.Values.Sum(),
                        InputTokens = s.TotalInputTokens,
                        OutputTokens = s.TotalOutputTokens,
                        ToolCalls = s.ToolCalls,
                        KbQueries = s.KbQueries,
                        KbHits = s.KbHits,
                        DurationMs = s.DurationMs
                    })
                    .ToList();

                return snap;
            }
        }

        /// <summary>
        /// 重置所有度量，保留配置不变的计数器归零。
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _activeSessions.Clear();
                _completedSessions.Clear();
                globalToolCalls.Clear();
                globalToolErrors.Clear();
                globalTermAccess.Clear();
                workspaceOps.Clear();
                Array.Clear(roleTotalInput, 0, 3);
                Array.Clear(roleTotalOutput, 0, 3);
                Array.Clear(roleTotalCacheRead, 0, 3);
                Array.Clear(roleLlmCalls, 0, 3);
                Array.Clear(roleActivations, 0, 3);
                Array.Clear(roleTotalRounds, 0, 3);
                Array.Clear(roleTotalEvents, 0, 3);
                Array.Clear(roleErrors, 0, 3);
            }
        }

        // ================================================================
        // 内部状态
        // ================================================================

        private class SessionInfo
        {
            public string SessionId;
            public AgentRole Role;
            public DateTime StartTime;
            public DateTime EndTime;
            public int TotalInputTokens;
            public int TotalOutputTokens;
            public int TotalCacheReadTokens;
            public string LastModel;
            public readonly Dictionary<string, int> ToolCalls = new Dictionary<string, int>();
            public readonly Dictionary<string, int> ToolErrors = new Dictionary<string, int>();
            public int KbQueries;   // 批量查询次数（一次 BuildUserMessage 算一次）
            public int KbHits;      // 命中次数

            public long DurationMs =>
                (long)((EndTime - StartTime).TotalMilliseconds);
        }

        private class TermAccessInfo
        {
            public int TotalAccesses;   // 该 term 在批量查询中出现的次数
            public int HitCount;        // 命中次数（任意层）
            public int L1HitCount;      // L1 命中次数
        }

        // 按角色分桶
        private static readonly int[] roleTotalInput = new int[3];
        private static readonly int[] roleTotalOutput = new int[3];
        private static readonly int[] roleTotalCacheRead = new int[3];
        private static readonly int[] roleLlmCalls = new int[3];
        private static readonly int[] roleActivations = new int[3];
        private static readonly int[] roleTotalRounds = new int[3];
        private static readonly int[] roleTotalEvents = new int[3];
        private static readonly int[] roleErrors = new int[3];

        // 全局工具计数
        private static readonly Dictionary<string, int> globalToolCalls
            = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> globalToolErrors
            = new Dictionary<string, int>();

        // 全局术语访问
        private static readonly Dictionary<string, TermAccessInfo> globalTermAccess
            = new Dictionary<string, TermAccessInfo>(StringComparer.OrdinalIgnoreCase);

        // 工作空间操作计数
        private static readonly Dictionary<string, int> workspaceOps
            = new Dictionary<string, int>();
    }

    /// <summary>
    /// 度量快照。只读视图，用于序列化查询。
    /// </summary>
    public class MetricsSnapshot
    {
        public int TotalSessions;
        public int ActiveSessions;

        public TokenBreakdown Tokens;
        public List<ToolStat> Tools;
        public KnowledgeStat Knowledge;
        public AgentLoopStat AgentLoops;
        public Dictionary<string, int> WorkspaceOperations;
        public List<SessionSummary> RecentSessions;

        public class TokenBreakdown
        {
            public int TotalInput;
            public int TotalOutput;
            public int TotalCacheRead;
            public Dictionary<string, int> InputByRole;
            public Dictionary<string, int> OutputByRole;
            public Dictionary<string, int> CacheReadByRole;
            public Dictionary<string, int> LlmCallsByRole;
        }

        public class ToolStat
        {
            public string Name;
            public int Calls;
            public int Errors;
        }

        public class KnowledgeStat
        {
            public int TotalBatches;
            public List<TermStat> Terms;
        }

        public class TermStat
        {
            public string Term;
            public int TotalAccesses;
            public int HitCount;
            public int L1HitCount;
            public double AccessRate;
        }

        public class AgentLoopStat
        {
            public Dictionary<string, int> ActivationsByRole;
            public int TotalRounds;
            public double AvgRoundsPerActivation;
            public int TotalActivations
            {
                get
                {
                    int sum = 0;
                    if (ActivationsByRole != null)
                        foreach (var v in ActivationsByRole.Values)
                            sum += v;
                    return sum;
                }
            }
            public int TotalEventsProcessed;
            public int TotalErrors;
        }

        public class SessionSummary
        {
            public string SessionId;
            public string Role;
            public int Rounds;
            public int InputTokens;
            public int OutputTokens;
            public Dictionary<string, int> ToolCalls;
            public int KbQueries;
            public int KbHits;
            public long DurationMs;
        }

        /// <summary>
        /// 序列化为 JSON 字符串。
        /// </summary>
        public string ToJson()
        {
            var w = new JsonWriter(2048);

            w.Prop("totalSessions", TotalSessions);
            w.Prop("activeSessions", ActiveSessions);

            // Tokens
            if (Tokens != null)
            {
                var tw = new JsonWriter(512);
                tw.Prop("totalInput", Tokens.TotalInput);
                tw.Prop("totalOutput", Tokens.TotalOutput);
                tw.Prop("totalCacheRead", Tokens.TotalCacheRead);
                if (Tokens.InputByRole != null)
                {
                    var rw = new JsonWriter(128);
                    foreach (var kv in Tokens.InputByRole)
                        rw.Prop(kv.Key, kv.Value);
                    tw.PropRaw("inputByRole", rw.Close());
                }
                if (Tokens.OutputByRole != null)
                {
                    var rw = new JsonWriter(128);
                    foreach (var kv in Tokens.OutputByRole)
                        rw.Prop(kv.Key, kv.Value);
                    tw.PropRaw("outputByRole", rw.Close());
                }
                if (Tokens.CacheReadByRole != null)
                {
                    var rw = new JsonWriter(128);
                    foreach (var kv in Tokens.CacheReadByRole)
                        rw.Prop(kv.Key, kv.Value);
                    tw.PropRaw("cacheReadByRole", rw.Close());
                }
                if (Tokens.LlmCallsByRole != null)
                {
                    var rw = new JsonWriter(128);
                    foreach (var kv in Tokens.LlmCallsByRole)
                        rw.Prop(kv.Key, kv.Value);
                    tw.PropRaw("llmCallsByRole", rw.Close());
                }
                w.PropRaw("tokens", tw.Close());
            }

            // Tools
            if (Tools != null)
            {
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < Tools.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var tw = new JsonWriter(128);
                    tw.Prop("name", Tools[i].Name ?? "");
                    tw.Prop("calls", Tools[i].Calls);
                    tw.Prop("errors", Tools[i].Errors);
                    sb.Append(tw.Close());
                }
                sb.Append(']');
                w.PropRaw("tools", sb.ToString());
            }

            // Knowledge
            if (Knowledge != null)
            {
                var kw = new JsonWriter(512);
                kw.Prop("totalBatches", Knowledge.TotalBatches);
                if (Knowledge.Terms != null)
                {
                    var sb = new System.Text.StringBuilder("[");
                    for (int i = 0; i < Knowledge.Terms.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var tw = new JsonWriter(128);
                        tw.Prop("term", Knowledge.Terms[i].Term ?? "");
                        tw.Prop("accesses", Knowledge.Terms[i].TotalAccesses);
                        tw.Prop("hits", Knowledge.Terms[i].HitCount);
                        tw.Prop("l1Hits", Knowledge.Terms[i].L1HitCount);
                        tw.Prop("accessRate", Knowledge.Terms[i].AccessRate);
                        sb.Append(tw.Close());
                    }
                    sb.Append(']');
                    kw.PropRaw("terms", sb.ToString());
                }
                w.PropRaw("knowledge", kw.Close());
            }

            // Agent Loops
            if (AgentLoops != null)
            {
                var aw = new JsonWriter(256);
                aw.Prop("totalActivations", AgentLoops.TotalActivations);
                aw.Prop("totalRounds", AgentLoops.TotalRounds);
                aw.Prop("avgRoundsPerActivation", AgentLoops.AvgRoundsPerActivation);
                aw.Prop("totalEventsProcessed", AgentLoops.TotalEventsProcessed);
                aw.Prop("totalErrors", AgentLoops.TotalErrors);
                if (AgentLoops.ActivationsByRole != null)
                {
                    var rw = new JsonWriter(128);
                    foreach (var kv in AgentLoops.ActivationsByRole)
                        rw.Prop(kv.Key, kv.Value);
                    aw.PropRaw("activationsByRole", rw.Close());
                }
                w.PropRaw("agentLoops", aw.Close());
            }

            // Workspace operations
            if (WorkspaceOperations != null && WorkspaceOperations.Count > 0)
            {
                var ow = new JsonWriter(128);
                foreach (var kv in WorkspaceOperations)
                    ow.Prop(kv.Key, kv.Value);
                w.PropRaw("workspaceOperations", ow.Close());
            }

            return w.Close();
        }
    }
}
