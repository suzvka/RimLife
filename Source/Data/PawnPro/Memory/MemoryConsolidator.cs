using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimLife
{
    /// <summary>
    /// 记忆巩固器：协调 STM → LTM 的两阶段巩固流程。
    /// Phase 1（同步）：构建 <see cref="ConsolidationRequest"/>，将 STM 和已有 LTM 打包。
    /// Phase 2（异步）：调用 <see cref="IMemoryRewriter"/> 生成重写结果。
    /// 由 <see cref="HediffComp_PawnMemory"/> 的 TryConsolidate() 调用。
    /// </summary>
    public static class MemoryConsolidator
    {
        /// <summary>
        /// 默认重写器实例。可在运行时替换（如注册 LlmMemoryRewriter）。
        /// </summary>
        public static IMemoryRewriter Rewriter { get; set; } = new TemplateMemoryRewriter();

        /// <summary>
        /// Phase 1：构建巩固请求。
        /// </summary>
        /// <param name="shortTermMemories">待巩固的短期记忆列表。</param>
        /// <param name="existingLtm">当前已有的全部长期记忆。</param>
        /// <param name="currentTick">当前游戏 tick。</param>
        /// <param name="pawnName">Pawn 名称（用于叙述语境）。</param>
        /// <param name="pawnContext">Pawn 上下文描述（可选）。</param>
        /// <returns>巩固请求，null 表示无需巩固。</returns>
        public static ConsolidationRequest BuildRequest(
            List<ShortTermMemory> shortTermMemories,
            List<LongTermMemory> existingLtm,
            int currentTick,
            string pawnName = null,
            string pawnContext = null)
        {
            if (shortTermMemories == null || shortTermMemories.Count == 0)
                return null;

            return new ConsolidationRequest
            {
                PawnName = pawnName ?? "Unknown",
                PawnContext = pawnContext,
                NewEvents = new List<ShortTermMemory>(shortTermMemories),
                ExistingLtm = existingLtm != null
                    ? new List<LongTermMemory>(existingLtm)
                    : new List<LongTermMemory>(),
                CurrentTick = currentTick
            };
        }

        /// <summary>
        /// Phase 2：调用重写器执行 LLM（或模板）重写。
        /// </summary>
        /// <param name="request">Phase 1 生成的巩固请求。</param>
        /// <returns>重写结果。</returns>
        public static Task<MemoryRewriteResult> RewriteAsync(ConsolidationRequest request)
        {
            return Rewriter.RewriteAsync(request);
        }
    }
}
