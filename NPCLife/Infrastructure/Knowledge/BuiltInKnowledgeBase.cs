using System;
using System.Collections.Generic;
using System.Linq;
using NPCLife.Core;
using NPCLife.Framework;

namespace NPCLife.Infrastructure.Knowledge
{
    /// <summary>
    /// 内置知识库（L1 本地缓存）。将结构化知识条目持久化到 LocalFileStore，
    /// 按存档 GUID 自动隔离。支持 O(1) 查询、自动合并、LRU 淘汰。
    /// </summary>
    public class BuiltInKnowledgeBase : IKnowledgeBase
    {
        private readonly ILogger _logger;

        private const string StoreKey = "rimlife_knowledge";
        private const int DefaultMaxCapacity = 500;

        private readonly Dictionary<string, KnowledgeEntry> _entries
            = new Dictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly ICacheStore _store;
        private readonly int _maxCapacity;
        private bool _dirty;

        /// <summary>全局访问序列号。仅 Agent 调用 TryLookup/Store 时递增，不依赖任何定时器或游戏 tick。</summary>
        private long _accessSeq;

        /// <summary>
        /// 创建内置知识库实例。
        /// </summary>
        /// <param name="store">缓存存储（通常为 RimLifeCore.CacheStore）。</param>
        /// <param name="logger">日志接口。</param>
        /// <param name="maxCapacity">最大条目数，超出时触发 LRU 淘汰。默认 500。</param>
        public BuiltInKnowledgeBase(ICacheStore store, ILogger logger, int maxCapacity = DefaultMaxCapacity)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxCapacity = Math.Max(32, maxCapacity);
            LoadFromStore();
        }

        // ================================================================
        // IKnowledgeBase
        // ================================================================

        /// <summary>
        /// 查询词条。O(1) 字典查找，大小写不敏感。
        /// 命中时自动更新访问时间和计数。
        /// </summary>
        public bool TryLookup(string term, out KnowledgeEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(term)) return false;

            if (_entries.TryGetValue(term, out entry))
            {
                entry.RecordAccess(++_accessSeq);
                _dirty = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 存储/覆盖知识条目。若 Term 已存在则智能合并：
        /// 保留更高 Confidence，合并 ContextTags，更新 Definition。
        /// 达到容量上限时触发 LRU 淘汰。
        /// </summary>
        public void Store(KnowledgeEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Term)) return;

            long seq = ++_accessSeq;

            if (_entries.TryGetValue(entry.Term, out var existing))
            {
                // 合并策略：取更高 Confidence 的来源
                if (entry.Confidence >= existing.Confidence)
                {
                    existing.Definition = entry.Definition;
                    existing.Confidence = entry.Confidence;
                    existing.Source = entry.Source;
                }

                // 合并 ContextTags（去重）
                if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                {
                    if (existing.ContextTags == null)
                        existing.ContextTags = new List<string>(entry.ContextTags);
                    else
                    {
                        foreach (var tag in entry.ContextTags)
                        {
                            if (!existing.ContextTags.Contains(tag))
                                existing.ContextTags.Add(tag);
                        }
                    }
                }

                existing.RecordAccess(seq);
            }
            else
            {
                // 容量检查 + LRU 淘汰
                if (_entries.Count >= _maxCapacity)
                {
                    EvictColdest(seq);
                }

                entry.CreatedSeq = seq;
                entry.LastAccessedSeq = seq;
                entry.AccessCount = 1;
                if (entry.ContextTags == null)
                    entry.ContextTags = new List<string>();

                _entries[entry.Term] = entry;
            }

