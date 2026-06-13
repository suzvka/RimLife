using RimLife.Cards;
using RimLife.Core;
using RimLife.Driver;
using System.Collections.Generic;
using Xunit;

namespace RimLife.Tests.Driver
{
    /// <summary>
    /// AgentEventPool 单元测试。纯逻辑，无 RimWorld 依赖。
    /// </summary>
    public class AgentEventPoolTests
    {
        private static DriverConfig CreateConfig()
        {
            return new DriverConfig
            {
                CountThreshold = 5,
                ImportanceThreshold = 15,
                RecentHistoryCapacity = 20
            };
        }

        private static IGameEvent MakeEvent(string id, string severity, int tick = 0)
        {
            return new TestGameEvent
            {
                EventID = id,
                DefName = "TestEvent",
                Tags = new List<string> { "Test" },
                Tick = tick,
                Severity = severity,
                Actors = new List<EventActorRef>(),
                MapHint = "",
                Payload = new Dictionary<string, string>()
            };
        }

        // ================================================================
        // Append & Pending
        // ================================================================

        [Fact]
        public void Append_IncreasesPendingCount()
        {
            var config = CreateConfig();
            var pool = new AgentEventPool(config);

            Assert.Equal(0, pool.PendingCount);
            pool.Append(MakeEvent("e1", "Minor"));
            Assert.Equal(1, pool.PendingCount);
            pool.Append(MakeEvent("e2", "Major"));
            Assert.Equal(2, pool.PendingCount);
        }

        [Fact]
        public void Append_NullEvent_DoesNotCrash()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(null);
            Assert.Equal(0, pool.PendingCount);
        }

        [Fact]
        public void Append_IncreasesTotalAppended()
        {
            var pool = new AgentEventPool(CreateConfig());
            Assert.Equal(0, pool.TotalAppended);
            pool.Append(MakeEvent("e1", "Minor"));
            Assert.Equal(1, pool.TotalAppended);
            pool.Append(MakeEvent("e2", "Major"));
            Assert.Equal(2, pool.TotalAppended);
        }

        // ================================================================
        // Importance
        // ================================================================

        [Fact]
        public void TotalImportance_CalculatesCorrectly()
        {
            var config = CreateConfig();
            var pool = new AgentEventPool(config);

            pool.Append(MakeEvent("e1", "Minor"));   // weight 1
            pool.Append(MakeEvent("e2", "Major"));   // weight 3
            pool.Append(MakeEvent("e3", "Extreme"));  // weight 5

            Assert.Equal(9, pool.TotalImportance);
        }

