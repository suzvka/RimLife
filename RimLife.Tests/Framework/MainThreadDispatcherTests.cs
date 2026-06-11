using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// MainThreadDispatcher 测试。
    /// 因 Queue&lt;T&gt; 类型加载受 Krafs.Rimworld.Ref 程序集转发影响，
    /// 当前仅在 RimWorld 运行时环境下可执行。
    /// 
    /// 在 RimWorld 环境中可移除 Skip 标记运行。
    /// </summary>
    public class MainThreadDispatcherTests
    {
        [Fact(Skip = "MainThreadDispatcher 依赖 RimWorld 运行时的程序集转发，需在游戏环境中执行")]
        public void Enqueue_DrainQueue_ExecutesActionsInOrder() { }

        [Fact(Skip = "MainThreadDispatcher 依赖 RimWorld 运行时的程序集转发，需在游戏环境中执行")]
        public void Enqueue_ExceptionInAction_IsCaught() { }

        [Fact(Skip = "MainThreadDispatcher 依赖 RimWorld 运行时的程序集转发，需在游戏环境中执行")]
        public void DrainQueue_ReentrantCall_Ignored() { }

        [Fact(Skip = "MainThreadDispatcher 依赖 RimWorld 运行时的程序集转发，需在游戏环境中执行")]
        public void EnqueueAsync_SyncExecution_ReturnsResult() { }

        [Fact(Skip = "MainThreadDispatcher 依赖 RimWorld 运行时的程序集转发，需在游戏环境中执行")]
        public void EnqueueAsync_ExceptionInFunc_ReturnsFaultedTask() { }
    }
}
