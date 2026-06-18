using System;
using System.Collections.Generic;
using System.Threading;

namespace NPCLife.Framework
{
    /// <summary>
    /// 全局错误处理器。提供统一错误钩子、诊断模式和请求链路追踪。
    /// 纯静态，零 RimWorld 依赖。
    ///
    /// 使用方式：
    ///   // 游戏侧注册全局错误处理器
    ///   ErrorHandler.OnError(ctx => { /* 上报、降级、记录 */ });
    ///
    ///   // 框架内部报告错误
    ///   ErrorHandler.ReportError("AgentLoop", exception);
    ///
    ///   // 请求链路追踪
    ///   var traceId = ErrorHandler.BeginTrace();
    ///   try { ... } finally { ErrorHandler.EndTrace(); }
    /// </summary>
    public static class ErrorHandler
    {
        private static readonly List<Action<ErrorContext>> _handlers = new List<Action<ErrorContext>>();
        private static readonly object _lock = new object();
        private static long _traceSeq;

        [ThreadStatic]
        private static string _currentTraceId;

        /// <summary>日志接口。由宿主层注入，未设置时静默忽略。</summary>
        public static ILogger Logger;

        // ================================================================
        // 诊断模式
        // ================================================================

        /// <summary>
        /// 诊断模式开关。启用后 ReportError 会输出更详细的上下文信息。
        /// 通常由 FrameworkConfig.Diagnostics.EnableVerboseLogging 驱动。
        /// </summary>
        public static bool DiagnosticMode { get; set; }

        // ================================================================
        // 请求链路追踪
        // ================================================================

        /// <summary>当前请求链路 ID（ThreadStatic，每个线程独立）。</summary>
        public static string CurrentTraceId => _currentTraceId;

        /// <summary>
        /// 开始新的请求链路。返回分配的 TraceId。
        /// 通常在 Agent 循环入口处调用。
        /// </summary>
        public static string BeginTrace()
        {
            _currentTraceId = $"trace-{Interlocked.Increment(ref _traceSeq)}";
            if (DiagnosticMode)
                Logger?.Message($"[ErrorHandler] Begin trace: {_currentTraceId}");
            return _currentTraceId;
        }

        /// <summary>
        /// 结束当前请求链路。通常在 Agent 循环结束（Finish 或 Error）时调用。
        /// </summary>
        public static void EndTrace()
        {
            if (DiagnosticMode && _currentTraceId != null)
                Logger?.Message($"[ErrorHandler] End trace: {_currentTraceId}");
            _currentTraceId = null;
        }

        // ================================================================
        // 错误注册与报告
        // ================================================================

        /// <summary>
        /// 注册全局错误处理器。可多次调用叠加注册。
        /// 处理器按注册顺序调用，每个处理器的异常被隔离不影响其他。
        /// </summary>
        /// <param name="handler">错误处理器。</param>
        public static void OnError(Action<ErrorContext> handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                _handlers.Add(handler);
            }
        }

        /// <summary>
        /// 报告异常错误。自动关联当前 TraceId。
        /// </summary>
        /// <param name="source">来源模块名（如 "AgentLoop", "LlmAccessor", "McpTool"）。</param>
        /// <param name="ex">原始异常。</param>
        /// <param name="metadata">额外上下文元数据（可选）。</param>
        public static void ReportError(string source, Exception ex, Dictionary<string, string> metadata = null)
        {
            ReportError(source, ex?.Message ?? "unknown error", ex, metadata);
        }

        /// <summary>
        /// 报告文本错误。自动关联当前 TraceId。
        /// </summary>
        /// <param name="source">来源模块名。</param>
        /// <param name="message">错误描述。</param>
        /// <param name="metadata">额外上下文元数据（可选）。</param>
        public static void ReportError(string source, string message, Dictionary<string, string> metadata = null)
        {
            ReportError(source, message, null, metadata);
        }

        private static void ReportError(string source, string message, Exception ex, Dictionary<string, string> metadata)
        {
            var context = new ErrorContext
            {
                Source = source ?? "unknown",
                Message = message ?? "",
                Exception = ex,
                TraceId = _currentTraceId,
                Metadata = metadata,
                IsHandled = false
            };

            // 通过 Logger 输出
            if (DiagnosticMode)
            {
                string detail = $"[ErrorHandler] [{context.Source}] {context.Message}";
                if (context.TraceId != null) detail += $" (trace={context.TraceId})";
                Logger?.Warning(detail);
            }

            // 发布到事件总线（如果有）
            try
            {
                EventBus.Publish(FrameworkEvents.LlmError, EventArg.WithPayload(
                    ("source", context.Source),
                    ("message", context.Message),
                    ("traceId", context.TraceId ?? "")
                ));
            }
            catch { /* 事件总线异常不应影响错误报告 */ }

            // 调用所有已注册的处理器
            List<Action<ErrorContext>> snapshot;
            lock (_lock)
            {
                if (_handlers.Count == 0) return;
                snapshot = new List<Action<ErrorContext>>(_handlers);
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i](context);
                }
                catch (Exception handlerEx)
                {
                    Logger?.Warning($"[ErrorHandler] Handler error: {handlerEx.Message}");
                }
            }
        }

        /// <summary>
        /// 清除所有已注册的错误处理器。通常用于测试或 Shutdown。
        /// </summary>
        public static void ClearHandlers()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }

        /// <summary>获取已注册的处理器数量（调试用）。</summary>
        public static int HandlerCount
        {
            get { lock (_lock) { return _handlers.Count; } }
        }
    }

    /// <summary>
    /// 错误上下文。传递给全局错误处理器的完整信息载体。
    /// </summary>
    public class ErrorContext
    {
        /// <summary>来源模块名（如 "AgentLoop", "LlmAccessor", "McpTool"）。</summary>
        public string Source;

        /// <summary>错误描述。</summary>
        public string Message;

        /// <summary>原始异常（可能为 null，如文本错误）。</summary>
        public Exception Exception;

        /// <summary>关联的请求链路 ID（BeginTrace/EndTrace 管理）。</summary>
        public string TraceId;

        /// <summary>额外上下文元数据。</summary>
        public Dictionary<string, string> Metadata;

        /// <summary>是否已被标记为已处理。处理器可设 true 阻止后续默认行为。</summary>
        public bool IsHandled;
    }
}
