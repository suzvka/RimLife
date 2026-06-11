using RimLife.Cards;
using RimLife.Core;
using Xunit;

namespace RimLife.Tests.Core
{
    /// <summary>
    /// EventQuery 纯逻辑断言测试。
    /// 验证查询对象的默认值、工厂方法和参数组合。
    /// </summary>
    public class EventQueryTests
    {
        [Fact]
        public void All_ReturnsQueryWithAllNullFilters()
        {
            var q = EventQuery.All;

            Assert.Null(q.Category);
            Assert.Null(q.SinceTick);
            Assert.Null(q.UntilTick);
            Assert.Null(q.ActorId);
            Assert.Null(q.Severity);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void ByCategory_SetsOnlyCategory()
        {
            var q = EventQuery.ByCategory(EventCategory.Combat);

            Assert.Equal(EventCategory.Combat, q.Category);
            Assert.Null(q.SinceTick);
            Assert.Null(q.Severity);
        }

        [Fact]
        public void Since_SetsOnlySinceTick()
        {
            var q = EventQuery.Since(1000);

            Assert.Equal(1000, q.SinceTick);
            Assert.Null(q.Category);
            Assert.Null(q.UntilTick);
        }

        [Fact]
        public void DefaultConstructor_AllNull()
        {
            var q = new EventQuery();

            Assert.Null(q.Category);
            Assert.Null(q.SinceTick);
            Assert.Null(q.UntilTick);
            Assert.Null(q.ActorId);
            Assert.Null(q.Severity);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void CombinedFilters_AllSetCorrectly()
        {
            var q = new EventQuery
            {
                Category = EventCategory.Social,
                SinceTick = 5000,
                UntilTick = 10000,
                ActorId = "pawn_001",
                Severity = "Major",
                Limit = 20,
                Offset = 5
            };

            Assert.Equal(EventCategory.Social, q.Category);
            Assert.Equal(5000, q.SinceTick);
            Assert.Equal(10000, q.UntilTick);
            Assert.Equal("pawn_001", q.ActorId);
            Assert.Equal("Major", q.Severity);
            Assert.Equal(20, q.Limit);
            Assert.Equal(5, q.Offset);
        }
    }
}
