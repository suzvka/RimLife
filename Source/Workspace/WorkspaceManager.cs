using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife.Workspace
{
    /// <summary>
    /// 工作空间管理器。负责上下文空间的全部生命周期：
    /// 创建、查询、挂起/恢复、完成/废弃、分支、合并、事件推送。
    /// 通过 RimLifeCore.SaveStore 持久化到存档文件。
    /// </summary>
    public class WorkspaceManager
    {
        private readonly List<WorkspaceState> _workspaces = new List<WorkspaceState>();
        private readonly IPersistentStore _store;
        private const string StoreKey = "rimlife_workspaces";

        /// <summary>
        /// 创建 WorkspaceManager 实例并尝试从存档加载已有工作空间。
        /// </summary>
        /// <param name="store">持久化存储（SaveStore）。</param>
        public WorkspaceManager(IPersistentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            LoadFromStore();
        }

        // ================================================================
        // CRUD
        // ================================================================

        /// <summary>
        /// 创建新的工作空间。
        /// </summary>
        /// <param name="label">人类可读标签。</param>
        /// <param name="colonistIds">关联的殖民者 ThingID 列表。</param>
        /// <param name="tags">语义标签列表。</param>
        /// <returns>创建的工作空间状态。</returns>
        public WorkspaceState Create(string label, List<string> colonistIds, List<string> tags)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var ws = new WorkspaceState
            {
                Id = Guid.NewGuid().ToString("D"),
                Label = label ?? "Unnamed",
                Status = WorkspaceStatus.Active,
                ParentId = null,
                MergedFromIds = new List<string>(),
                ColonistIds = colonistIds ?? new List<string>(),
                Tags = tags ?? new List<string>(),
                PinnedEvents = new List<WorkspaceEvent>(),
                CreatedAtTick = tick,
                LastActivityTick = tick,
                Outcome = null
            };

            _workspaces.Add(ws);
            SaveToStore();

            Log.Message($"[RimLife.Workspace] Created workspace '{ws.Label}' (id={ws.Id})");
            return ws;
        }

        /// <summary>
        /// 按 ID 获取工作空间。
        /// </summary>
        public WorkspaceState Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _workspaces.FirstOrDefault(w => w.Id == id);
        }

        /// <summary>
        /// 列出工作空间，可按状态过滤。
        /// </summary>
        /// <param name="status">过滤状态，null 表示全部。</param>
        public IReadOnlyList<WorkspaceState> List(WorkspaceStatus? status = null)
        {
            if (status.HasValue)
                return _workspaces.Where(w => w.Status == status.Value).ToList();
            return _workspaces.ToList();
        }

        /// <summary>
        /// 获取所有活跃状态的工作空间。
        /// </summary>
        public IReadOnlyList<WorkspaceState> GetActive()
        {
            return List(WorkspaceStatus.Active);
        }

        /// <summary>
        /// 更新工作空间状态。
        /// </summary>
        /// <param name="id">工作空间 ID。</param>
        /// <param name="newStatus">新状态。</param>
        /// <param name="outcome">结束原因（Completed/Abandoned 时有效）。</param>
        /// <returns>是否成功。</returns>
        public bool UpdateStatus(string id, WorkspaceStatus newStatus, string outcome = null)
        {
            var ws = Get(id);
            if (ws == null) return false;

            // 验证状态转换合法性
            if (!IsValidTransition(ws.Status, newStatus))
            {
                Log.Warning($"[RimLife.Workspace] Invalid status transition for '{ws.Label}': {ws.Status} → {newStatus}");
                return false;
            }

            ws.Status = newStatus;
            ws.LastActivityTick = Find.TickManager?.TicksGame ?? ws.LastActivityTick;
            if (outcome != null)
                ws.Outcome = outcome;

            SaveToStore();
            Log.Message($"[RimLife.Workspace] Workspace '{ws.Label}' status: {newStatus}");
            return true;
        }

        // ================================================================
        // 分支 / 合并
        // ================================================================

        /// <summary>
        /// 从父工作空间分叉创建新的子工作空间。
        /// 复制父空间的事件列表和角色列表。
        /// </summary>
        /// <param name="parentId">父工作空间 ID。</param>
        /// <param name="newLabel">新工作空间标签。</param>
        /// <returns>新创建的子工作空间，失败返回 null。</returns>
        public WorkspaceState Branch(string parentId, string newLabel)
        {
            var parent = Get(parentId);
            if (parent == null)
            {
                Log.Warning($"[RimLife.Workspace] Branch failed: parent workspace '{parentId}' not found.");
                return null;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;

            var child = new WorkspaceState
            {
                Id = Guid.NewGuid().ToString("D"),
                Label = newLabel ?? $"{parent.Label} (branch)",
                Status = WorkspaceStatus.Active,
                ParentId = parentId,
                MergedFromIds = new List<string>(),
                ColonistIds = new List<string>(parent.ColonistIds ?? new List<string>()),
                Tags = new List<string>(parent.Tags ?? new List<string>()),
                PinnedEvents = new List<WorkspaceEvent>(parent.PinnedEvents ?? new List<WorkspaceEvent>()),
                CreatedAtTick = tick,
                LastActivityTick = tick,
                Outcome = null
            };

            _workspaces.Add(child);
            SaveToStore();

            Log.Message($"[RimLife.Workspace] Branched workspace '{child.Label}' (id={child.Id}) from '{parent.Label}'");
            return child;
        }

        /// <summary>
        /// 将源工作空间的事件合并到目标工作空间，然后废弃源空间。
        /// </summary>
        /// <param name="sourceId">源工作空间 ID。</param>
        /// <param name="targetId">目标工作空间 ID。</param>
        /// <returns>是否成功。</returns>
        public bool Merge(string sourceId, string targetId)
        {
            var source = Get(sourceId);
            var target = Get(targetId);

            if (source == null || target == null)
            {
                Log.Warning($"[RimLife.Workspace] Merge failed: source '{sourceId}' or target '{targetId}' not found.");
                return false;
            }

            if (source.Id == target.Id)
            {
                Log.Warning($"[RimLife.Workspace] Merge failed: source and target are the same workspace.");
                return false;
            }

            // 合并事件（按 tick 排序后追加，去重）
            var existingIds = new HashSet<string>(target.PinnedEvents.Select(e => e.EventId));
            foreach (var evt in source.PinnedEvents)
            {
                if (!existingIds.Contains(evt.EventId))
                {
                    target.PinnedEvents.Add(evt);
                    existingIds.Add(evt.EventId);
                }
            }
            target.PinnedEvents = target.PinnedEvents.OrderBy(e => e.Tick).ToList();

            // 记录合并来源
            if (target.MergedFromIds == null)
                target.MergedFromIds = new List<string>();
            target.MergedFromIds.Add(sourceId);

            // 合并角色列表（去重）
            if (source.ColonistIds != null)
            {
                foreach (var cid in source.ColonistIds)
                {
                    if (!target.ColonistIds.Contains(cid))
                        target.ColonistIds.Add(cid);
                }
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            target.LastActivityTick = tick;

            // 废弃源空间
            source.Status = WorkspaceStatus.Abandoned;
            source.LastActivityTick = tick;
            source.Outcome = $"Merged into '{target.Label}' ({target.Id})";

            SaveToStore();
            Log.Message($"[RimLife.Workspace] Merged '{source.Label}' into '{target.Label}'");
            return true;
        }

        // ================================================================
        // 事件推送
        // ================================================================

        /// <summary>
        /// 将事件数据复制到指定工作空间。
        /// </summary>
        /// <param name="workspaceId">目标工作空间 ID。</param>
        /// <param name="evt">游戏事件。</param>
        /// <returns>是否成功。</returns>
        public bool PushEvent(string workspaceId, IGameEvent evt)
        {
            if (evt == null) return false;

            var ws = Get(workspaceId);
            if (ws == null)
            {
                Log.Warning($"[RimLife.Workspace] PushEvent failed: workspace '{workspaceId}' not found.");
                return false;
            }

            if (ws.Status != WorkspaceStatus.Active)
            {
                Log.Warning($"[RimLife.Workspace] PushEvent failed: workspace '{ws.Label}' is not Active (status={ws.Status}).");
                return false;
            }

            // 去重：同 eventId 不重复添加
            if (ws.PinnedEvents.Any(e => e.EventId == evt.EventID))
                return true;

            ws.PinnedEvents.Add(WorkspaceEvent.From(evt));
            ws.LastActivityTick = Find.TickManager?.TicksGame ?? ws.LastActivityTick;

            SaveToStore();
            return true;
        }

        // ================================================================
        // 持久化
        // ================================================================

        private void SaveToStore()
        {
            try
            {
                var wsJsons = new List<string>();
                foreach (var ws in _workspaces)
                    wsJsons.Add(SerializeWorkspace(ws));

                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < wsJsons.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(wsJsons[i]);
                }
                sb.Append(']');

                _store.Store(StoreKey, sb.ToString());
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.Workspace] Failed to save workspaces: {e.Message}");
            }
        }

        private void LoadFromStore()
        {
            try
            {
                var json = _store.Retrieve<string>(StoreKey, null);
                if (string.IsNullOrEmpty(json) || json == "[]")
                    return;

                var dicts = JsonParser.ParseObjectArray(json);
                foreach (var dict in dicts)
                {
                    var ws = DeserializeWorkspace(dict);
                    if (ws != null)
                        _workspaces.Add(ws);
                }

                Log.Message($"[RimLife.Workspace] Loaded {_workspaces.Count} workspaces from save.");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.Workspace] Failed to load workspaces: {e.Message}");
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeWorkspace(WorkspaceState ws)
        {
            var w = new JsonWriter(1024);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);

            // MergedFromIds
            if (ws.MergedFromIds != null && ws.MergedFromIds.Count > 0)
                w.Array("mergedFromIds", ws.MergedFromIds);

            // ColonistIds
            if (ws.ColonistIds != null && ws.ColonistIds.Count > 0)
                w.Array("colonistIds", ws.ColonistIds);

            // Tags
            if (ws.Tags != null && ws.Tags.Count > 0)
                w.Array("tags", ws.Tags);

            // PinnedEvents
            if (ws.PinnedEvents != null && ws.PinnedEvents.Count > 0)
            {
                var eventJsons = new List<string>();
                foreach (var evt in ws.PinnedEvents)
                    eventJsons.Add(SerializeWorkspaceEvent(evt));
                w.ArrayRaw("pinnedEvents", eventJsons);
            }

            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            return w.Close();
        }

        private static string SerializeWorkspaceEvent(WorkspaceEvent evt)
        {
            var w = new JsonWriter(256);
            w.Prop("eventId", evt.EventId ?? "");
            w.Prop("defName", evt.DefName ?? "");
            w.Array("tags", evt.Tags);
            w.Prop("tick", evt.Tick);
            w.Prop("severity", evt.Severity ?? "");
            w.Prop("mapHint", evt.MapHint ?? "");

            // Actors
            if (evt.Actors != null && evt.Actors.Count > 0)
            {
                var actorJsons = new List<string>();
                foreach (var a in evt.Actors)
                {
                    var aw = new JsonWriter(128);
                    aw.Prop("id", a.ID ?? "");
                    aw.Prop("name", a.Name ?? "");
                    aw.Prop("role", a.Role ?? "");
                    aw.Prop("refType", a.RefType ?? "");
                    actorJsons.Add(aw.Close());
                }
                w.ArrayRaw("actors", actorJsons);
            }

            // Payload
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                var pw = new JsonWriter(256);
                foreach (var kv in evt.Payload)
                    pw.Prop(kv.Key, kv.Value ?? "");
                w.PropRaw("payload", pw.Close());
            }

            return w.Close();
        }

        private static WorkspaceState DeserializeWorkspace(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0) return null;

            var ws = new WorkspaceState
            {
                Id = data.TryGetValue("id", out var v) ? v : Guid.NewGuid().ToString("D"),
                Label = data.TryGetValue("label", out v) ? v : "Unnamed",
                Status = ParseStatus(data.TryGetValue("status", out v) ? v : "Active"),
                ParentId = data.TryGetValue("parentId", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null,
                MergedFromIds = DeserializeStringList(data.TryGetValue("mergedFromIds", out v) ? v : null),
                ColonistIds = DeserializeStringList(data.TryGetValue("colonistIds", out v) ? v : null),
                Tags = DeserializeStringList(data.TryGetValue("tags", out v) ? v : null),
                PinnedEvents = DeserializeWorkspaceEvents(data.TryGetValue("pinnedEvents", out v) ? v : null),
                CreatedAtTick = data.TryGetValue("createdAtTick", out v) && int.TryParse(v, out var tick) ? tick : 0,
                LastActivityTick = data.TryGetValue("lastActivityTick", out v) && int.TryParse(v, out var lt) ? lt : 0,
                Outcome = data.TryGetValue("outcome", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null
            };

            return ws;
        }

        private static List<WorkspaceEvent> DeserializeWorkspaceEvents(string json)
        {
            var result = new List<WorkspaceEvent>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            var eventDicts = JsonParser.ParseObjectArray(json);
            foreach (var dict in eventDicts)
            {
                var evt = new WorkspaceEvent
                {
                    EventId = dict.TryGetValue("eventId", out var v) ? v : "?",
                    DefName = dict.TryGetValue("defName", out v) ? v : "?",
                    Tags = DeserializeStringList(dict.TryGetValue("tags", out v) ? v : null),
                    Tick = dict.TryGetValue("tick", out v) && int.TryParse(v, out var t) ? t : 0,
                    Severity = dict.TryGetValue("severity", out v) ? v : "Minor",
                    MapHint = dict.TryGetValue("mapHint", out v) ? v : ""
                };

                // Actors: nested JSON array
                var actors = new List<EventActorRef>();
                if (dict.TryGetValue("actors", out var actorsJson) && !string.IsNullOrEmpty(actorsJson))
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
                evt.Actors = actors;

                // Payload: nested JSON object
                if (dict.TryGetValue("payload", out var payloadJson) && !string.IsNullOrEmpty(payloadJson))
                {
                    evt.Payload = JsonParser.ParseDict(payloadJson);
                }
                else
                {
                    evt.Payload = new Dictionary<string, string>();
                }

                result.Add(evt);
            }

            return result;
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static WorkspaceStatus ParseStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return WorkspaceStatus.Active;
            if (Enum.TryParse<WorkspaceStatus>(s, true, out var status))
                return status;
            return WorkspaceStatus.Active;
        }

        private static List<string> DeserializeStringList(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            json = json.Trim();
            int start = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '"')
                {
                    start = i + 1;
                    i++;
                    while (i < json.Length && json[i] != '"')
                        i++;
                    if (start <= i)
                        result.Add(JsonParser.UnescapeJson(json.Substring(start, i - start)));
                }
            }
            return result;
        }

        /// <summary>
        /// 验证工作空间状态转换是否合法。
        /// Active → Suspended/Completed/Abandoned
        /// Suspended → Active/Completed/Abandoned
        /// Completed/Abandoned → (不可转换)
        /// </summary>
        private static bool IsValidTransition(WorkspaceStatus from, WorkspaceStatus to)
        {
            if (from == to) return true;

            switch (from)
            {
                case WorkspaceStatus.Active:
                case WorkspaceStatus.Suspended:
                    return to == WorkspaceStatus.Active
                        || to == WorkspaceStatus.Suspended
                        || to == WorkspaceStatus.Completed
                        || to == WorkspaceStatus.Abandoned;

                case WorkspaceStatus.Completed:
                case WorkspaceStatus.Abandoned:
                    return false; // 终态不可逆转

                default:
                    return false;
            }
        }
    }
}
