using System.Collections.Generic;
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

            Assert.Null(q.TagsAny);
            Assert.Null(q.TagsAll);
            Assert.Null(q.SinceTick);
            Assert.Null(q.UntilTick);
            Assert.Null(q.ActorId);
            Assert.Null(q.MinImportance);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void ByAnyTag_SetsOnlyTagsAny()
        {
            var q = EventQuery.ByAnyTag("Combat", "Raid");

            Assert.Equal(2, q.TagsAny.Count);
            Assert.Contains("Combat", q.TagsAny);
            Assert.Null(q.TagsAll);
            Assert.Null(q.SinceTick);
            Assert.Null(q.MinImportance);
        }

        [Fact]
        public void ByAllTags_SetsOnlyTagsAll()
        {
            var q = EventQuery.ByAllTags("Combat", "Major");

            Assert.Equal(2, q.TagsAll.Count);
            Assert.Contains("Combat", q.TagsAll);
            Assert.Null(q.TagsAny);
            Assert.Null(q.SinceTick);
        }

        [Fact]
        public void Since_SetsOnlySinceTick()
        {
            var q = EventQuery.Since(1000);

            Assert.Equal(1000, q.SinceTick);
            Assert.Null(q.TagsAny);
            Assert.Null(q.TagsAll);
            Assert.Null(q.UntilTick);
        }

        [Fact]
        public void DefaultConstructor_AllNull()
        {
            var q = new EventQuery();

            Assert.Null(q.TagsAny);
            Assert.Null(q.TagsAll);
            Assert.Null(q.SinceTick);
            Assert.Null(q.UntilTick);
            Assert.Null(q.ActorId);
            Assert.Null(q.MinImportance);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void CombinedFilters_AllSetCorrectly()
        {
            var q = new EventQuery
            {
                TagsAny = new List<string> { "Social", "Health" },
                TagsAll = new List<string> { "Combat" },
                SinceTick = 5000,
                UntilTick = 10000,
                ActorId = "pawn_001",
                MinImportance = 3f,
                Limit = 20,
                Offset = 5
            };

            Assert.Equal(2, q.TagsAny.Count);
            Assert.Contains("Social", q.TagsAny);
            Assert.Single(q.TagsAll);
            Assert.Contains("Combat", q.TagsAll);
            Assert.Equal(5000, q.SinceTick);
            Assert.Equal(10000, q.UntilTick);
            Assert.Equal("pawn_001", q.ActorId);
            Assert.Equal(3f, q.MinImportance);
            Assert.Equal(20, q.Limit);
            Assert.Equal(5, q.Offset);
        }
    }
}
