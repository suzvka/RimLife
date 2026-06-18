using NPCLife.Cards;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Framework
{
    /// <summary>
    /// AgentLoop 请求管道中的拦截点。
    /// 实现此接口可在 Agent 循环的关键步骤前后注入行为。
    ///
    /// 拦截顺序（按优先级，priority 越小越先执行）：
    ///   OnBeforePrompt → OnBeforeLlm → OnBeforeToolCall → OnAfterToolCall → OnLoopFinished
    ///
    /// 默认无拦截器时零开销。
    /// </summary>
    public interface IAgentInterceptor
    {
        /// <summary>
        /// 事件 drain 后、prompt 构造前。可修改事件列表或注入额外上下文。
        /// </summary>
        void OnBeforePrompt(PromptContext ctx);

        /// <summary>
        /// prompt 构造后、LLM 请求前。可修改 LLM 请求（追加系统消息、修改 temperature 等）。
        /// </summary>
        void OnBeforeLlm(LlmContext ctx);

        /// <summary>
        /// LLM 响应后、工具调用前。可拦截或修改工具调用参数。
        /// 设置 ctx.Cancelled = true 可跳过本次工具调用。
        /// </summary>
        void OnBeforeToolCall(ToolCallContext ctx);

        /// <summary>
        /// 工具调用后。可审查或改写工具返回结果。
        /// </summary>
        void OnAfterToolCall(ToolCallContext ctx);

        /// <summary>
        /// Agent 循环结束。用于统计、审计、清理。
        /// </summary>
        void OnLoopFinished(LoopContext ctx);
    }

    /// <summary>
    /// 拦截器基类（空实现）。继承后只需覆盖感兴趣的方法。
    /// </summary>
    public abstract class AgentInterceptorBase : IAgentInterceptor
    {
        public virtual void OnBeforePrompt(PromptContext ctx) { }
        public virtual void OnBeforeLlm(LlmContext ctx) { }
        public virtual void OnBeforeToolCall(ToolCallContext ctx) { }
        public virtual void OnAfterToolCall(ToolCallContext ctx) { }
        public virtual void OnLoopFinished(LoopContext ctx) { }
    }

    // ================================================================
    // 上下文对象
    // ================================================================

    /// <summary>Prompt 构造上下文。拦截器可修改 UserMessage。</summary>
    public class PromptContext
    {
        /// <summary>从事件池 drain 出的事件列表。</summary>
        public IReadOnlyList<IGameEvent> Events;

        /// <summary>构造好的用户消息文本。拦截器可追加或修改。</summary>
        public string UserMessage;
    }

    /// <summary>LLM 请求上下文。拦截器可修改 Request 的 messages、tools 等。</summary>
    public class LlmContext
    {
        /// <summary>即将发送的 LLM 请求。</summary>
        public LlmRequest Request;
    }

    /// <summary>工具调用上下文。拦截器可审查参数、跳过调用、改写结果。</summary>
    public class ToolCallContext
    {
        /// <summary>工具名称。</summary>
        public string ToolName;

        /// <summary>工具参数（JSON 字符串）。</summary>
        public string Arguments;

        /// <summary>工具执行结果（JSON 字符串）。OnBeforeToolCall 时为 null。</summary>
        public string Result;

        /// <summary>
        /// 设为 true 可跳过本次工具调用（仅 OnBeforeToolCall 有效）。
        /// 跳过时 Result 保持 null。
        /// </summary>
        public bool Cancelled;
    }

    /// <summary>循环结束上下文。只读统计信息。</summary>
    public class LoopContext
    {
        /// <summary>总轮数（工具调用次数）。</summary>
        public int Rounds;

        /// <summary>处理的事件数。</summary>
        public int EventsProcessed;

        /// <summary>是否正常结束（false = 异常或达到最大轮数）。</summary>
        public bool NormalCompletion;
    }

    // ================================================================
    // 管道管理
    // ================================================================

    /// <summary>
    /// Agent 管道拦截器管理器。纯静态，零 RimWorld 依赖。
    /// 拦截器按优先级排序执行（priority 越小越先）。
    /// </summary>
    public static class AgentPipeline
    {
        private struct InterceptorEntry
        {
            public IAgentInterceptor Interceptor;
            public int Priority;
        }

        private static readonly List<InterceptorEntry> _interceptors = new List<InterceptorEntry>();
        private static readonly object _lock = new object();

        /// <summary>日志接口。由宿主层注入，未设置时静默忽略。</summary>
        public static ILogger Logger;

        /// <summary>
        /// 添加拦截器。priority 越小越先执行。
        /// </summary>
        public static void AddInterceptor(IAgentInterceptor interceptor, int priority = 0)
        {
            if (interceptor == null) return;
            lock (_lock)
            {
                _interceptors.Add(new InterceptorEntry { Interceptor = interceptor, Priority = priority });
                _interceptors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
        }

        /// <summary>移除拦截器。</summary>
        public static void RemoveInterceptor(IAgentInterceptor interceptor)
        {
            if (interceptor == null) return;
            lock (_lock)
            {
                _interceptors.RemoveAll(e => e.Interceptor == interceptor);
            }
        }

        /// <summary>获取当前拦截器列表（只读快照）。</summary>
        public static IReadOnlyList<IAgentInterceptor> Interceptors
        {
            get
            {
                lock (_lock)
                {
                    return _interceptors.Select(e => e.Interceptor).ToList();
                }
            }
        }

        /// <summary>清除所有拦截器。</summary>
        public static void ClearInterceptors()
        {
            lock (_lock) { _interceptors.Clear(); }
        }

        // ================================================================
        // 管道执行（由 AgentLoop 内部调用）
        // ================================================================

        /// <summary>执行 OnBeforePrompt 链。</summary>
        internal static void RunBeforePrompt(PromptContext ctx)
        {
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].OnBeforePrompt(ctx); }
                catch (Exception ex) { Logger?.Warning($"[AgentPipeline] OnBeforePrompt error: {ex.Message}"); }
            }
        }

        /// <summary>执行 OnBeforeLlm 链。</summary>
        internal static void RunBeforeLlm(LlmContext ctx)
        {
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].OnBeforeLlm(ctx); }
                catch (Exception ex) { Logger?.Warning($"[AgentPipeline] OnBeforeLlm error: {ex.Message}"); }
            }
        }

        /// <summary>执行 OnBeforeToolCall 链。</summary>
        internal static void RunBeforeToolCall(ToolCallContext ctx)
        {
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i].OnBeforeToolCall(ctx);
                    if (ctx.Cancelled) break; // 任一拦截器取消则终止链
                }
                catch (Exception ex) { Logger?.Warning($"[AgentPipeline] OnBeforeToolCall error: {ex.Message}"); }
            }
        }

        /// <summary>执行 OnAfterToolCall 链。</summary>
        internal static void RunAfterToolCall(ToolCallContext ctx)
        {
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].OnAfterToolCall(ctx); }
                catch (Exception ex) { Logger?.Warning($"[AgentPipeline] OnAfterToolCall error: {ex.Message}"); }
            }
        }

        /// <summary>执行 OnLoopFinished 链。</summary>
        internal static void RunLoopFinished(LoopContext ctx)
        {
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].OnLoopFinished(ctx); }
                catch (Exception ex) { Logger?.Warning($"[AgentPipeline] OnLoopFinished error: {ex.Message}"); }
            }
        }

        private static List<IAgentInterceptor> Snapshot()
        {
            lock (_lock)
            {
                if (_interceptors.Count == 0) return new List<IAgentInterceptor>();
                return _interceptors.Select(e => e.Interceptor).ToList();
            }
        }
    }
}
