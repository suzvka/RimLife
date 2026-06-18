using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
namespace NPCLife.Workspace
{
    /// <summary>
    /// 工作空间管理器。职责：CRUD、分支/合并结构操作、事件路由。
    /// 工作空间内部组件（事件池、技能槽）通过 IWorkspace 接口访问，管理器不关心。
    /// 通过 RimLifeCore.SaveStore 持久化到存档文件。
    /// </summary>
    public class WorkspaceManager : IDisposable, IWorkspaceManager
    {
        private readonly List<WorkspaceImpl> _workspaces = new List<WorkspaceImpl>();
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
        private readonly IAuthorityStore _store;
        private readonly ILogger _logger;
        private readonly Func<string> _timeProvider;
        private readonly DriverConfig _driverConfig;
        private readonly ICardSerializer _serializer;
        private readonly Action<string> _onWorkspaceReady;
        private const string StoreKey = "rimlife_workspaces";

        public WorkspaceManager(IAuthorityStore store, ILogger logger, Func<string> timeProvider, DriverConfig driverConfig, ICardSerializer serializer = null, Action<string> onWorkspaceReady = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _driverConfig = driverConfig ?? DriverConfig.CreateDefault();
            _serializer = serializer ?? CardSerializer.Default;
            _onWorkspaceReady = onWorkspaceReady;
            LoadFromStore();
        }

        private string Now() => _timeProvider() ?? "";

        private void PublishUpdated(string workspaceId)
        {
            EventBus.Publish(FrameworkEvents.WorkspaceUpdated,
                EventArg.WithPayload(("workspaceId", workspaceId ?? "")));
        }

        public void Persist()
        {
            _rwLock.EnterReadLock();
            try
            {
                var wsJsons = new List<string>();
                foreach (var ws in _workspaces)
                    wsJsons.Add(SerializeWorkspace(ws.State));

                var sb = new StringBuilder("[");
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
                _logger.Warning($"[NPCLife.Workspace] Failed to save workspaces: {e.Message}");
            }
            finally { _rwLock.ExitReadLock(); }
        }

        private WorkspaceImpl Wrap(WorkspaceState state)
        {
            return new WorkspaceImpl(
                state,
                _driverConfig,
                _serializer,
                _timeProvider,
                publishUpdated: PublishUpdated,
                logger: _logger);
        }

        // ================================================================
        // CRUD
        // ================================================================

        public IWorkspace Create(string label, List<string> colonistIds, List<string> tags, WorkspaceRole createdByRole)
        {
            string now = Now();

            var ws = new WorkspaceState
            {
                Id = Guid.NewGuid().ToString("D"),
                Label = label ?? "Unnamed",
                Status = WorkspaceStatus.Active,
                CreatedByRole = createdByRole,
                ParentId = null,
                MergedFromIds = new List<string>(),
                ColonistIds = colonistIds ?? new List<string>(),
                Tags = tags ?? new List<string>(),
                Rounds = new List<WorkspaceRound>(),
                CurrentRecap = "",
                CreatedAt = now,
                LastActivityAt = now,
                ActiveSkillIds = new List<string>(),
                EventCache = new Dictionary<string, string>(),
                PendingEventIds = new List<string>(),
                PendingImportance = 0,
                Outcome = null,
                DirectorMessage = null
            };

            var impl = Wrap(ws);

            // 根据角色自动激活默认技能集
            var defaultSkills = RoleSkillProfile.GetDefaultSkillIds(createdByRole);
            foreach (var skillId in defaultSkills)
            {
                impl.SkillSlot.Activate(skillId);
            }

            _rwLock.EnterWriteLock();
            try
            {
                _workspaces.Add(impl);
            }
            finally { _rwLock.ExitWriteLock(); }

            _logger.Message($"[NPCLife.Workspace] Created workspace '{ws.Label}' (id={ws.Id}, role={createdByRole}, skills={string.Join(",", defaultSkills)})");
            EventBus.Publish(FrameworkEvents.WorkspaceCreated,
                EventArg.WithPayload(("workspaceId", ws.Id), ("label", ws.Label ?? "")));
            _onWorkspaceReady?.Invoke(ws.Id);
            return impl;
        }

