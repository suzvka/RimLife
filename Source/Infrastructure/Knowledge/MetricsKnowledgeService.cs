using NPCLife.Core;
using NPCLife.Framework;
using System.Collections.Generic;

namespace RimLife.Infrastructure.Knowledge
{
    /// <summary>
    /// IKnowledgeService 指标装饰器。拦截 Lookup 调用，
    /// 对每条查询结果按 Source 上报 RuntimeMetrics 指标。
    /// </summary>
    internal class MetricsKnowledgeService : IKnowledgeService
    {
        private readonly IKnowledgeService _inner;

        public MetricsKnowledgeService(IKnowledgeService inner)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// 代理到内部服务，对命中/未命中上报指标。
        /// hitLayer=0 表示命中，hitLayer=-1 表示未命中。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> Lookup(string term)
        {
            var results = _inner.Lookup(term);
            var sessionId = MetricsInterceptor.CurrentSessionId;
            int hitLayer = results.Count > 0 ? 0 : -1;
            RuntimeMetrics.RecordKnowledgeLookup(term, hitLayer, sessionId);
            return results;
        }

        public void Store(KnowledgeEntry entry) => _inner.Store(entry);
        public void Delete(string term) => _inner.Delete(term);
        public IReadOnlyList<KnowledgeEntry> ListAll() => _inner.ListAll();
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix) => _inner.ListByPrefix(prefix);
    }
}