            _dirty = true;
            SaveToStore();
        }

        /// <summary>
        /// 删除指定词条。
        /// </summary>
        public void Delete(string term)
        {
            if (string.IsNullOrEmpty(term)) return;
            if (_entries.Remove(term))
            {
                _dirty = true;
                SaveToStore();
            }
        }

        /// <summary>
        /// 按前缀列举词条（大小写不敏感）。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return ListAll();

            return _entries.Values
                .Where(e => e.Term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Term)
                .ToList();
        }

        /// <summary>
        /// 按语义标签筛选词条。
        /// </summary>
        /// <param name="tags">标签列表。命中任一标签即匹配。</param>
        public IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags)
        {
            if (tags == null || tags.Count == 0)
                return ListAll();

            return _entries.Values
                .Where(e => e.ContextTags != null
                    && tags.Any(t => e.ContextTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(e => e.Term)
                .ToList();
        }

        /// <summary>
        /// 列出全部词条。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListAll()
        {
            return _entries.Values.OrderBy(e => e.Term).ToList();
        }

        /// <summary>
        /// 返回当前词条总数。
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 当前最大容量。
        /// </summary>
        public int MaxCapacity => _maxCapacity;

        // ================================================================
        // LRU 淘汰
        // ================================================================

        private void EvictColdest(long currentSeq)
        {
            if (_entries.Count == 0) return;

            string coldestKey = null;
            float coldestScore = float.MaxValue;

            foreach (var kv in _entries)
            {
                float score = kv.Value.GetHeatScore(currentSeq);
                if (score < coldestScore)
                {
                    coldestScore = score;
                    coldestKey = kv.Key;
                }
            }

            if (coldestKey != null)
            {
                _entries.Remove(coldestKey);
                _logger.Message($"[NPCLife.Knowledge] LRU evicted term '{coldestKey}' (heat={coldestScore:F2})");
            }
        }

        // ================================================================
        // 持久化
        // ================================================================

        private void LoadFromStore()
        {
            try
            {
                var json = _store.FetchCache<string>(StoreKey, null);
                if (string.IsNullOrEmpty(json) || json == "[]")
                    return;

                var dicts = JsonParser.ParseObjectArray(json);
                foreach (var dict in dicts)
                {
                    var entry = DeserializeEntry(dict);
                    if (entry != null && !string.IsNullOrEmpty(entry.Term))
                        _entries[entry.Term] = entry;
                }

                _logger.Message($"[NPCLife.Knowledge] Loaded {_entries.Count} knowledge entries from cache.");
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.Knowledge] Failed to load knowledge: {e.Message}");
            }
        }

        private void SaveToStore()
        {
            if (!_dirty) return;
            _dirty = false;

            try
            {
                var entryJsons = new List<string>(_entries.Count);
                foreach (var entry in _entries.Values)
                    entryJsons.Add(SerializeEntry(entry));

                var sb = new System.Text.StringBuilder(entryJsons.Count * 128 + 4);
                sb.Append('[');
                for (int i = 0; i < entryJsons.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(entryJsons[i]);
                }
                sb.Append(']');

                _store.Cache(StoreKey, sb.ToString());
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.Knowledge] Failed to save knowledge: {e.Message}");
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeEntry(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definition", entry.Definition ?? "");
            w.Prop("source", entry.Source.ToString());
            w.Prop("confidence", entry.Confidence, "F3");
            w.Prop("createdSeq", entry.CreatedSeq);
            w.Prop("lastAccessedSeq", entry.LastAccessedSeq);
            w.Prop("accessCount", entry.AccessCount);

            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);

            return w.Close();
        }

        private static KnowledgeEntry DeserializeEntry(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0) return null;

            var entry = new KnowledgeEntry
            {
                Term = data.TryGetValue("term", out var v) ? v : null,
                Definition = data.TryGetValue("definition", out v) ? v : "",
                Source = ParseSource(data.TryGetValue("source", out v) ? v : "LegacyCache"),
                Confidence = data.TryGetValue("confidence", out v) && float.TryParse(v,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var conf) ? conf : 0.5f,
                CreatedSeq = data.TryGetValue("createdSeq", out v) && long.TryParse(v, out var cs) ? cs : 0,
                LastAccessedSeq = data.TryGetValue("lastAccessedSeq", out v) && long.TryParse(v, out var ls) ? ls : 0,
                AccessCount = data.TryGetValue("accessCount", out v) && int.TryParse(v, out var ac) ? ac : 0
            };

            // ContextTags: JSON 字符串数组
            if (data.TryGetValue("contextTags", out var tagsJson) && !string.IsNullOrEmpty(tagsJson))
            {
                entry.ContextTags = JsonParser.ParseStringArray(tagsJson);
            }
            else
            {
                entry.ContextTags = new List<string>();
            }

            return entry;
        }

        private static KnowledgeSource ParseSource(string s)
        {
            if (string.IsNullOrEmpty(s)) return KnowledgeSource.LegacyCache;
            if (Enum.TryParse<KnowledgeSource>(s, true, out var result))
                return result;
            return KnowledgeSource.LegacyCache;
        }
    }
}