        public IWorkspace Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _rwLock.EnterReadLock();
            try { return _workspaces.FirstOrDefault(w => w.Id == id); }
            finally { _rwLock.ExitReadLock(); }
        }

        public IReadOnlyList<IWorkspace> List(WorkspaceStatus? status = null)
        {
            _rwLock.EnterReadLock();
            try
            {
                if (status.HasValue)
                    return _workspaces.Where(w => w.Status == status.Value).Cast<IWorkspace>().ToList();
                return _workspaces.Cast<IWorkspace>().ToList();
            }
            finally { _rwLock.ExitReadLock(); }
        }

        public IReadOnlyList<IWorkspace> GetActive()
        {
            return List(WorkspaceStatus.Active);
        }

        public bool UpdateStatus(string id, WorkspaceStatus newStatus, string outcome = null)
        {
            var impl = GetImpl(id);
            if (impl == null) return false;

            if (!IsValidTransition(impl.Status, newStatus))
            {
                _logger.Warning($"[NPCLife.Workspace] Invalid status transition for '{impl.Label}': {impl.Status} → {newStatus}");
                return false;
            }

            impl.SetStatus(newStatus, outcome);

            _logger.Message($"[NPCLife.Workspace] Workspace '{impl.Label}' status: {newStatus}");

            if (newStatus == WorkspaceStatus.Completed || newStatus == WorkspaceStatus.Abandoned)
                EventBus.Publish(FrameworkEvents.WorkspaceClosed,
                    EventArg.WithPayload(("workspaceId", id), ("status", newStatus.ToString())));
            else
                PublishUpdated(id);

            return true;
        }

        // ================================================================
        // 分支
        // ================================================================

        public IWorkspace Branch(string parentId, string newLabel, string branchRecap, WorkspaceRole callerRole)
        {
            if (callerRole != WorkspaceRole.Director)
            {
                _logger.Warning($"[NPCLife.Workspace] Branch rejected: caller is {callerRole}, only Director can branch.");
                return null;
            }

            var parentImpl = GetImpl(parentId);
            if (parentImpl == null)
            {
                _logger.Warning($"[NPCLife.Workspace] Branch failed: parent workspace '{parentId}' not found.");
                return null;
            }

            var parent = parentImpl.State;
            string now = Now();

            var copiedRounds = new List<WorkspaceRound>(parent.Rounds ?? new List<WorkspaceRound>());
            int branchSeq = copiedRounds.Count;
            var branchRound = new WorkspaceRound
            {
                Seq = branchSeq,
                Type = RoundType.Branch,
                Recap = branchRecap ?? $"Forked from '{parent.Label}'",
                Narrative = "",
                CreatedAt = now,
                TriggerEventIds = new List<string>(),
                AuthorRole = callerRole,
                AuthorId = null
            };
            copiedRounds.Add(branchRound);

            var childSkillIds = new List<string>(parent.ActiveSkillIds ?? new List<string>());

            var child = new WorkspaceState
            {
                Id = Guid.NewGuid().ToString("D"),
                Label = newLabel ?? $"{parent.Label} (branch)",
                Status = WorkspaceStatus.Active,
                CreatedByRole = parent.CreatedByRole,
                ParentId = parentId,
                MergedFromIds = new List<string>(),
                ColonistIds = new List<string>(parent.ColonistIds ?? new List<string>()),
                Tags = new List<string>(parent.Tags ?? new List<string>()),
                Rounds = copiedRounds,
                CurrentRecap = branchRecap ?? "",
                CreatedAt = now,
                LastActivityAt = now,
                ActiveSkillIds = childSkillIds,
                EventCache = new Dictionary<string, string>(),
                PendingEventIds = new List<string>(),
                PendingImportance = 0,
                Outcome = null,
                DirectorMessage = null
            };

            var childImpl = Wrap(child);

            _rwLock.EnterWriteLock();
            try
            {
                _workspaces.Add(childImpl);
            }
            finally { _rwLock.ExitWriteLock(); }

            _logger.Message($"[NPCLife.Workspace] Branched workspace '{child.Label}' (id={child.Id}) from '{parent.Label}'");
            EventBus.Publish(FrameworkEvents.WorkspaceCreated,
                EventArg.WithPayload(("workspaceId", child.Id), ("label", child.Label ?? ""), ("parentId", parentId)));
            return childImpl;
        }

