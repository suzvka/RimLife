using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using RimLife.Cards;
using RimLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IEventLog 的 RimWorld 实现。替代固定容量的 EventBuffer。
    /// 事件 append-only，通过 IPersistentStore 持久化到存档文件。
    /// 支持按维度查询、分页和计数。
    /// </summary>
    public class RimWorldEventLog : IEventLog
    {
        private readonly List<IGameEvent> _events = new List<IGameEvent>();
        private readonly IPersistentStore _store;
        private readonly int _maxCapacity;
        private const string StoreKey = "rimlife_eventlog";

        /// <summary>
        /// 创建事件日志实例。
        /// </summary>
        /// <param name="store">持久化存储（用于存档文件读写）。</param>
        /// <param name="maxCapacity">最大容量，超出时裁剪旧事件。默认 500。</param>
        public RimWorldEventLog(IPersistentStore store, int maxCapacity = 500)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _maxCapacity = Math.Max(64, maxCapacity);
            LoadFromStore();
        }

        // ================================================================
        // IEventLog 实现
        // ================================================================

        public void Append(IGameEvent evt)
        {
            if (evt == null) return;

            _events.Add(evt);
            Log.Message($"[RimLife.EventLog] +{evt.Category}/{evt.DefName} tick={evt.Tick} total={TotalAppended}");

            // 裁剪旧事件
            while (_events.Count > _maxCapacity)
            {
                // 优先裁剪 Minor 事件
                int removeIdx = -1;
                for (int i = 0; i < _events.Count; i++)
                {
                    if (_events[i].Severity == "Minor")
                    {
                        removeIdx = i;
                        break;
                    }
                }
                if (removeIdx < 0) removeIdx = 0; // 没有 Minor 事件，删最旧的
                _events.RemoveAt(removeIdx);
            }

            SaveToStore();
        }

        public IReadOnlyList<IGameEvent> Query(EventQuery query)
        {
            if (query == null) query = EventQuery.All;

            IEnumerable<IGameEvent> result = _events;

            // 按类别筛选
            if (query.Category.HasValue)
                result = result.Where(e => e.Category == query.Category.Value);

            // 按时间范围筛选
            if (query.SinceTick.HasValue)
                result = result.Where(e => e.Tick >= query.SinceTick.Value);
            if (query.UntilTick.HasValue)
                result = result.Where(e => e.Tick < query.UntilTick.Value);

            // 按 Actor 筛选
            if (!string.IsNullOrEmpty(query.ActorId))
                result = result.Where(e => e.Actors != null && e.Actors.Any(a => a.ID == query.ActorId));

            // 按严重度筛选
            if (!string.IsNullOrEmpty(query.Severity))
                result = result.Where(e => e.Severity == query.Severity);

            // 按时间正序
            result = result.OrderBy(e => e.Tick);

            // 分页
            int offset = query.Offset ?? 0;
            if (offset > 0)
                result = result.Skip(offset);

            if (query.Limit.HasValue)
                result = result.Take(query.Limit.Value);

            return result.ToList();
        }

        public int Count(EventQuery query)
        {
            if (query == null) return _events.Count;

            // 复用过滤逻辑但不应用分页
            var q = new EventQuery
            {
                Category = query.Category,
                SinceTick = query.SinceTick,
                UntilTick = query.UntilTick,
                ActorId = query.ActorId,
                Severity = query.Severity,
                Limit = null,
                Offset = null
            };
            return Query(q).Count;
        }

        public IGameEvent Latest => _events.Count > 0 ? _events[_events.Count - 1] : null;

        public int TotalAppended { get; private set; }

        // ================================================================
        // 持久化
        // ================================================================

        private void SaveToStore()
        {
            try
            {
                // 将事件列表序列化为 JSON 字符串数组存入 Store
                var jsonList = new List<string>();
                foreach (var evt in _events)
                {
                    jsonList.Add(SerializeEvent(evt));
                }
                _store.Store(StoreKey, jsonList);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.EventLog] Failed to save events: {e.Message}");
            }
        }

        private void LoadFromStore()
        {
            try
            {
                var jsonList = _store.Retrieve<List<string>>(StoreKey, null);
                if (jsonList != null)
                {
                    foreach (var json in jsonList)
                    {
                        var evt = DeserializeEvent(json);
                        if (evt != null)
                            _events.Add(evt);
                    }
                    TotalAppended = _events.Count;
                    Log.Message($"[RimLife.EventLog] Loaded {_events.Count} events from save.");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.EventLog] Failed to load events: {e.Message}");
            }
        }

        // ================================================================
        // 事件序列化（JSON 格式）
        // ================================================================

        private static string SerializeEvent(IGameEvent evt)
        {
            var writer = new Framework.JsonWriter(512);
            writer.Prop("eventId", evt.EventID);
            writer.Prop("defName", evt.DefName);
            writer.Prop("category", evt.Category.ToString());
            writer.Prop("tick", evt.Tick);
            writer.Prop("severity", evt.Severity);
            writer.Prop("mapHint", evt.MapHint ?? "");

            // Actors
            if (evt.Actors != null && evt.Actors.Count > 0)
            {
                var actorJsons = evt.Actors.Select(a =>
                {
                    var aw = new Framework.JsonWriter(128);
                    aw.Prop("id", a.ID);
                    aw.Prop("name", a.Name);
                    aw.Prop("role", a.Role);
                    aw.Prop("refType", a.RefType);
                    return aw.Close();
                }).ToList();
                writer.ArrayRaw("actors", actorJsons);
            }

            // Payload
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                var pw = new Framework.JsonWriter(256);
                foreach (var kv in evt.Payload)
                {
                    pw.Prop(kv.Key, kv.Value ?? "");
                }
                writer.PropRaw("payload", pw.Close());
            }

            return writer.Close();
        }

        private static IGameEvent DeserializeEvent(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return null;

            var data = JsonParser.ParseDict(json);
            if (data.Count == 0) return null;

            return new SerializedGameEvent(data);
        }

        /// <summary>
        /// 从反序列化的 JSON 字典重建事件的轻量包装。
        /// </summary>
        private class SerializedGameEvent : IGameEvent
        {
            public string EventID { get; }
            public string DefName { get; }
            public EventCategory Category { get; }
            public int Tick { get; }
            public string Severity { get; }
            public IReadOnlyList<EventActorRef> Actors { get; }
            public string MapHint { get; }
            public IDictionary<string, string> Payload { get; }

            public SerializedGameEvent(Dictionary<string, string> data)
            {
                EventID = data.TryGetValue("eventId", out var v) ? v : "?";
                DefName = data.TryGetValue("defName", out v) ? v : "?";
                Category = data.TryGetValue("category", out v) && Enum.TryParse<EventCategory>(v, out var cat) ? cat : EventCategory.Anomaly;
                Tick = data.TryGetValue("tick", out v) && int.TryParse(v, out var t) ? t : 0;
                Severity = data.TryGetValue("severity", out v) ? v : "Minor";
                MapHint = data.TryGetValue("mapHint", out v) ? v : "";

                // Actors: 嵌套 JSON 数组
                var actors = new List<EventActorRef>();
                if (data.TryGetValue("actors", out var actorsJson) && !string.IsNullOrEmpty(actorsJson))
                {
                    var actorDicts = JsonParser.ParseObjectArray(actorsJson);
                    foreach (var ad in actorDicts)
                    {
                        actors.Add(new EventActorRef
                        {
                            ID = ad.TryGetValue("id", out var aid) ? aid : "?",
                            Name = ad.TryGetValue("name", out var nm) ? nm : "?",
                            Role = ad.TryGetValue("role", out var rl) ? rl : "Bystander",
                            RefType = ad.TryGetValue("refType", out var rt) ? rt : "Pawn"
                        });
                    }
                }
                Actors = actors;

                // Payload: 嵌套 JSON 对象
                if (data.TryGetValue("payload", out var payloadJson) && !string.IsNullOrEmpty(payloadJson))
                {
                    Payload = JsonParser.ParseDict(payloadJson);
                }
                else
                {
                    Payload = new Dictionary<string, string>();
                }
            }
        }
    }
}
