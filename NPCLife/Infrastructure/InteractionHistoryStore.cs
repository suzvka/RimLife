using NPCLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NPCLife.Cards;
using NPCLife.Core;

namespace NPCLife.Infrastructure
{
    /// <summary>
    /// IInteractionStore 的 RimWorld 实现。
    /// append-only 流水，不裁剪，持久化到存档文件。
    /// 语义层 KV 由上层按需触发计算，写入 CacheStore。
    /// </summary>
    public class InteractionHistoryStore : IInteractionStore, IDisposable
    {
        private readonly List<InteractionRecord> _records = new List<InteractionRecord>();
        private readonly IAuthorityStore _store;
        private readonly ILogger _logger;
        private const string StoreKey = "rimlife_interactions";

        /// <summary>
        /// 创建交互历史存储实例。
        /// </summary>
        /// <param name="store">持久化存储（用于存档文件读写）。</param>
        /// <param name="logger">日志接口（可选）。</param>
        public InteractionHistoryStore(IAuthorityStore store, ILogger logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger;
            LoadFromStore();
        }

        // ================================================================
        // IInteractionStore 实现
        // ================================================================

        public void Append(InteractionRecord record)
        {
            if (string.IsNullOrEmpty(record.InitiatorID) || string.IsNullOrEmpty(record.RecipientID))
                return;

            _records.Add(record);
            TotalAppended++;
        }

        public IReadOnlyList<InteractionRecord> Query(string pawnIdA, string pawnIdB, int? sinceTick = null, int? limit = null)
        {
            IEnumerable<InteractionRecord> result = _records;

            result = result.Where(r =>
                (r.InitiatorID == pawnIdA && r.RecipientID == pawnIdB) ||
                (r.InitiatorID == pawnIdB && r.RecipientID == pawnIdA));

            if (sinceTick.HasValue)
                result = result.Where(r => r.Tick >= sinceTick.Value);

            result = result.OrderBy(r => r.Tick);

            if (limit.HasValue && limit.Value > 0)
                result = result.Take(limit.Value);

            return result.ToList();
        }

        public IReadOnlyList<InteractionRecord> QueryByPawn(string pawnId, int? sinceTick = null, int? limit = null)
        {
            IEnumerable<InteractionRecord> result = _records;

            result = result.Where(r => r.InitiatorID == pawnId || r.RecipientID == pawnId);

            if (sinceTick.HasValue)
                result = result.Where(r => r.Tick >= sinceTick.Value);

            result = result.OrderBy(r => r.Tick);

            if (limit.HasValue && limit.Value > 0)
                result = result.Take(limit.Value);

            return result.ToList();
        }

        public int Count(string pawnIdA, string pawnIdB)
        {
            return _records.Count(r =>
                (r.InitiatorID == pawnIdA && r.RecipientID == pawnIdB) ||
                (r.InitiatorID == pawnIdB && r.RecipientID == pawnIdA));
        }

        public int TotalAppended { get; private set; }

        // ================================================================
        // 持久化
        // ================================================================

        public void Persist()
        {
            try
            {
                // 手动构建 JSON 数组字符串，以 string 类型存入 SaveStore
                // (Store<T> 的 SerializeValue 只支持基础类型，不能直接存 List<string>)
                var sb = new StringBuilder(_records.Count * 128 + 4);
                sb.Append('[');
                for (int i = 0; i < _records.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(SerializeRecord(_records[i]));
                }
                sb.Append(']');
                _store.Store(StoreKey, sb.ToString());
            }
            catch (Exception e)
            {
                _logger?.Warning($"[NPCLife.InteractionStore] Failed to save interactions: {e.Message}");
            }
        }

        private void LoadFromStore()
        {
            try
            {
                var json = _store.Retrieve<string>(StoreKey, null);
                if (string.IsNullOrEmpty(json) || json == "[]")
                    return;

                // 用 ParseObjectArray 解析 JSON 对象数组
                var dicts = JsonParser.ParseObjectArray(json);
                foreach (var dict in dicts)
                {
                    var rec = DictToRecord(dict);
                    _records.Add(rec);
                }
                TotalAppended = _records.Count;
                _logger?.Message($"[NPCLife.InteractionStore] Loaded {_records.Count} interactions from save.");
            }
            catch (Exception e)
            {
                _logger?.Warning($"[NPCLife.InteractionStore] Failed to load interactions: {e.Message}");
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeRecord(InteractionRecord rec)
        {
            var writer = new JsonWriter(128);
            writer.Prop("tick", rec.Tick);
            writer.Prop("initiatorId", rec.InitiatorID ?? "");
            writer.Prop("recipientId", rec.RecipientID ?? "");
            writer.Prop("interactionDef", rec.InteractionDef ?? "");
            writer.Prop("outcome", rec.Outcome ?? "");
            return writer.Close();
        }

        private static InteractionRecord DictToRecord(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0)
                return default;

            return new InteractionRecord
            {
                Tick = data.TryGetValue("tick", out var tv) && int.TryParse(tv, out var tick) ? tick : 0,
                InitiatorID = data.TryGetValue("initiatorId", out var iid) ? iid : "?",
                RecipientID = data.TryGetValue("recipientId", out var rid) ? rid : "?",
                InteractionDef = data.TryGetValue("interactionDef", out var idef) ? idef : "?",
                Outcome = data.TryGetValue("outcome", out var o) ? o : ""
            };
        }

        // ================================================================
        // IDisposable
        // ================================================================

        /// <summary>清空内存中的记录。</summary>
        public void Dispose()
        {
            _records.Clear();
            TotalAppended = 0;
        }
    }
}
