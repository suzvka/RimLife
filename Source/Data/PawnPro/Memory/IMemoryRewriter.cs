using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimLife
{
    /// <summary>
    /// 记忆重写器接口。将 STM 流水 + 已有 LTM 存档融合为新的长期记忆叙述。
    /// 由 MemoryConsolidator 在巩固 Phase 2 调用。
    /// 实现：<see cref="TemplateMemoryRewriter"/>（无 LLM 降级）和 LlmMemoryRewriter（MCP 调用）。
    /// </summary>
    public interface IMemoryRewriter
    {
        /// <summary>
        /// 将新事件与已有 LTM 融合，生成重写结果。
        /// </summary>
        /// <param name="request">巩固请求，包含新 STM 和匹配的旧 LTM。</param>
        Task<MemoryRewriteResult> RewriteAsync(ConsolidationRequest request);
    }

    /// <summary>
    /// 巩固请求：Phase 1 生成的中间产物，供 Phase 2 重写器消费。
    /// </summary>
    public class ConsolidationRequest
    {
        /// <summary>Pawn 名称（用于叙述语境）。</summary>
        public string PawnName;

        /// <summary>Pawn 上下文描述（如性格、技能等摘要，可选）。</summary>
        public string PawnContext;

        /// <summary>待巩固的 STM 条目。</summary>
        public List<ShortTermMemory> NewEvents;

        /// <summary>当前已有的全部 LTM 条目。</summary>
        public List<LongTermMemory> ExistingLtm;

        /// <summary>当前游戏 tick。</summary>
        public int CurrentTick;
    }

    /// <summary>
    /// 记忆重写结果：包含更新后的 LTM 列表和新的短期回顾。
    /// 注意：即时心境不在此结果中，它由 LLM 通过独立 MCP 工具主动写入。
    /// </summary>
    public class MemoryRewriteResult
    {
        /// <summary>重写后的完整 LTM 列表（替换旧列表）。</summary>
        public List<LongTermMemory> UpdatedLtm;

        /// <summary>本次巩固生成的短期回顾（可选，null 表示不更新）。</summary>
        public ShortTermReview Review;
    }
}
