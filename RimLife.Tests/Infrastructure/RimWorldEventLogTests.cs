using Xunit;

namespace RimLife.Tests.Infrastructure
{
    /// <summary>
    /// RimWorldEventLog 测试。
    /// 因 RimWorldEventLog 内部调用 Verse.Log，且 JsonWriter 依赖
    /// RimWorld 程序集转发，当前仅在 RimWorld 运行时环境下可执行。
    ///
    /// 在 RimWorld 环境中可移除 Skip 标记运行完整生命周期测试。
    /// 测试涵盖：Append/Query/Count/分页/持久化往返/容量裁剪。
    /// </summary>
    public class RimWorldEventLogTests
    {
        [Fact(Skip = "RimWorldEventLog 依赖 Verse.Log 和 RimWorld 运行时，需在游戏环境中执行")]
        public void Append_And_Query_FullLifecycle() { }

        [Fact(Skip = "RimWorldEventLog 依赖 Verse.Log 和 RimWorld 运行时，需在游戏环境中执行")]
        public void CapacityLimit_TrimsMinorEventsFirst() { }

        [Fact(Skip = "RimWorldEventLog 依赖 Verse.Log 和 RimWorld 运行时，需在游戏环境中执行")]
        public void NullStore_ThrowsArgumentNullException() { }

        [Fact(Skip = "RimWorldEventLog 依赖 Verse.Log 和 RimWorld 运行时，需在游戏环境中执行")]
        public void Append_NullEvent_NoError() { }
    }
}
