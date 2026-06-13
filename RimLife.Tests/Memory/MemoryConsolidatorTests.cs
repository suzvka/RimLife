using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RimLife.Tests.Memory
{
    /// <summary>
    /// MemoryConsolidator 巩固逻辑测试。
    /// 验证两阶段巩固：BuildRequest + RewriteAsync（TemplateMemoryRewriter）。
    /// </summary>
    public class MemoryConsolidatorTests
    {
        [Fact]
        public void BuildRequest_EmptyList_ReturnsNull()
        {
            var result = MemoryConsolidator.BuildRequest(
                new List<ShortTermMemory>(), new List<LongTermMemory>(), 1000);
            Assert.Null(result);
        }

        [Fact]
        public void BuildRequest_NullList_ReturnsNull()
        {
            var result = MemoryConsolidator.BuildRequest(
                null, new List<LongTermMemory>(), 1000);
            Assert.Null(result);
        }

        [Fact]
        public void BuildRequest_ValidList_ReturnsRequest()
        {
            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(100, "Combat", "击退了一只狂躁松鼠", "pawn_002")
            };

            var request = MemoryConsolidator.BuildRequest(
                stmList, new List<LongTermMemory>(), 5000, "TestPawn");

            Assert.NotNull(request);
            Assert.Equal("TestPawn", request.PawnName);
            Assert.Single(request.NewEvents);
            Assert.Empty(request.ExistingLtm);
            Assert.Equal(5000, request.CurrentTick);
        }

        [Fact]
        public async Task RewriteAsync_SingleEntry_CreatesOneLtm()
        {
            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(100, "Combat", "击退了一只狂躁松鼠", "pawn_002")
            };

            var request = new ConsolidationRequest
            {
                PawnName = "Test",
                NewEvents = stmList,
                ExistingLtm = new List<LongTermMemory>(),
                CurrentTick = 5000
            };

            var rewriter = new TemplateMemoryRewriter();
            var result = await rewriter.RewriteAsync(request);

            Assert.NotNull(result);
            Assert.NotNull(result.UpdatedLtm);
            Assert.Single(result.UpdatedLtm);
            Assert.Contains("击退了一只狂躁松鼠", result.UpdatedLtm[0].Summary);
            Assert.Equal(5000, result.UpdatedLtm[0].ConsolidatedTick);
            Assert.NotNull(result.Review);
        }

        [Fact]
        public async Task RewriteAsync_SameTypeAndPawn_MergesIntoOne()
        {
            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(100, "Interaction", "与 pawn_B 聊天", "pawn_B"),
                new ShortTermMemory(200, "Interaction", "与 pawn_B 争论", "pawn_B"),
                new ShortTermMemory(300, "Interaction", "与 pawn_B 开玩笑", "pawn_B"),
            };

            var request = new ConsolidationRequest
            {
                PawnName = "Test",
                NewEvents = stmList,
                ExistingLtm = new List<LongTermMemory>(),
                CurrentTick = 10000
            };

            var rewriter = new TemplateMemoryRewriter();
            var result = await rewriter.RewriteAsync(request);

            // 同类型 + 同关联角色 → 一条 LTM
            Assert.Single(result.UpdatedLtm);
            Assert.Contains("pawn_B", result.UpdatedLtm[0].RelatedPawnIds);
        }

        [Fact]
        public async Task RewriteAsync_DifferentTypes_ProducesSeparateLtms()
        {
            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(100, "Combat", "与掠夺者战斗", null),  // no pawn → topic "combat"
                new ShortTermMemory(200, "Interaction", "与 pawn_B 聊天", "pawn_B"),
                new ShortTermMemory(300, "Observation", "看到了美丽的日落", null),
            };

            var request = new ConsolidationRequest
            {
                PawnName = "Test",
                NewEvents = stmList,
                ExistingLtm = new List<LongTermMemory>(),
                CurrentTick = 5000
            };

            var rewriter = new TemplateMemoryRewriter();
            var result = await rewriter.RewriteAsync(request);

            // 三个不同类型 → 三条 LTM
            Assert.Equal(3, result.UpdatedLtm.Count);
            var topics = result.UpdatedLtm.Select(ltm => ltm.Topic).ToList();
            Assert.Contains("combat", topics);
            Assert.Contains("relationship:pawn_B", topics); // Interaction with pawn_B → relationship topic
            Assert.Contains("observations", topics);
        }

        [Fact]
        public async Task RewriteAsync_ExistingLtm_MergesWithExisting()
        {
            var existingLtm = new List<LongTermMemory>
            {
                new LongTermMemory(1000, "combat", "之前经历过一场大战", new List<string>())
            };

            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(2000, "Combat", "又打了一场小仗", null)  // no pawn → topic "combat"
            };

            var request = new ConsolidationRequest
            {
                PawnName = "Test",
                NewEvents = stmList,
                ExistingLtm = existingLtm,
                CurrentTick = 5000
            };

            var rewriter = new TemplateMemoryRewriter();
            var result = await rewriter.RewriteAsync(request);

            // 应合并到已有的 combat 条目
            var combatEntries = result.UpdatedLtm.Where(ltm => ltm.Topic == "combat").ToList();
            Assert.Single(combatEntries);
            Assert.Contains("大战", combatEntries[0].Summary);
            Assert.Contains("小仗", combatEntries[0].Summary);
        }

        [Fact]
        public async Task RewriteAsync_GeneratesReview()
        {
            var stmList = new List<ShortTermMemory>
            {
                new ShortTermMemory(100, "Event", "发生了事件A"),
                new ShortTermMemory(200, "Event", "发生了事件B"),
            };

            var request = new ConsolidationRequest
            {
                PawnName = "Test",
                NewEvents = stmList,
                ExistingLtm = new List<LongTermMemory>(),
                CurrentTick = 5000
            };

            var rewriter = new TemplateMemoryRewriter();
            var result = await rewriter.RewriteAsync(request);

            Assert.NotNull(result.Review);
            Assert.Equal(5000, result.Review.LastUpdateTick);
            Assert.NotEmpty(result.Review.Content);
            Assert.Contains("近期发生", result.Review.Content);
        }
    }
}