        // ================================================================
        // 合并
        // ================================================================

        public bool Merge(string sourceId, string targetId, string mergeRecap, WorkspaceRole callerRole)
        {
            if (callerRole != WorkspaceRole.Director)
            {
                _logger.Warning($"[NPCLife.Workspace] Merge rejected: caller is {callerRole}, only Director can merge.");
                return false;
            }

            var sourceImpl = GetImpl(sourceId);
            var targetImpl = GetImpl(targetId);

            if (sourceImpl == null || targetImpl == null)
            {
                _logger.Warning($"[NPCLife.Workspace] Merge failed: source '{sourceId}' or target '{targetId}' not found.");
                return false;
            }

            if (sourceId == targetId)
            {
                _logger.Warning($"[NPCLife.Workspace] Merge failed: source and target are the same workspace.");
                return false;
            }

            var source = sourceImpl.State;
            var target = targetImpl.State;

            var mergedRounds = new List<WorkspaceRound>(target.Rounds ?? new List<WorkspaceRound>());
            var existingSeqs = new HashSet<int>(mergedRounds.Select(r => r.Seq));

            if (source.Rounds != null)
            {
                foreach (var r in source.Rounds)
                {
                    if (!existingSeqs.Contains(r.Seq))
                    {
                        mergedRounds.Add(r);
                        existingSeqs.Add(r.Seq);
                    }
                }
            }
            mergedRounds = mergedRounds.OrderBy(r => r.Seq).ToList();

            int mergeSeq = mergedRounds.Count;
            string now = Now();
            var mergeRound = new WorkspaceRound
            {
                Seq = mergeSeq,
                Type = RoundType.Merge,
                Recap = mergeRecap ?? $"Merged from '{source.Label}' into '{target.Label}'",
                Narrative = "",
                CreatedAt = now,
                TriggerEventIds = new List<string>(),
                AuthorRole = callerRole,
                AuthorId = null
            };
            mergedRounds.Add(mergeRound);

            if (target.MergedFromIds == null)
                target.MergedFromIds = new List<string>();
            target.MergedFromIds.Add(sourceId);

            if (source.ColonistIds != null)
            {
                if (target.ColonistIds == null)
                    target.ColonistIds = new List<string>();
                foreach (var cid in source.ColonistIds)
                {
                    if (!target.ColonistIds.Contains(cid))
                        target.ColonistIds.Add(cid);
                }
            }

            if (source.Tags != null)
            {
                if (target.Tags == null)
                    target.Tags = new List<string>();
                foreach (var tag in source.Tags)
                {
                    if (!target.Tags.Contains(tag))
                        target.Tags.Add(tag);
                }
            }

            target.Rounds = mergedRounds;
            target.CurrentRecap = mergeRecap ?? "";
            target.LastActivityAt = now;

            if (source.ActiveSkillIds != null && source.ActiveSkillIds.Count > 0)
            {
                if (target.ActiveSkillIds == null)
                    target.ActiveSkillIds = new List<string>();
                foreach (var skillId in source.ActiveSkillIds)
                {
                    if (!target.ActiveSkillIds.Contains(skillId))
                        target.ActiveSkillIds.Add(skillId);
                }
            }

            source.Status = WorkspaceStatus.Abandoned;
            source.LastActivityAt = now;
            source.Outcome = $"Merged into '{target.Label}' ({target.Id})";

            _logger.Message($"[NPCLife.Workspace] Merged '{source.Label}' into '{target.Label}'");

            PublishUpdated(targetId);
            PublishUpdated(sourceId);
            return true;
        }

