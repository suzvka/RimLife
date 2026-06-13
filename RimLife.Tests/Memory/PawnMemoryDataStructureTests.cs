using System.Collections.Generic;
using Xunit;

namespace RimLife.Tests.Memory
{
    /// <summary>
    /// Pawn 记忆数据结构测试。
    /// 验证 ShortTermMemory / LongTermMemory / ShortTermReview / CurrentMindset
    /// 的创建、属性访问和截断。
    /// </summary>
    public class PawnMemoryDataStructureTests
    {
        // ================================================================
        // ShortTermMemory
        // ================================================================

        [Fact]
        public void ShortTermMemory_Constructor_SetsProperties()
        {
            var stm = new ShortTermMemory(1000, "Combat", "参与了一场激烈的战斗", "pawn_002");

            Assert.Equal(1000, stm.Tick);
            Assert.Equal("Combat", stm.Type);
            Assert.Equal("参与了一场激烈的战斗", stm.Summary);
            Assert.Equal("pawn_002", stm.RelatedPawnId);
        }

        [Fact]
        public void ShortTermMemory_DefaultConstructor_Works()
        {
            var stm = new ShortTermMemory();

            Assert.Equal(0, stm.Tick);
            Assert.Null(stm.Type);
            Assert.Null(stm.Summary);
            Assert.Null(stm.RelatedPawnId);
        }

        [Fact]
        public void ShortTermMemory_TruncatedSummary_WithinLimit()
        {
            var stm = new ShortTermMemory(100, "Event", "短摘要");

            Assert.Equal("短摘要", stm.TruncatedSummary(200));
        }

        [Fact]
        public void ShortTermMemory_TruncatedSummary_ExceedsLimit()
        {
            var longText = new string('A', 250);
            var stm = new ShortTermMemory(100, "Event", longText);

            var truncated = stm.TruncatedSummary(200);
            Assert.Equal(201, truncated.Length); // 200 + "…"
            Assert.EndsWith("…", truncated);
        }

        [Fact]
        public void ShortTermMemory_ToSting_ContainsKeyInfo()
        {
            var stm = new ShortTermMemory(5000, "Combat", "战斗摘要", "pawn_X");

            var s = stm.ToString();
            Assert.Contains("[STM", s);
            Assert.Contains("5000", s);
            Assert.Contains("Combat", s);
        }

        [Fact]
        public void ShortTermMemory_NullType_DefaultsToObservation()
        {
            var stm = new ShortTermMemory(100, null, "test");
            Assert.Equal("Observation", stm.Type);
        }

        // ================================================================
        // LongTermMemory
        // ================================================================

        [Fact]
        public void LongTermMemory_Constructor_SetsProperties()
        {
            var relatedIds = new List<string> { "pawn_A", "pawn_B" };
            var ltm = new LongTermMemory(10000, "combat", "重大战役的叙述", relatedIds);

            Assert.Equal(10000, ltm.ConsolidatedTick);
            Assert.Equal("combat", ltm.Topic);
            Assert.Equal("重大战役的叙述", ltm.Summary);
            Assert.Equal(2, ltm.RelatedPawnIds.Count);
            Assert.Contains("pawn_A", ltm.RelatedPawnIds);
            Assert.Contains("pawn_B", ltm.RelatedPawnIds);
        }

        [Fact]
        public void LongTermMemory_DefaultConstructor_InitializesLists()
        {
            var ltm = new LongTermMemory();

            Assert.NotNull(ltm.RelatedPawnIds);
            Assert.Empty(ltm.RelatedPawnIds);
        }

        [Fact]
        public void LongTermMemory_TruncatedSummary_Works()
        {
            var ltm = new LongTermMemory(1000, "topic", "短摘要", new List<string>());

            Assert.Equal("短摘要", ltm.TruncatedSummary(500));

            var longText = new string('B', 600);
            var ltmLong = new LongTermMemory(1000, "topic", longText, new List<string>());
            var truncated = ltmLong.TruncatedSummary(500);

            Assert.Equal(501, truncated.Length); // 500 + "…"
            Assert.EndsWith("…", truncated);
        }

        [Fact]
        public void LongTermMemory_ToSting_ContainsKeyInfo()
        {
            var ltm = new LongTermMemory(10000, "combat", "测试记忆", new List<string>());

            var s = ltm.ToString();
            Assert.Contains("[LTM", s);
            Assert.Contains("10000", s);
            Assert.Contains("combat", s);
        }

        // ================================================================
        // ShortTermReview
        // ================================================================

        [Fact]
        public void ShortTermReview_Constructor_SetsProperties()
        {
            var review = new ShortTermReview(5000, "近期发生了很多事");

            Assert.Equal(5000, review.LastUpdateTick);
            Assert.Equal("近期发生了很多事", review.Content);
        }

        [Fact]
        public void ShortTermReview_DefaultConstructor_Works()
        {
            var review = new ShortTermReview();

            Assert.Equal(0, review.LastUpdateTick);
            Assert.Null(review.Content);
        }

        [Fact]
        public void ShortTermReview_ToString_ContainsPreview()
        {
            var review = new ShortTermReview(5000, "这是一段回顾内容");
            var s = review.ToString();
            Assert.Contains("[Review", s);
            Assert.Contains("5000", s);
        }

        // ================================================================
        // CurrentMindset
        // ================================================================

        [Fact]
        public void CurrentMindset_Constructor_SetsProperties()
        {
            var mindset = new CurrentMindset(8000, "我感到有些不安");

            Assert.Equal(8000, mindset.LastUpdateTick);
            Assert.Equal("我感到有些不安", mindset.Content);
        }

        [Fact]
        public void CurrentMindset_DefaultConstructor_Works()
        {
            var mindset = new CurrentMindset();

            Assert.Equal(0, mindset.LastUpdateTick);
            Assert.Null(mindset.Content);
        }

        [Fact]
        public void CurrentMindset_ToString_ContainsPreview()
        {
            var mindset = new CurrentMindset(8000, "心情平静的描述");
            var s = mindset.ToString();
            Assert.Contains("[Mindset", s);
            Assert.Contains("8000", s);
        }
    }
}
