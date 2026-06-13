using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimLife
{
    /// <summary>
    /// 模板记忆重写器：无 LLM 时的降级方案。
    /// 按 (Type, RelatedPawnId) 分组 STM，尝试与匹配的旧 LTM 合并。
    /// 生成机械但功能完整的叙述和短期回顾。
    /// </summary>
    public class TemplateMemoryRewriter : IMemoryRewriter
    {
        public Task<MemoryRewriteResult> RewriteAsync(ConsolidationRequest request)
        {
            var result = new MemoryRewriteResult
            {
                UpdatedLtm = new List<LongTermMemory>(request.ExistingLtm),
                Review = BuildReview(request)
            };

            // 按 (Type, RelatedPawnId) 分组 STM
            var groups = request.NewEvents
                .GroupBy(stm => new { Type = stm.Type ?? "Observation", PawnKey = stm.RelatedPawnId ?? "__alone__" });

            foreach (var group in groups)
            {
                var entries = group.ToList();
                string topic = BuildTopic(group.Key.Type, group.Key.PawnKey);

                // 查找匹配的旧 LTM（同 Topic）
                var existingIndex = result.UpdatedLtm.FindIndex(ltm => ltm.Topic == topic);
                var relatedPawnIds = entries
                    .Select(e => e.RelatedPawnId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();

                string newSummary = BuildSummary(entries, topic);

                if (existingIndex >= 0)
                {
                    // 重写：在旧叙述基础上追加新内容
                    var existing = result.UpdatedLtm[existingIndex];
                    string merged = MergeSummaries(existing.Summary, newSummary);
                    var mergedPawnIds = existing.RelatedPawnIds
                        .Union(relatedPawnIds)
                        .Distinct()
                        .ToList();

                    result.UpdatedLtm[existingIndex] = new LongTermMemory(
                        consolidatedTick: request.CurrentTick,
                        topic: topic,
                        summary: merged,
                        relatedPawnIds: mergedPawnIds);
                }
                else
                {
                    // 新增 LTM 条目
                    result.UpdatedLtm.Add(new LongTermMemory(
                        consolidatedTick: request.CurrentTick,
                        topic: topic,
                        summary: newSummary,
                        relatedPawnIds: relatedPawnIds));
                }
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// 构建主题标签。
        /// </summary>
        private static string BuildTopic(string type, string pawnKey)
        {
            if (pawnKey != "__alone__")
                return $"relationship:{pawnKey}";

            switch (type)
            {
                case "Combat": return "combat";
                case "Event": return "colony_events";
                case "Milestone": return "milestones";
                case "Interaction": return "social";
                default: return "observations";
            }
        }

        /// <summary>
        /// 从 STM 条目构建叙述摘要。
        /// </summary>
        private static string BuildSummary(List<ShortTermMemory> entries, string topic)
        {
            if (entries.Count == 0) return "";

            var sb = new StringBuilder();
            sb.Append(entries[0].Summary);

            if (entries.Count > 1)
            {
                int otherCount = entries.Count - 1;
                sb.Append($"（同期还发生了 {otherCount} 件相关事件）");
            }

            string result = sb.ToString();
            return result.Length <= 500 ? result : result.Substring(0, 497) + "…";
        }

        /// <summary>
        /// 将旧叙述与新叙述合并，截断至 500 字。
        /// </summary>
        private static string MergeSummaries(string oldSummary, string newSummary)
        {
            if (string.IsNullOrEmpty(oldSummary)) return newSummary;
            if (string.IsNullOrEmpty(newSummary)) return oldSummary;

            string merged = oldSummary + " " + newSummary;
            return merged.Length <= 500 ? merged : merged.Substring(0, 497) + "…";
        }

        /// <summary>
        /// 构建短期回顾：对所有 STM 做客观摘要。
        /// </summary>
        private static ShortTermReview BuildReview(ConsolidationRequest request)
        {
            if (request.NewEvents.Count == 0)
                return new ShortTermReview(request.CurrentTick, "(无事发生)");

            var sb = new StringBuilder();
            sb.Append("近期发生了以下事件：");

            int count = 0;
            foreach (var stm in request.NewEvents.OrderByDescending(e => e.Tick).Take(8))
            {
                if (count > 0) sb.Append("；");
                sb.Append(stm.TruncatedSummary(80));
                count++;
            }

            if (request.NewEvents.Count > 8)
                sb.Append($"……等共 {request.NewEvents.Count} 件事件。");

            string content = sb.ToString();
            if (content.Length > 300)
                content = content.Substring(0, 297) + "…";

            return new ShortTermReview(request.CurrentTick, content);
        }
    }
}
