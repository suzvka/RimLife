using RimLife.Cards;
using RimLife.Core;
using RimLife.Driver;
using RimLife.Framework.Mcp;
using RimLife.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Driver
{
    /// <summary>
    /// 工作空间内部事件池测试。覆盖 EventPool 生命周期、Append、
    /// Drain、阈值回调、激活条件。
    /// </summary>
    public class WorkspaceEventPoolTests
    {
        private static DriverConfig CreateConfig()
        {
            return new DriverConfig
            {
                CountThreshold = 3,
                ImportanceThreshold = 10,
                RecentHistoryCapacity = 20
            };
        }

        private static IGameEvent MakeEvent(string id, string severity, int tick = 0,
            IReadOnlyList<string> tags = null, IReadOnlyList<EventActorRef> actors = null)
        {
            return new TestGameEvent
            {
                EventID = id,
                DefName = "TestEvent",
                Tags = tags ?? new List<string> { "Test" },
                Tick = tick,
                Severity = severity,
                Actors = actors ?? new List<EventActorRef>(),
                MapHint = "",
                Payload = new Dictionary<string, string>()
            };
        }

        private static WorkspaceState CreateWorkspaceState(string id = "ws-001")
        {
            return new WorkspaceState
            {
                Id = id,
                Label = "Test Workspace",
                Status = WorkspaceStatus.Active,
                CreatedByRole = WorkspaceRole.Screenwriter,
                ColonistIds = new List<string> { "pawn_001" },
                Tags = new List<string> { "RaidArc" },
                ActiveSkillIds = new List<string> { "workspace_writing" }
            };
        }

        private static WorkspaceEventPool CreatePool(string wsId = "ws-001")
        {
            var ws = CreateWorkspaceState(wsId);
            return new WorkspaceEventPool(ws, CreateConfig(), CardSerializer.Default, () => { });
        }

        // ================================================================
        // EventPool 初始化
        // ================================================================

        [Fact]
        public void EventPool_InitialState()
        {
            var pool = CreatePool();
            Assert.Equal(0, pool.PendingCount);
            Assert.Equal(0, pool.TotalImportance);
            Assert.Equal(0, pool.TotalAppended);
        }

        // ================================================================
        // Append（写入语义）
        // ================================================================

        [Fact]
        public void Append_IncreasesPendingCount()
        {
            var pool = CreatePool();

            pool.Append(MakeEvent("e1", "Minor"));
            Assert.Equal(1, pool.PendingCount);

            pool.Append(MakeEvent("e2", "Major"));
            Assert.Equal(2, pool.PendingCount);
        }

        [Fact]
        public void Append_CalculatesImportance()
        {
            var pool = CreatePool();

            pool.Append(MakeEvent("e1", "Minor"));   // weight 1
            pool.Append(MakeEvent("e2", "Major"));   // weight 3
            pool.Append(MakeEvent("e3", "Extreme"));  // weight 5

            Assert.Equal(9, pool.TotalImportance);
        }

        [Fact]
        public void Append_Null_DoesNotAffectPool()
        {
            var pool = CreatePool();

            pool.Append(null);
            Assert.Equal(0, pool.PendingCount);
        }

        [Fact]
        public void DrainPending_ReturnsEventsAndClears()
        {
            var pool = CreatePool();

            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Major"));

            var drained = pool.DrainPending();

            Assert.Equal(2, drained.Count);
            Assert.Equal(0, pool.PendingCount);
            Assert.Equal(0, pool.TotalImportance);
        }

        // ================================================================
        // OnThresholdReached 回调
        // ================================================================

        [Fact]
        public void OnThresholdReached_NotFiredWhenNoSubscriber()
        {
            var pool = CreatePool();
            // 无订阅者时，Append 不应抛异常
            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Major"));
            Assert.Equal(3, pool.PendingCount);
        }

        [Fact]
        public void OnThresholdReached_FiresWhenCountExceeded()
        {
            var pool = CreatePool(); // threshold=3
            int fireCount = 0;
            pool.OnThresholdReached += () => fireCount++;

            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Minor"));
            Assert.Equal(0, fireCount); // 未达阈值

            pool.Append(MakeEvent("e3", "Minor"));
            Assert.Equal(1, fireCount); // 达到 count=3
        }

        [Fact]
        public void OnThresholdReached_FiresWhenImportanceExceeded()
        {
            var pool = CreatePool(); // imp threshold=10
            int fireCount = 0;
            pool.OnThresholdReached += () => fireCount++;

            // 2 Extreme = weight 10 → 达阈值
            pool.Append(MakeEvent("e1", "Extreme"));
            pool.Append(MakeEvent("e2", "Extreme"));
            Assert.Equal(1, fireCount);
        }

        [Fact]
        public void OnThresholdReached_FiresMultipleTimes()
        {
            var pool = CreatePool();
            int fireCount = 0;
            pool.OnThresholdReached += () => fireCount++;

            // 第一轮触发
            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Major"));
            Assert.Equal(1, fireCount);

            pool.DrainPending();

            // 第二轮触发
            pool.Append(MakeEvent("e4", "Major"));
            pool.Append(MakeEvent("e5", "Major"));
            pool.Append(MakeEvent("e6", "Major"));
            Assert.Equal(2, fireCount);
        }

        // ================================================================
        // 激活条件（纯事件驱动，无定时器）
        // ================================================================

        [Fact]
        public void Activation_CountThreshold_Satisfied()
        {
            var pool = CreatePool(); // threshold=3

            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Minor"));
            pool.Append(MakeEvent("e3", "Minor"));

            Assert.True(pool.PendingCount >= 3);
        }

        [Fact]
        public void Activation_CountThreshold_NotSatisfied()
        {
            var pool = CreatePool(); // threshold=3

            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Minor"));

            Assert.False(pool.PendingCount >= 3);
        }

        [Fact]
        public void Activation_ImportanceThreshold_Satisfied()
        {
            var pool = CreatePool(); // imp threshold=10

            // 2 Major(6) + 1 Extreme(5) = 11
            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Extreme"));

            Assert.True(pool.TotalImportance >= 10);
        }

        [Fact]
        public void Activation_ImportanceThreshold_NotSatisfied()
        {
            var pool = CreatePool(); // imp threshold=10

            // 3 Major = 9
            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Major"));

            Assert.False(pool.TotalImportance >= 10);
        }

        [Fact]
        public void Activation_EitherCountOrImportance_Triggers()
        {
            var pool = CreatePool(); // count=3, imp=10

            // Count 不够，但 Importance 够了
            pool.Append(MakeEvent("e1", "Extreme")); // weight 5
            pool.Append(MakeEvent("e2", "Extreme")); // weight 5

            Assert.False(pool.PendingCount >= 3);   // count not met
            Assert.True(pool.TotalImportance >= 10);  // imp met

            // Drain 后重置
            pool.DrainPending();

            // Importance 不够，但 Count 够了
            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Minor"));
            pool.Append(MakeEvent("e3", "Minor"));

            Assert.True(pool.PendingCount >= 3);    // count met
            Assert.False(pool.TotalImportance >= 10); // imp not met
        }

        // ================================================================
        // 多工作空间独立性
        // ================================================================

        [Fact]
        public void MultipleWorkspaces_IndependentPools()
        {
            var poolA = CreatePool("ws-a");
            var poolB = CreatePool("ws-b");

            poolA.Append(MakeEvent("e1", "Major"));
            poolA.Append(MakeEvent("e2", "Major"));
            poolB.Append(MakeEvent("e3", "Extreme"));

            Assert.Equal(2, poolA.PendingCount);
            Assert.Equal(1, poolB.PendingCount);
        }

        [Fact]
        public void MultipleWorkspaces_IndependentCallbacks()
        {
            var poolA = CreatePool("ws-a");
            var poolB = CreatePool("ws-b");

            int fireA = 0, fireB = 0;
            poolA.OnThresholdReached += () => fireA++;
            poolB.OnThresholdReached += () => fireB++;

            // 仅触发 A
            poolA.Append(MakeEvent("e1", "Major"));
            poolA.Append(MakeEvent("e2", "Major"));
            poolA.Append(MakeEvent("e3", "Major"));

            Assert.Equal(1, fireA);
            Assert.Equal(0, fireB);
        }

        // ================================================================
        // ThresholdReached 跨工作空间隔离
        // ================================================================

        [Fact]
        public void Callback_DoesNotCrossFireBetweenPools()
        {
            var poolA = CreatePool("ws-a");
            var poolB = CreatePool("ws-b");

            int fireB = 0;
            poolB.OnThresholdReached += () => fireB++;

            // 填满 poolA，但 poolB 回调不应触发
            poolA.Append(MakeEvent("e1", "Major"));
            poolA.Append(MakeEvent("e2", "Major"));
            poolA.Append(MakeEvent("e3", "Major"));

            Assert.Equal(0, fireB);
        }

        // ================================================================
        // Test Helper
        // ================================================================

        private class TestGameEvent : IGameEvent
        {
            public string EventID { get; set; }
            public string DefName { get; set; }
            public IReadOnlyList<string> Tags { get; set; }
            public int Tick { get; set; }
            public string Severity { get; set; }
            public IReadOnlyList<EventActorRef> Actors { get; set; }
            public string MapHint { get; set; }
            public IDictionary<string, string> Payload { get; set; }
        }
    }
}
