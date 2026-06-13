using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimLife.Workspace
{
    /// <summary>
    /// 工作空间管理器。负责上下文空间的全部生命周期：
    /// 创建、查询、挂起/恢复、完成/废弃、分支、合并、回合推送。
    /// 工作空间只存 Agent 的写作日志（Rounds），不重复存储事件数据。
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
                Rounds = new List<WorkspaceRound>(),
                CurrentRecap = "",
                CreatedAtTick = tick,
                LastActivityTick = tick,
                ActiveSkillIds = new List<string>(),
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
        // 回合推送
        // ================================================================

        /// <summary>
        /// 推送一个新轮次到工作空间。Agent 每轮生成结束后调用。
        /// </summary>
        /// <param name="workspaceId">目标工作空间 ID。</param>
        /// <param name="recap">Agent 写的前情提要。</param>
        /// <param name="narrative">Agent 写的正式台词。</param>
        /// <param name="triggerEventIds">本轮触发的事件 ID 列表（溯源用）。</param>
        /// <returns>是否成功。</returns>
        public bool PushRound(string workspaceId, string recap, string narrative, List<string> triggerEventIds)
        {
            if (string.IsNullOrEmpty(recap) && string.IsNullOrEmpty(narrative)) return false;

            var ws = Get(workspaceId);
            if (ws == null)
            {
                Log.Warning($"[RimLife.Workspace] PushRound failed: workspace '{workspaceId}' not found.");
                return false;
            }

            if (ws.Status != WorkspaceStatus.Active)
            {
                Log.Warning($"[RimLife.Workspace] PushRound failed: workspace '{ws.Label}' is not Active (status={ws.Status}).");
                return false;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int nextSeq = (ws.Rounds?.Count) ?? 0;

            var round = new WorkspaceRound
            {
                Seq = nextSeq,
                Type = RoundType.Normal,
                Recap = recap ?? "",
                Narrative = narrative ?? "",
                CreatedAtTick = tick,
                TriggerEventIds = triggerEventIds ?? new List<string>()
            };

            if (ws.Rounds == null)
                ws.Rounds = new List<WorkspaceRound>();
            ws.Rounds.Add(round);

            // 更新 CurrentRecap 供下一轮注入 prompt
            ws.CurrentRecap = recap ?? "";
            ws.LastActivityTick = tick;

            SaveToStore();
            return true;
        }

        // ================================================================
        // 分支
        // ================================================================

        /// <summary>
        /// 从父工作空间分叉创建新的子工作空间。
        /// 拷贝父空间的 Rounds 历史，并追加一条 Branch 轮记录分支声明。
        /// </summary>
        /// <param name="parentId">父工作空间 ID。</param>
        /// <param name="newLabel">新工作空间标签。</param>
        /// <param name="branchRecap">Agent 写的分支前情提要（作为子空间的初始 CurrentRecap）。</param>
        /// <returns>新创建的子工作空间，失败返回 null。</returns>
        public WorkspaceState Branch(string parentId, string newLabel, string branchRecap)
        {
            var parent = Get(parentId);
            if (parent == null)
            {
                Log.Warning($"[RimLife.Workspace] Branch failed: parent workspace '{parentId}' not found.");
                return null;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;

            // 拷贝父空间的 Rounds 历史（浅拷贝，WorkspaceRound 是值类型 struct）
            var copiedRounds = new List<WorkspaceRound>(parent.Rounds ?? new List<WorkspaceRound>());

            // 追加一条 Branch 声明轮
            int branchSeq = copiedRounds.Count;
            var branchRound = new WorkspaceRound
            {
                Seq = branchSeq,
                Type = RoundType.Branch,
                Recap = branchRecap ?? $"Forked from '{parent.Label}'",
                Narrative = "",
                CreatedAtTick = tick,
                TriggerEventIds = new List<string>()
            };
            copiedRounds.Add(branchRound);

            // 拷贝父空间的激活技能列表
            var childSkillIds = new List<string>(parent.ActiveSkillIds ?? new List<string>());

            var child = new WorkspaceState
            {
                Id = Guid.NewGuid().ToString("D"),
                Label = newLabel ?? $"{parent.Label} (branch)",
                Status = WorkspaceStatus.Active,
                ParentId = parentId,
                MergedFromIds = new List<string>(),
                ColonistIds = new List<string>(parent.ColonistIds ?? new List<string>()),
                Tags = new List<string>(parent.Tags ?? new List<string>()),
                Rounds = copiedRounds,
                CurrentRecap = branchRecap ?? "",
                CreatedAtTick = tick,
                LastActivityTick = tick,
                ActiveSkillIds = childSkillIds,
                Outcome = null
            };

            _workspaces.Add(child);
            SaveToStore();

            Log.Message($"[RimLife.Workspace] Branched workspace '{child.Label}' (id={child.Id}) from '{parent.Label}'");
            return child;
        }

        // ================================================================
        // 合并
        // ================================================================

        /// <summary>
        /// 将源空间和目标的 Rounds 按 Seq 合并去重，追加一条 Merge 轮记录合并声明，然后废弃源空间。
        /// </summary>
        /// <param name="sourceId">源工作空间 ID。</param>
        /// <param name="targetId">目标工作空间 ID。</param>
        /// <param name="mergeRecap">Agent 写的合并前情提要（作为目标空间新的 CurrentRecap）。</param>
        /// <returns>是否成功。</returns>
        public bool Merge(string sourceId, string targetId, string mergeRecap)
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

            // 合并 Rounds：按 Seq 去重后排序
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

            // 追加一条 Merge 声明轮
            int mergeSeq = mergedRounds.Count;
            int tick = Find.TickManager?.TicksGame ?? 0;
            var mergeRound = new WorkspaceRound
            {
                Seq = mergeSeq,
                Type = RoundType.Merge,
                Recap = mergeRecap ?? $"Merged from '{source.Label}' into '{target.Label}'",
                Narrative = "",
                CreatedAtTick = tick,
                TriggerEventIds = new List<string>()
            };
            mergedRounds.Add(mergeRound);

            // 记录合并来源
            if (target.MergedFromIds == null)
                target.MergedFromIds = new List<string>();
            target.MergedFromIds.Add(sourceId);

            // 合并角色列表（去重）
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

            // 合并语义标签（去重）
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
            target.LastActivityTick = tick;

            // 合并激活技能：将源空间独有的技能并入目标空间
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

            // 废弃源空间
            source.Status = WorkspaceStatus.Abandoned;
            source.LastActivityTick = tick;
            source.Outcome = $"Merged into '{target.Label}' ({target.Id})";

            SaveToStore();
            Log.Message($"[RimLife.Workspace] Merged '{source.Label}' into '{target.Label}'");
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
                    {
                        _workspaces.Add(ws);
                    }
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

            // CurrentRecap
            if (!string.IsNullOrEmpty(ws.CurrentRecap))
                w.Prop("currentRecap", ws.CurrentRecap);

            // ActiveSkillIds
            if (ws.ActiveSkillIds != null && ws.ActiveSkillIds.Count > 0)
                w.Array("activeSkillIds", ws.ActiveSkillIds);

            // Rounds
            if (ws.Rounds != null && ws.Rounds.Count > 0)
            {
                var roundJsons = new List<string>();
                foreach (var r in ws.Rounds)
                    roundJsons.Add(SerializeRound(r));
                w.ArrayRaw("rounds", roundJsons);
            }

            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

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
            w.Prop("createdAtTick", r.CreatedAtTick);

            if (r.TriggerEventIds != null && r.TriggerEventIds.Count > 0)
                w.Array("triggerEventIds", r.TriggerEventIds);

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
                CurrentRecap = data.TryGetValue("currentRecap", out v) ? v : "",
                Rounds = DeserializeRounds(data.TryGetValue("rounds", out v) ? v : null),
                CreatedAtTick = data.TryGetValue("createdAtTick", out v) && int.TryParse(v, out var tick) ? tick : 0,
                LastActivityTick = data.TryGetValue("lastActivityTick", out v) && int.TryParse(v, out var lt) ? lt : 0,
                ActiveSkillIds = DeserializeStringList(data.TryGetValue("activeSkillIds", out v) ? v : null),
                Outcome = data.TryGetValue("outcome", out v) ? (string.IsNullOrEmpty(v) ? null : v) : null
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
                    CreatedAtTick = dict.TryGetValue("createdAtTick", out v) && int.TryParse(v, out var t) ? t : 0,
                    TriggerEventIds = DeserializeStringList(dict.TryGetValue("triggerEventIds", out v) ? v : null)
                };
                result.Add(r);
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

        private static RoundType ParseRoundType(string s)
        {
            if (string.IsNullOrEmpty(s)) return RoundType.Normal;
            if (Enum.TryParse<RoundType>(s, true, out var rt))
                return rt;
            return RoundType.Normal;
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

        // ================================================================
        // 技能管理（WorkspaceState.ActiveSkillIds 是唯一权威源）
        // ================================================================

        /// <summary>
        /// 为指定工作空间激活一个 Skill。直接修改 WorkspaceState 并持久化。
        /// </summary>
        /// <returns>激活结果 JSON（供 LLM 工具调用返回）。</returns>
        public string ActivateSkill(string workspaceId, string skillId)
        {
            var ws = Get(workspaceId);
            if (ws == null)
                return McpSkillRegistry.MakeError($"Workspace '{workspaceId}' not found.");

            if (string.IsNullOrEmpty(skillId))
                return McpSkillRegistry.MakeError("skillId is required");

            if (string.Equals(skillId, McpSkillRegistry.SystemSkillId, StringComparison.OrdinalIgnoreCase))
                return McpSkillRegistry.MakeError($"System skill '{McpSkillRegistry.SystemSkillId}' is always active.");

            if (ws.ActiveSkillIds == null)
                ws.ActiveSkillIds = new List<string>();

            string newToolsJson;
            if (!ws.ActiveSkillIds.Contains(skillId))
            {
                ws.ActiveSkillIds.Add(skillId);
                newToolsJson = McpSkillRegistry.GetSkillToolsJson(skillId);
            }
            else
            {
                newToolsJson = "[]"; // 已激活，无新工具
            }

            ws.LastActivityTick = Find.TickManager?.TicksGame ?? ws.LastActivityTick;
            SaveToStore();
            return McpSkillRegistry.MakeActivateResult(skillId, newToolsJson);
        }

        /// <summary>
        /// 为指定工作空间停用一个 Skill。直接修改 WorkspaceState 并持久化。
        /// </summary>
        /// <returns>反激活结果 JSON（供 LLM 工具调用返回）。</returns>
        public string DeactivateSkill(string workspaceId, string skillId)
        {
            var ws = Get(workspaceId);
            if (ws == null)
                return McpSkillRegistry.MakeError($"Workspace '{workspaceId}' not found.");

            if (string.IsNullOrEmpty(skillId))
                return McpSkillRegistry.MakeError("skillId is required");

            if (string.Equals(skillId, McpSkillRegistry.SystemSkillId, StringComparison.OrdinalIgnoreCase))
                return McpSkillRegistry.MakeError($"Cannot deactivate system skill '{McpSkillRegistry.SystemSkillId}'.");

            if (ws.ActiveSkillIds != null)
                ws.ActiveSkillIds.Remove(skillId);

            ws.LastActivityTick = Find.TickManager?.TicksGame ?? ws.LastActivityTick;
            SaveToStore();
            return McpSkillRegistry.MakeDeactivateResult(skillId);
        }

        /// <summary>
        /// 获取指定工作空间的已激活工具定义 JSON（用于 LLM prompt 注入）。
        /// </summary>
        public string GetActiveToolsJson(string workspaceId)
        {
            var ws = Get(workspaceId);
            var activeIds = ws?.ActiveSkillIds;
            return McpSkillRegistry.GetActiveToolsJson(activeIds);
        }

        /// <summary>
        /// 获取所有 Skill 的轻量列表 JSON（含激活状态），用于 list_skills 工具。
        /// </summary>
        public string GetSkillListJson(string workspaceId)
        {
            var ws = Get(workspaceId);
            var activeIds = ws?.ActiveSkillIds;
            return McpSkillRegistry.GetSkillListJson(activeIds);
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
