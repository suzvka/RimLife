using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Framework
{
    /// <summary>
    /// 主线程任务调度器。零外部依赖。
    /// 从任意线程 Enqueue action，由主线程周期调用 DrainQueue 执行。
    /// 日志接口需由宿主层注入（如 RimWorld 适配层设置 Logger）。
    /// </summary>
    public static class MainThreadDispatcher
    {
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();
        private static readonly object _queueLock = new object();

        private static int _mainThreadId = -1;
        private static volatile bool _isDraining;
        private const int MaxQueueSize = 5000;

        // ================================================================
        // 可注入日志接口（由宿主层设置）
        // ================================================================

        /// <summary>日志接口。由宿主层注入，未设置时静默忽略。</summary>
        public static ILogger Logger;

        private static void LogWarning(string message)
        {
            Logger?.Warning(message);
        }

        private static void LogError(string message)
        {
            Logger?.Error(message);
        }

        // ================================================================
        // 公共 API
        // ================================================================

        /// <summary>
        /// 将 action 加入主线程执行队列。
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queueLock)
            {
                if (_executionQueue.Count >= MaxQueueSize)
                {
                    LogWarning($"[MainThreadDispatcher] Queue size {_executionQueue.Count} exceeded {MaxQueueSize}. Action may be delayed.");
                }
                _executionQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// 在主线程上执行所有待处理 action。必须从主线程调用。
        /// </summary>
        public static void DrainQueue()
        {
            int currentThread = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == -1)
            {
                _mainThreadId = currentThread;
            }
            else if (_mainThreadId != currentThread)
            {
                LogError("[MainThreadDispatcher] DrainQueue called from non-main thread. Ignored.");
                return;
            }

            if (_isDraining) return;

            List<Action> workItems = null;
            lock (_queueLock)
            {
                if (_executionQueue.Count == 0) return;
                workItems = new List<Action>(_executionQueue.Count);
                while (_executionQueue.Count > 0)
                {
                    workItems.Add(_executionQueue.Dequeue());
                }
            }

            _isDraining = true;
            try
            {
                for (int i = 0; i < workItems.Count; i++)
                {
                    var action = workItems[i];
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        LogError($"[MainThreadDispatcher] Error executing action: {e}");
                    }
                }
            }
            finally
            {
                _isDraining = false;
            }
        }

        /// <summary>
        /// 将 func 加入主线程执行队列，返回 Task 等待结果。
        /// 已在主线程且非 draining 状态时同步执行。
        /// </summary>
        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            if (func == null) return Task.FromException<T>(new ArgumentNullException(nameof(func)));

            if (_mainThreadId != -1 && Thread.CurrentThread.ManagedThreadId == _mainThreadId && !_isDraining)
            {
                try
                {
                    T result = func();
                    return Task.FromResult(result);
                }
                catch (Exception e)
                {
                    return Task.FromException<T>(e);
                }
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
            return tcs.Task;
        }
    }
}
