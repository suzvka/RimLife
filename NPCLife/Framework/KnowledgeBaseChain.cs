using System;
using System.Collections.Generic;
using NPCLife.Core;

namespace NPCLife.Framework
{
    /// <summary>
    /// 分层知识库链。将多个 IKnowledgeBase 实现按优先级串联：
    /// L1 为本地缓存（BuiltInKnowledgeBase），L2 为 GameDef 查询，L3 为外部 LLM/Wiki。
    /// TryLookup 依次尝试各层，首个命中即返回。L1 仅由 Agent 通过 learn_term 写入，
    /// 系统不做自动回写，保证 L1 中沉淀的是 Agent 主动学习的高质量关联知识。
    /// 纯静态友好的责任链设计，零 RimWorld 依赖。
    /// </summary>
    public class KnowledgeBaseChain : IKnowledgeBase
    {
        private readonly List<IKnowledgeBase> _chain;
        private readonly IKnowledgeBase _l1;

        /// <summary>
        /// 创建知识库链。第一个元素视为 L1（本地缓存），Store/Delete/ListByPrefix 代理到 L1。
        /// </summary>
        /// <param name="chain">按优先级排列的知识库列表。至少需要一个元素。</param>
        public KnowledgeBaseChain(params IKnowledgeBase[] chain)
        {
            if (chain == null || chain.Length == 0)
                throw new ArgumentException("KnowledgeBaseChain requires at least one IKnowledgeBase.", nameof(chain));

            _chain = new List<IKnowledgeBase>(chain);
            _l1 = chain[0];
        }

        /// <summary>
        /// 向链末尾追加一个新的知识库层。
        /// </summary>
        public void AppendLayer(IKnowledgeBase kb)
        {
            if (kb != null)
                _chain.Add(kb);
        }

        // ================================================================
        // IKnowledgeBase
        // ================================================================

        /// <summary>
        /// 分层查询：依次尝试链中的每个知识库，首个命中即返回。
        /// L1 仅由 Agent 写入（learn_term），系统不做自动回写，
        /// 确保 L1 中沉淀的是高质量 Agent 关联知识而非原始 Def 数据。
        /// </summary>
        public bool TryLookup(string term, out KnowledgeEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(term))
            {
                OnLookupResult?.Invoke(term ?? "", -1, false);
                return false;
            }

            for (int i = 0; i < _chain.Count; i++)
            {
                if (_chain[i].TryLookup(term, out entry) && entry != null)
                {
                    OnLookupResult?.Invoke(term, i, true);
                    return true;
                }
                entry = null; // 重置，避免下行副作用污染
            }

            OnLookupResult?.Invoke(term, -1, false);
            return false;
        }

        /// <summary>
        /// 存储知识到 L1（本地缓存）。
        /// </summary>
        public void Store(KnowledgeEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Term)) return;
            _l1?.Store(entry);
        }

        /// <summary>
        /// 从 L1 中删除词条。
        /// </summary>
        public void Delete(string term)
        {
            if (string.IsNullOrEmpty(term)) return;
            _l1?.Delete(term);
        }

        /// <summary>
        /// 从 L1 中按前缀列举词条。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
        {
            return _l1?.ListByPrefix(prefix) ?? new List<KnowledgeEntry>();
        }

        /// <summary>
        /// 从 L1 中按语义标签筛选词条。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags)
        {
            return _l1?.ListByTags(tags) ?? new List<KnowledgeEntry>();
        }

        /// <summary>
        /// 从 L1 中列出全部词条。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListAll()
        {
            return _l1?.ListAll() ?? new List<KnowledgeEntry>();
        }

        /// <summary>
        /// 返回 L1 中的词条总数。
        /// </summary>
        public int Count => _l1?.Count ?? 0;

        /// <summary>
        /// 返回链中的层数。
        /// </summary>
        public int LayerCount => _chain.Count;

        /// <summary>
        /// 查询结果回调。参数：(term, hitLayer, isHit)。
        /// hitLayer: 0=L1, 1=L2, ..., -1=miss。
        /// 用于运行时度量采集（知识库命中率统计），不影响查询结果。
        /// </summary>
        public Action<string, int, bool> OnLookupResult;

        /// <summary>
        /// 按索引获取链中的知识库层。
        /// </summary>
        public IKnowledgeBase GetLayer(int index)
        {
            if (index < 0 || index >= _chain.Count)
                return null;
            return _chain[index];
        }
    }
}
