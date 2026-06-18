using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Framework
{
    /// <summary>
    /// 生命周期管理器。统一管理框架组件的初始化、销毁和生命周期钩子。
    /// 纯静态，零 RimWorld 依赖。
    ///
    /// 核心职责：
    /// 1. 注册/级联销毁 IDisposable 组件
    /// 2. 管理 OnInit / OnConfigReady / OnDestroy 钩子
    /// 3. 提供 Initialize / Shutdown / Reset 生命周期入口
    ///
    /// 使用流程：
    ///   1. RegisterDisposable("name", component) — 注册组件
    ///   2. LifecycleManager.Initialize() — 触发所有 OnInit 回调
    ///   3. LifecycleManager.NotifyConfigReady() — 触发所有 OnConfigReady 回调
    ///   4. LifecycleManager.Shutdown() — 逆序 Dispose + OnDestroy
    ///   5. LifecycleManager.Reset() — Shutdown + Initialize（存档切换）
    /// </summary>
    public static class LifecycleManager
    {
        private struct DisposableEntry
        {
            public string Name;
            public IDisposable Instance;
        }

        private struct HookEntry
        {
            public Action Callback;
            public int Priority;
        }

        private static readonly List<DisposableEntry> _disposables = new List<DisposableEntry>();
        private static readonly List<HookEntry> _initHooks = new List<HookEntry>();
        private static readonly List<HookEntry> _destroyHooks = new List<HookEntry>();
        private static readonly List<HookEntry> _configReadyHooks = new List<HookEntry>();
        private static readonly object _lock = new object();

        private static volatile bool _isInitialized;

        /// <summary>日志接口。由宿主层注入，未设置时静默忽略。</summary>
        public static ILogger Logger;

        // ================================================================
        // 状态查询
        // ================================================================

        /// <summary>框架是否已初始化。</summary>
        public static bool IsInitialized => _isInitialized;

        /// <summary>已注册的组件名列表（按注册顺序）。</summary>
        public static IReadOnlyList<string> RegisteredComponents
        {
            get
            {
                lock (_lock)
                {
                    return _disposables.Select(d => d.Name).ToList();
                }
            }
        }

        // ================================================================
        // 组件注册
        // ================================================================

        /// <summary>
        /// 注册可销毁组件。Shutdown 时按注册逆序 Dispose。
        /// 重复注册同名组件会替换旧实例。
        /// </summary>
        /// <param name="name">组件名称（用于调试和日志）。</param>
        /// <param name="disposable">可销毁实例。</param>
        public static void RegisterDisposable(string name, IDisposable disposable)
        {
            if (disposable == null) return;

            lock (_lock)
            {
                // 去重：同名替换
                for (int i = 0; i < _disposables.Count; i++)
                {
                    if (string.Equals(_disposables[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Dispose 旧实例
                        try { _disposables[i].Instance.Dispose(); }
                        catch (Exception ex) { Logger?.Warning($"[Lifecycle] Dispose old '{name}' failed: {ex.Message}"); }
                        _disposables[i] = new DisposableEntry { Name = name ?? "unnamed", Instance = disposable };
                        return;
                    }
                }
                _disposables.Add(new DisposableEntry { Name = name ?? "unnamed", Instance = disposable });
            }
        }

        // ================================================================
        // 钩子注册
        // ================================================================

        /// <summary>
        /// 注册初始化回调。在 Initialize() 时按优先级执行（priority 越小越先）。
        /// 若已初始化，立即执行。
        /// </summary>
        public static void OnInit(Action callback, int priority = 0)
        {
            if (callback == null) return;

            lock (_lock)
            {
                _initHooks.Add(new HookEntry { Callback = callback, Priority = priority });
                _initHooks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }

            // 若已初始化，立即执行
            if (_isInitialized)
            {
                try { callback(); }
                catch (Exception ex) { Logger?.Warning($"[Lifecycle] OnInit(late) failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// 注册销毁回调。在 Shutdown() 时按反优先级执行（priority 越大越先）。
        /// </summary>
        public static void OnDestroy(Action callback, int priority = 0)
        {
            if (callback == null) return;
            lock (_lock)
            {
                _destroyHooks.Add(new HookEntry { Callback = callback, Priority = priority });
                _destroyHooks.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        /// <summary>
        /// 注册配置就绪回调。在 NotifyConfigReady() 时按优先级执行。
        /// </summary>
        public static void OnConfigReady(Action callback, int priority = 0)
        {
            if (callback == null) return;
            lock (_lock)
            {
                _configReadyHooks.Add(new HookEntry { Callback = callback, Priority = priority });
                _configReadyHooks.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
        }

        // ================================================================
        // 生命周期触发
        // ================================================================

        /// <summary>
        /// 触发初始化。执行所有 OnInit 钩子并发布 framework.initialized 事件。
        /// 幂等：已初始化时不重复执行。
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                Logger?.Message("[Lifecycle] Initializing...");
                InvokeHooks(_initHooks, "OnInit");
                _isInitialized = true;
                EventBus.Publish(FrameworkEvents.Initialized);
                Logger?.Message($"[Lifecycle] Initialized. {_disposables.Count} components registered.");
            }
        }

        /// <summary>
        /// 通知配置就绪。执行所有 OnConfigReady 钩子并发布 framework.config_ready 事件。
        /// </summary>
        public static void NotifyConfigReady()
        {
            lock (_lock)
            {
                Logger?.Message("[Lifecycle] Config ready.");
                InvokeHooks(_configReadyHooks, "OnConfigReady");
                EventBus.Publish(FrameworkEvents.ConfigReady);
            }
        }

        /// <summary>
        /// 级联销毁所有已注册组件（逆序）+ 触发 OnDestroy 钩子 + 发布 framework.disposed 事件。
        /// 幂等：已关闭时不重复执行。
        /// </summary>
        public static void Shutdown()
        {
            if (!_isInitialized) return;

            lock (_lock)
            {
                if (!_isInitialized) return;

                Logger?.Message($"[Lifecycle] Shutting down... {_disposables.Count} components to dispose.");

                // 1. 触发 OnDestroy 钩子
                InvokeHooks(_destroyHooks, "OnDestroy");

                // 2. 逆序 Dispose 所有组件
                for (int i = _disposables.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _disposables[i].Instance.Dispose();
                        Logger?.Message($"[Lifecycle] Disposed '{_disposables[i].Name}'.");
                    }
                    catch (Exception ex)
                    {
                        Logger?.Warning($"[Lifecycle] Dispose '{_disposables[i].Name}' failed: {ex.Message}");
                    }
                }
                _disposables.Clear();

                // 3. 清除事件总线订阅
                EventBus.ClearAll();

                // 4. 发布销毁事件（在 ClearAll 之后，此事件不会被已销毁的组件接收）
                _isInitialized = false;
                EventBus.Publish(FrameworkEvents.Disposed);

                Logger?.Message("[Lifecycle] Shutdown complete.");
            }
        }

        /// <summary>
        /// 重置：先 Shutdown 再 Initialize。用于存档切换。
        /// </summary>
        public static void Reset()
        {
            EventBus.Publish(FrameworkEvents.SaveUnloaded);
            Shutdown();
            Initialize();
            EventBus.Publish(FrameworkEvents.SaveLoaded);
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        private static void InvokeHooks(List<HookEntry> hooks, string hookName)
        {
            // 拷贝快照，避免回调中修改列表导致迭代异常
            var snapshot = new List<HookEntry>(hooks);
            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i].Callback();
                }
                catch (Exception ex)
                {
                    Logger?.Warning($"[Lifecycle] {hookName} hook failed: {ex.Message}");
                }
            }
        }
    }
}