        [Fact]
        public void TotalImportance_UnknownSeverity_CountsAsZero()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "UnknownSeverity"));
            Assert.Equal(0, pool.TotalImportance);
        }

        // ================================================================
        // Drain & Clear
        // ================================================================

        [Fact]
        public void DrainPending_ReturnsEventsAndClears()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Major"));

            var drained = pool.DrainPending();

            Assert.Equal(2, drained.Count);
            Assert.Equal(0, pool.PendingCount);
            Assert.Equal(0, pool.TotalImportance);
        }

        [Fact]
        public void DrainPending_EmptyPool_ReturnsEmpty()
        {
            var pool = new AgentEventPool(CreateConfig());
            var drained = pool.DrainPending();
            Assert.Empty(drained);
        }

        [Fact]
        public void ClearPending_ClearsAll()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Extreme"));
            Assert.Equal(2, pool.PendingCount);
            Assert.NotEqual(0, pool.TotalImportance);

            pool.ClearPending();
            Assert.Equal(0, pool.PendingCount);
            Assert.Equal(0, pool.TotalImportance);
        }

        // ================================================================
        // Recent History (Query)
        // ================================================================

        [Fact]
        public void Query_ReturnsEventsInOrder()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor", 10));
            pool.Append(MakeEvent("e2", "Major", 20));
            pool.Append(MakeEvent("e3", "Minor", 30));

            var all = pool.Query(EventQuery.All);
            Assert.Equal(3, all.Count);
            Assert.Equal("e1", all[0].EventID);
            Assert.Equal("e2", all[1].EventID);
            Assert.Equal("e3", all[2].EventID);
        }

        [Fact]
        public void Query_FilterByTag()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor"));
            // Need to add a different tag event
            var evt = MakeEvent("e2", "Major");
            var tags = new List<string> { "Combat", "Raid" };
            // Use TestGameEvent to set custom tags
            var combatEvent = new TestGameEvent
            {
                EventID = "e2",
                DefName = "Raid",
                Tags = new List<string> { "Combat", "Raid" },
                Tick = 20,
                Severity = "Major",
                Actors = new List<EventActorRef>(),
                MapHint = "",
                Payload = new Dictionary<string, string>()
            };
            pool.Append(combatEvent);

            var result = pool.Query(EventQuery.ByAnyTag("Combat"));
            Assert.Single(result);
            Assert.Equal("e2", result[0].EventID);
        }

        [Fact]
        public void Query_FilterBySeverity()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Major"));
            pool.Append(MakeEvent("e3", "Extreme"));

            var q = new EventQuery { Severity = "Major" };
            var result = pool.Query(q);
            Assert.Single(result);
            Assert.Equal("e2", result[0].EventID);
        }

        [Fact]
        public void Query_FilterByTimeRange()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor", 100));
            pool.Append(MakeEvent("e2", "Major", 200));
            pool.Append(MakeEvent("e3", "Minor", 300));

            var q = new EventQuery { SinceTick = 150, UntilTick = 250 };
            var result = pool.Query(q);
            Assert.Single(result);
            Assert.Equal("e2", result[0].EventID);
        }

        [Fact]
        public void Query_Pagination()
        {
            var pool = new AgentEventPool(CreateConfig());
            for (int i = 0; i < 10; i++)
                pool.Append(MakeEvent($"e{i}", "Minor", i));

            var q = new EventQuery { Limit = 3 };
            var result = pool.Query(q);
            Assert.Equal(3, result.Count);

            var q2 = new EventQuery { Offset = 5, Limit = 3 };
            var result2 = pool.Query(q2);
            Assert.Equal(3, result2.Count);
            Assert.Equal("e5", result2[0].EventID);
        }

        [Fact]
        public void Count_ReturnsCorrectTotal()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor"));
            pool.Append(MakeEvent("e2", "Major"));

            Assert.Equal(2, pool.Count(EventQuery.All));
        }

        [Fact]
        public void Latest_ReturnsMostRecent()
        {
            var pool = new AgentEventPool(CreateConfig());
            pool.Append(MakeEvent("e1", "Minor", 10));
            pool.Append(MakeEvent("e2", "Major", 20));

            Assert.Equal("e2", pool.Latest.EventID);
        }

        [Fact]
        public void Latest_EmptyPool_ReturnsNull()
        {
            var pool = new AgentEventPool(CreateConfig());
            Assert.Null(pool.Latest);
        }

        // ================================================================
        // Capacity
        // ================================================================

        [Fact]
        public void RecentHistory_TrimsWhenOverCapacity()
        {
            var config = new DriverConfig { RecentHistoryCapacity = 5 };
            var pool = new AgentEventPool(config);

            for (int i = 0; i < 10; i++)
                pool.Append(MakeEvent($"e{i}", "Minor", i));

            Assert.Equal(5, pool.RecentEvents.Count);
            // 应保留最新的：e5-e9
            Assert.Equal("e5", pool.RecentEvents[0].EventID);
            Assert.Equal("e9", pool.RecentEvents[4].EventID);
        }

        [Fact]
        public void RecentHistory_PrefersTrimmingMinorEvents()
        {
            var config = new DriverConfig { RecentHistoryCapacity = 3 };
            var pool = new AgentEventPool(config);

            pool.Append(MakeEvent("e1", "Major"));
            pool.Append(MakeEvent("e2", "Minor"));
            pool.Append(MakeEvent("e3", "Extreme"));
            // 再加一个会触发裁剪
            pool.Append(MakeEvent("e4", "Major"));

            Assert.Equal(3, pool.RecentEvents.Count);
            // Minor 应被优先裁剪（e2 被移除）
            Assert.DoesNotContain(pool.RecentEvents, e => e.EventID == "e2");
        }

        // ================================================================
        // Null config
        // ================================================================

        [Fact]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new AgentEventPool(null));
        }

        // ================================================================
        // IEventLog compatibility
        // ================================================================

        [Fact]
        public void Implements_IEventLog()
        {
            var pool = new AgentEventPool(CreateConfig());
            Assert.IsAssignableFrom<IEventLog>(pool);
        }

        // ================================================================
        // Helper
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
