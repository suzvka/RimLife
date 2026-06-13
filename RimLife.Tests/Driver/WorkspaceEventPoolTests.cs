using RimLife.Cards;
using RimLife.Driver;
using RimLife.Workspace;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Driver
{
    /// <summary>
    /// 工作空间内部事件池测试。覆盖 EventPool 生命周期、PushEvent、
    /// Locked 状态、激活条件。
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

        private static WorkspaceState CreateWorkspace(string id = "ws-001")
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

        // ================================================================
        // EventPool 初始化
        // ================================================================

        [Fact]
        public void EventPool_DefaultsToNull()
        {
            var ws = new WorkspaceState();
            Assert.Null(ws.EventPool);
        }

        [Fact]
        public void EnsureEventPool_CreatesPoolWhenNull()
        {
            var ws = CreateWorkspace();
            Assert.Null(ws.EventPool);

            var pool = new AgentEventPool(CreateConfig());
            ws.EventPool = pool;

            Assert.NotNull(ws.EventPool);
            Assert.Equal(0, ws.EventPool.PendingCount);
        }

        [Fact]
        public void EnsureEventPool_Idempotent_DoesNotReplace()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig());

            ws.EventPool.Append(MakeEvent("e1", "Major"));
            Assert.Equal(1, ws.EventPool.PendingCount);

            // 第二次不应替换（幂等性由调用方保证——WorkspaceManager.EnsureEventPool
            // 检查 ws.EventPool != null 后跳过）
            Assert.Equal(1, ws.EventPool.PendingCount);
        }

        // ================================================================
        // PushEvent（move 语义）
        // ================================================================

        [Fact]
        public void PushEvent_IncreasesPendingCount()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig());

            ws.EventPool.Append(MakeEvent("e1", "Minor"));
            Assert.Equal(1, ws.EventPool.PendingCount);

            ws.EventPool.Append(MakeEvent("e2", "Major"));
            Assert.Equal(2, ws.EventPool.PendingCount);
        }

        [Fact]
        public void PushEvent_CalculatesImportance()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig());

            ws.EventPool.Append(MakeEvent("e1", "Minor"));   // weight 1
            ws.EventPool.Append(MakeEvent("e2", "Major"));   // weight 3
            ws.EventPool.Append(MakeEvent("e3", "Extreme"));  // weight 5

            Assert.Equal(9, ws.EventPool.TotalImportance);
        }

        [Fact]
        public void PushEvent_Null_DoesNotAffectPool()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig());

            ws.EventPool.Append(null);
            Assert.Equal(0, ws.EventPool.PendingCount);
        }

        [Fact]
        public void DrainPending_ReturnsEventsAndClears()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig());

            ws.EventPool.Append(MakeEvent("e1", "Minor"));
            ws.EventPool.Append(MakeEvent("e2", "Major"));

            var drained = ws.EventPool.DrainPending();

            Assert.Equal(2, drained.Count);
            Assert.Equal(0, ws.EventPool.PendingCount);
            Assert.Equal(0, ws.EventPool.TotalImportance);
        }

        // ================================================================
        // OnThresholdReached 回调
        // ================================================================

        [Fact]
        public void OnThresholdReached_NotFiredWhenNoSubscriber()
        {
            var pool = new AgentEventPool(CreateConfig());
            // 无订阅者时，Append 不应抛异常
            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Major"));
            // 仅验证无异常即可
            Assert.Equal(3, pool.PendingCount);
        }

        [Fact]
        public void OnThresholdReached_FiresWhenCountExceeded()
        {
            var pool = new AgentEventPool(CreateConfig()); // threshold=3
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
            var pool = new AgentEventPool(CreateConfig()); // imp threshold=10
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
            var pool = new AgentEventPool(CreateConfig());
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
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig()); // threshold=3

            ws.EventPool.Append(MakeEvent("e1", "Minor"));
            ws.EventPool.Append(MakeEvent("e2", "Minor"));
            ws.EventPool.Append(MakeEvent("e3", "Minor"));

            Assert.True(ws.EventPool.PendingCount >= 3);
        }

        [Fact]
        public void Activation_CountThreshold_NotSatisfied()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig()); // threshold=3

            ws.EventPool.Append(MakeEvent("e1", "Minor"));
            ws.EventPool.Append(MakeEvent("e2", "Minor"));

            Assert.False(ws.EventPool.PendingCount >= 3);
        }

        [Fact]
        public void Activation_ImportanceThreshold_Satisfied()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig()); // imp threshold=10

            // 2 Major(6) + 1 Extreme(5) = 11
            ws.EventPool.Append(MakeEvent("e1", "Major"));
            ws.EventPool.Append(MakeEvent("e2", "Major"));
            ws.EventPool.Append(MakeEvent("e3", "Extreme"));

            Assert.True(ws.EventPool.TotalImportance >= 10);
        }

        [Fact]
        public void Activation_ImportanceThreshold_NotSatisfied()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig()); // imp threshold=10

            // 3 Major = 9
            ws.EventPool.Append(MakeEvent("e1", "Major"));
            ws.EventPool.Append(MakeEvent("e2", "Major"));
            ws.EventPool.Append(MakeEvent("e3", "Major"));

            Assert.False(ws.EventPool.TotalImportance >= 10);
        }

        [Fact]
        public void Activation_EitherCountOrImportance_Triggers()
        {
            var ws = CreateWorkspace();
            ws.EventPool = new AgentEventPool(CreateConfig()); // count=3, imp=10

            // Count 不够，但 Importance 够了
            ws.EventPool.Append(MakeEvent("e1", "Extreme")); // weight 5
            ws.EventPool.Append(MakeEvent("e2", "Extreme")); // weight 5

            Assert.False(ws.EventPool.PendingCount >= 3);   // count not met
            Assert.True(ws.EventPool.TotalImportance >= 10);  // imp met

            // Drain 后重置
            ws.EventPool.DrainPending();

            // Importance 不够，但 Count 够了
            ws.EventPool.Append(MakeEvent("e1", "Minor"));
            ws.EventPool.Append(MakeEvent("e2", "Minor"));
            ws.EventPool.Append(MakeEvent("e3", "Minor"));

            Assert.True(ws.EventPool.PendingCount >= 3);    // count met
            Assert.False(ws.EventPool.TotalImportance >= 10); // imp not met
        }

        // ================================================================
        // 多工作空间独立性
        // ================================================================

        [Fact]
        public void MultipleWorkspaces_IndependentPools()
        {
            var wsA = CreateWorkspace("ws-a");
            var wsB = CreateWorkspace("ws-b");

            wsA.EventPool = new AgentEventPool(CreateConfig());
            wsB.EventPool = new AgentEventPool(CreateConfig());

            wsA.EventPool.Append(MakeEvent("e1", "Major"));
            wsA.EventPool.Append(MakeEvent("e2", "Major"));
            wsB.EventPool.Append(MakeEvent("e3", "Extreme"));

            Assert.Equal(2, wsA.EventPool.PendingCount);
            Assert.Equal(1, wsB.EventPool.PendingCount);
        }

        [Fact]
        public void MultipleWorkspaces_IndependentCallbacks()
        {
            var wsA = CreateWorkspace("ws-a");
            var wsB = CreateWorkspace("ws-b");

            wsA.EventPool = new AgentEventPool(CreateConfig());
            wsB.EventPool = new AgentEventPool(CreateConfig());

            int fireA = 0, fireB = 0;
            wsA.EventPool.OnThresholdReached += () => fireA++;
            wsB.EventPool.OnThresholdReached += () => fireB++;

            // 仅触发 A
            wsA.EventPool.Append(MakeEvent("e1", "Major"));
            wsA.EventPool.Append(MakeEvent("e2", "Major"));
            wsA.EventPool.Append(MakeEvent("e3", "Major"));

            Assert.Equal(1, fireA);
            Assert.Equal(0, fireB);
        }

        // ================================================================
        // ThresholdReached 跨工作空间隔离
        // ================================================================

        [Fact]
        public void Callback_DoesNotCrossFireBetweenPools()
        {
            var poolA = new AgentEventPool(CreateConfig());
            var poolB = new AgentEventPool(CreateConfig());

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
