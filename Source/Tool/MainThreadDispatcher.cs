using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Schedules actions to execute on the main game thread. Public API unchanged.
    /// </summary>
    public static class MainThreadDispatcher
    {
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();
        private static readonly object _queueLock = new object();

        // Main thread tracking & drain state
        private static int _mainThreadId = -1; // set on first DrainQueue call
        private static volatile bool _isDraining;
        private const int MaxQueueSize = 5000; // soft limit

        /// <summary>
        /// Enqueues an action to be executed on the main thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queueLock)
            {
                if (_executionQueue.Count >= MaxQueueSize)
                {
                    Log.Warning($"[MainThreadDispatcher] Queue size {_executionQueue.Count} exceeded {MaxQueueSize}. Action may be delayed.");
                }
                _executionQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// Executes all pending actions. Must be called from the main thread update loop.
        /// </summary>
        public static void DrainQueue()
        {
            int currentThread = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == -1)
            {
                _mainThreadId = currentThread; // first invocation establishes main thread
            }
            else if (_mainThreadId != currentThread)
            {
                Log.Error("[MainThreadDispatcher] DrainQueue called from non-main thread. Ignored.");
                return;
            }

            if (_isDraining) return; // prevent re-entrancy

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
                        Log.Error($"[MainThreadDispatcher] Error executing action: {e}");
                    }
                }
            }
            finally
            {
                _isDraining = false;
            }
        }

        /// <summary>
        /// Enqueues a function to execute on the main thread and returns a Task for the result.
        /// Executes inline if already on main thread and not draining.
        /// </summary>
        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            if (func == null) return Task.FromException<T>(new ArgumentNullException(nameof(func)));

            // Fast path: already on main thread and not currently draining => execute immediately.
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
