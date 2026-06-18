using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NPCLife.Framework
{
    /// <summary>
    /// 通用事件总线。提供发布/订阅能力，支持命名空间事件名、错误隔离、优先级排序。
    /// 纯静态，零 RimWorld 依赖。
    ///
    /// 使用示例：
    ///   var unsub = EventBus.Subscribe("agent.activated", args => { ... });
    ///   EventBus.Publish("agent.activated", new EventArgs { Payload = ... });
    ///   unsub();  // 取消订阅
    /// </summary>
    public static class EventBus
    {
        private struct Subscription
        {
            public Action<EventArg> Handler;
            public int Priority;
        }

        private static readonly Dictionary<string, List<Subscription>> _subscribers =
            new Dictionary<string, List<Subscription>>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();
        private static long _seq;

        /// <summary>日志接口。由宿主层注入，未设置时静默忽略。</summary>
        public static ILogger Logger;

        // ================================================================
        // 公共 API
        // ================================================================

        /// <summary>
        /// 订阅事件。返回的 Action 调用即可取消订阅（等价于 off）。
        /// priority 越小越先执行（0 为默认优先级）。
        /// </summary>
        /// <param name="eventName">事件名（点分命名空间，如 "agent.activated"）。</param>
        /// <param name="handler">事件处理器。</param>
        /// <param name="priority">执行优先级，数字越小越先执行。</param>
        /// <returns>取消订阅的 Action，调用即取消。</returns>
        public static Action Subscribe(string eventName, Action<EventArg> handler, int priority = 0)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return () => { };

            lock (_lock)
            {
                if (!_subscribers.TryGetValue(eventName, out var list))
                {
                    list = new List<Subscription>();
                    _subscribers[eventName] = list;
                }

                var sub = new Subscription { Handler = handler, Priority = priority };
                list.Add(sub);
                // 按优先级排序（稳定排序，同优先级按添加顺序）
                list.Sort((a, b) => a.Priority.CompareTo(b.Priority));

                return () => Unsubscribe(eventName, handler);
            }
        }

        /// <summary>
        /// 取消订阅。通常使用 Subscribe 返回的 Action 更方便。
        /// </summary>
        public static void Unsubscribe(string eventName, Action<EventArg> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;

            lock (_lock)
            {
                if (!_subscribers.TryGetValue(eventName, out var list)) return;
                list.RemoveAll(s => s.Handler == handler);
            }
        }

        /// <summary>
        /// 发布事件。错误隔离：一个 handler 抛异常不阻断其他。
        /// </summary>
        /// <param name="eventName">事件名。</param>
        /// <param name="args">事件参数（可为 null，框架自动填充 EventName 和 Timestamp）。</param>
        public static void Publish(string eventName, EventArg args = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            List<Subscription> snapshot;
            lock (_lock)
            {
                if (!_subscribers.TryGetValue(eventName, out var list) || list.Count == 0) return;
                snapshot = new List<Subscription>(list);
            }

            // 自动填充基类字段
            if (args == null) args = new EventArg();
            args.EventName = eventName;
            args.Timestamp = Interlocked.Increment(ref _seq);

            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i].Handler(args);
                }
                catch (Exception ex)
                {
                    Logger?.Warning($"[EventBus] Handler error on '{eventName}': {ex.Message}");
                }
            }
        }

        /// <summary>清除指定事件的所有订阅。</summary>
        public static void Clear(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            lock (_lock)
            {
                _subscribers.Remove(eventName);
            }
        }

        /// <summary>清除全部订阅。</summary>
        public static void ClearAll()
        {
            lock (_lock)
            {
                _subscribers.Clear();
            }
        }

        /// <summary>获取指定事件的订阅数（调试用）。</summary>
        public static int SubscriberCount(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return 0;
            lock (_lock)
            {
                return _subscribers.TryGetValue(eventName, out var list) ? list.Count : 0;
            }
        }

        /// <summary>获取已订阅的事件名列表（调试用）。</summary>
        public static IReadOnlyList<string> SubscribedEvents
        {
            get
            {
                lock (_lock)
                {
                    return _subscribers.Keys.ToList();
                }
            }
        }
    }

    /// <summary>
    /// 事件参数基类。Payload 提供松结构扩展。
    /// 命名为 EventArg（而非 EventArgs）以避免与 System.EventArgs 冲突。
    /// </summary>
    public class EventArg
    {
        /// <summary>事件名（由 Publish 自动填充）。</summary>
        public string EventName;

        /// <summary>发布时的全局序列号（由 Publish 自动填充，非游戏 tick）。</summary>
        public long Timestamp;

        /// <summary>松结构扩展参数。</summary>
        public Dictionary<string, string> Payload;

        /// <summary>创建带 Payload 的事件参数。</summary>
        public static EventArg WithPayload(params (string key, string value)[] entries)
        {
            var payload = new Dictionary<string, string>();
            foreach (var (key, value) in entries)
                payload[key] = value;
            return new EventArg { Payload = payload };
        }
    }

    /// <summary>
    /// 预定义框架事件名常量。
    /// 命名空间约定：模块.动作（如 agent.activated, workspace.created）。
    /// </summary>
    public static class FrameworkEvents
    {
        // ---- 生命周期 ----
        /// <summary>框架初始化完成。</summary>
        public const string Initialized = "framework.initialized";
        /// <summary>框架已销毁。</summary>
        public const string Disposed = "framework.disposed";
        /// <summary>配置已就绪。</summary>
        public const string ConfigReady = "framework.config_ready";

        // ---- 存档切换 ----
        /// <summary>新存档已加载。</summary>
        public const string SaveLoaded = "save.loaded";
        /// <summary>存档已卸载（切换前）。</summary>
        public const string SaveUnloaded = "save.unloaded";

        // ---- Agent ----
        /// <summary>Agent 循环已激活（事件池阈值达到）。</summary>
        public const string AgentActivated = "agent.activated";
        /// <summary>Agent 完成一轮工具调用。</summary>
        public const string AgentRoundComplete = "agent.round_complete";
        /// <summary>Agent 循环结束（所有轮次完成或异常终止）。</summary>
        public const string AgentLoopFinished = "agent.loop_finished";

        // ---- 工具调用 ----
        /// <summary>MCP 工具即将被调用。</summary>
        public const string ToolInvoking = "tool.invoking";
        /// <summary>MCP 工具已调用完成。</summary>
        public const string ToolInvoked = "tool.invoked";

        // ---- LLM ----
        /// <summary>LLM 请求已发送。</summary>
        public const string LlmRequestSent = "llm.request_sent";
        /// <summary>LLM 响应已接收。</summary>
        public const string LlmResponseReceived = "llm.response_received";
        /// <summary>LLM 调用出错。</summary>
        public const string LlmError = "llm.error";

        // ---- 工作空间 ----
        /// <summary>工作空间已创建。</summary>
        public const string WorkspaceCreated = "workspace.created";
        /// <summary>工作空间已关闭/废弃。</summary>
        public const string WorkspaceClosed = "workspace.closed";
        /// <summary>工作空间已更新（回合推送、信号上报、状态变更、分支/合并等）。</summary>
        public const string WorkspaceUpdated = "workspace.updated";

        // ---- 台词 ----
        /// <summary>单句台词已就绪，ScriptDeliveryService 将逐行推送到游戏侧。</summary>
        public const string ScriptLineReady = "script.line_ready";
        /// <summary>台词轮次完成（finish_round 调用后）。</summary>
        public const string ScriptReady = "script.ready";

        // ---- 记忆 ----
        /// <summary>记忆巩固完成。</summary>
        public const string MemoryConsolidated = "memory.consolidated";
    }
}