        // ================================================================
        // 事件路由
        // ================================================================

        public bool RouteEvents(string workspaceId, IReadOnlyList<IGameEvent> events)
        {
            var ws = GetImpl(workspaceId);
            if (ws == null || ws.Status != WorkspaceStatus.Active) return false;
            if (events == null || events.Count == 0) return false;

            foreach (var evt in events)
                ws.EventPool.Append(evt);

            return true;
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        private WorkspaceImpl GetImpl(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _rwLock.EnterReadLock();
            try { return _workspaces.FirstOrDefault(w => w.Id == id); }
            finally { _rwLock.ExitReadLock(); }
        }

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
                    return false;
                default:
                    return false;
            }
        }

        // ================================================================
        // 持久化
        // ================================================================

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
                        _workspaces.Add(Wrap(ws));
                }

                _logger.Message($"[NPCLife.Workspace] Loaded {_workspaces.Count} workspaces from save.");

                foreach (var ws in _workspaces)
                {
                    if (ws.Status == WorkspaceStatus.Active)
                        _onWorkspaceReady?.Invoke(ws.Id);
                }
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.Workspace] Failed to load workspaces: {e.Message}");
            }
        }

        // ================================================================
        // 序列化（与之前完全一致）
        // ================================================================

        private string SerializeWorkspace(WorkspaceState ws)
        {
            var w = new JsonWriter(1024);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            w.Prop("createdByRole", ws.CreatedByRole.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            if (ws.MergedFromIds != null && ws.MergedFromIds.Count > 0)
                w.Array("mergedFromIds", ws.MergedFromIds);
            if (ws.ColonistIds != null && ws.ColonistIds.Count > 0)
                w.Array("colonistIds", ws.ColonistIds);
            if (ws.Tags != null && ws.Tags.Count > 0)
                w.Array("tags", ws.Tags);
            if (!string.IsNullOrEmpty(ws.CurrentRecap))
                w.Prop("currentRecap", ws.CurrentRecap);
            if (ws.ActiveSkillIds != null && ws.ActiveSkillIds.Count > 0)
                w.Array("activeSkillIds", ws.ActiveSkillIds);
            if (ws.EventCache != null && ws.EventCache.Count > 0)
                w.PropRaw("eventCache", _serializer.SerializeEventCache(ws.EventCache));
            if (ws.PendingEventIds != null && ws.PendingEventIds.Count > 0)
                w.Array("pendingEventIds", ws.PendingEventIds);
            w.Prop("pendingImportance", ws.PendingImportance, "F2");
            if (ws.Rounds != null && ws.Rounds.Count > 0)
            {
                var roundJsons = new List<string>();
                foreach (var r in ws.Rounds)
                    roundJsons.Add(SerializeRound(r));
                w.ArrayRaw("rounds", roundJsons);
            }
            w.Prop("createdAt", ws.CreatedAt ?? "");
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);
            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                w.Prop("directorMessage", ws.DirectorMessage);
            return w.Close();
        }

        private static string SerializeRound(WorkspaceRound r)
        {
            var w = new JsonWriter(512);
            w.Prop("seq", r.Seq);
            w.Prop("type", r.Type.ToString());
            w.Prop("recap", r.Recap ?? "");
            if (!string.IsNullOrEmpty(r.Narrative))
                w.Prop("narrative", r.Narrative);
            w.Prop("createdAt", r.CreatedAt ?? "");
            if (r.TriggerEventIds != null && r.TriggerEventIds.Count > 0)
                w.Array("triggerEventIds", r.TriggerEventIds);
            w.Prop("authorRole", r.AuthorRole.ToString());
            if (!string.IsNullOrEmpty(r.AuthorId))
                w.Prop("authorId", r.AuthorId);
            return w.Close();
        }

        private WorkspaceState DeserializeWorkspace(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0) return null;

            var ws = new WorkspaceState
            {
                Id = data.TryGetValue("id", out var v) ? v : Guid.NewGuid().ToString("D"),
                Label = data.TryGetValue("label", out v) ? v : "Unnamed",
                Status = ParseStatus(data.TryGetValue("status", out v) ? v : "Active"),
                CreatedByRole = ParseRole(data.TryGetValue("createdByRole", out v) ? v : "Director"),
                ParentId = data.TryGetValue("parentId", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null,
                MergedFromIds = DeserializeStringList(data.TryGetValue("mergedFromIds", out v) ? v : null),
                ColonistIds = DeserializeStringList(data.TryGetValue("colonistIds", out v) ? v : null),
                Tags = DeserializeStringList(data.TryGetValue("tags", out v) ? v : null),
                CurrentRecap = data.TryGetValue("currentRecap", out v) ? v : "",
                Rounds = DeserializeRounds(data.TryGetValue("rounds", out v) ? v : null),
                CreatedAt = data.TryGetValue("createdAt", out v) ? v : "",
                LastActivityAt = data.TryGetValue("lastActivityAt", out v) ? v : "",
                ActiveSkillIds = DeserializeStringList(data.TryGetValue("activeSkillIds", out v) ? v : null),
                EventCache = _serializer.DeserializeEventCache(data.TryGetValue("eventCache", out v) ? v : "{}"),
                PendingEventIds = DeserializeStringList(data.TryGetValue("pendingEventIds", out v) ? v : null),
                PendingImportance = data.TryGetValue("pendingImportance", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var imp) ? imp : 0,
                Outcome = data.TryGetValue("outcome", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null,
                DirectorMessage = data.TryGetValue("directorMessage", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null
            };

            return ws;
        }

        private static List<WorkspaceRound> DeserializeRounds(string json)
        {
            var result = new List<WorkspaceRound>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            var roundDicts = JsonParser.ParseObjectArray(json);
            foreach (var dict in roundDicts)
            {
                var r = new WorkspaceRound
                {
                    Seq = dict.TryGetValue("seq", out var v) && int.TryParse(v, out var s) ? s : result.Count,
                    Type = ParseRoundType(dict.TryGetValue("type", out v) ? v : "Normal"),
                    Recap = dict.TryGetValue("recap", out v) ? v : "",
                    Narrative = dict.TryGetValue("narrative", out v) ? v : "",
                    CreatedAt = dict.TryGetValue("createdAt", out v) ? v : "",
                    TriggerEventIds = DeserializeStringList(dict.TryGetValue("triggerEventIds", out v) ? v : null),
                    AuthorRole = ParseRole(dict.TryGetValue("authorRole", out v) ? v : "Screenwriter"),
                    AuthorId = dict.TryGetValue("authorId", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null
                };
                result.Add(r);
            }
            return result;
        }

        private static WorkspaceStatus ParseStatus(string s)
        {
            if (string.IsNullOrEmpty(s)) return WorkspaceStatus.Active;
            if (Enum.TryParse<WorkspaceStatus>(s, true, out var status))
                return status;
            return WorkspaceStatus.Active;
        }

        private static RoundType ParseRoundType(string s)
        {
            if (string.IsNullOrEmpty(s)) return RoundType.Normal;
            if (Enum.TryParse<RoundType>(s, true, out var rt))
                return rt;
            return RoundType.Normal;
        }

        private static WorkspaceRole ParseRole(string s)
        {
            if (string.IsNullOrEmpty(s)) return WorkspaceRole.Director;
            if (Enum.TryParse<WorkspaceRole>(s, true, out var role))
                return role;
            return WorkspaceRole.Director;
        }

        private static List<string> DeserializeStringList(string json)
        {
            return JsonParser.ParseStringArray(json);
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            try { Persist(); } catch { }
            _rwLock.EnterWriteLock();
            try { _workspaces.Clear(); }
            finally { _rwLock.ExitWriteLock(); }
            _rwLock.Dispose();
        }
    }
}
