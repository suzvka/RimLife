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
    /// 工作空间管理的 MCP 工具集。暴露给导演 Agent 使用，
    /// 提供上下文空间的创建、查询、分支、合并和生命周期控制。
    /// 每个静态方法对应一个 MCP 工具，通过 [McpTool] / [McpParam] 标注。
    /// </summary>
    public static class WorkspaceMcpTools
    {
        // ================================================================
        // 创建
        // ================================================================

        /// <summary>
        /// 创建新的上下文空间（剧情线工作空间）。
        /// </summary>
        [McpTool(Name = "create_workspace",
                 Description = "创建新的上下文空间（剧情线工作空间），返回工作空间完整信息。")]
        public static string CreateWorkspace(
            [McpParam(Description = "人类可读标签，如 'RaidAftermath'")] string label,
            [McpParam(Description = "关联殖民者 ThingID，逗号分隔",
                      Required = McpRequired.False)] string colonistIds = null,
            [McpParam(Description = "语义标签，逗号分隔，如 'Combat,Romance'",
                      Required = McpRequired.False)] string tags = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var colonistList = ParseStringList(colonistIds);
                var tagList = ParseStringList(tags);

                var ws = manager.Create(label, colonistList, tagList);
                return SerializeWorkspace(ws);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] create_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 列出工作空间，可按状态过滤。
        /// </summary>
        [McpTool(Name = "list_workspaces",
                 Description = "列出所有工作空间摘要。可按状态过滤（Active/Suspended/Completed/Abandoned）。")]
        public static string ListWorkspaces(
            [McpParam(Description = "过滤状态：Active/Suspended/Completed/Abandoned，留空=全部",
                      Required = McpRequired.False)] string status = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "[]";

                WorkspaceStatus? statusFilter = null;
                if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkspaceStatus>(status, true, out var s))
                    statusFilter = s;

                var workspaces = manager.List(statusFilter);
                return SerializeWorkspaceSummaryList(workspaces);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] list_workspaces failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取单个工作空间的完整信息（含事件列表）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的完整信息，包括关联角色、标签和全部事件列表。")]
        public static string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeWorkspace(ws);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 事件推送
        // ================================================================

        /// <summary>
        /// 将事件推送到指定工作空间。
        /// </summary>
        [McpTool(Name = "push_event_to_workspace",
                 Description = "从 EventLog 中查找事件并复制到指定工作空间（仅 Active 空间可推送）。")]
        public static string PushEventToWorkspace(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "事件 ID（eventId）")] string eventId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                var eventLog = Infrastructure.RimLifeCore.EventLog;
                if (manager == null || eventLog == null) return "{}";

                // 从 EventLog 中查找事件
                var query = new Core.EventQuery { Limit = 1 };
                var allEvents = eventLog.Query(Core.EventQuery.All);
                var evt = allEvents.FirstOrDefault(e => e.EventID == eventId);
                if (evt == null)
                {
                    Log.Warning($"[RimLife.WorkspaceMcp] push_event: event '{eventId}' not found in EventLog.");
                    return "{}";
                }

                bool ok = manager.PushEvent(workspaceId, evt);
                return ok ? SerializeWorkspace(manager.Get(workspaceId)) : "{}";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] push_event_to_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 生命周期
        // ================================================================

        /// <summary>
        /// 挂起工作空间。
        /// </summary>
        [McpTool(Name = "suspend_workspace",
                 Description = "挂起指定工作空间，保留数据但停止事件推送。")]
        public static string SuspendWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                bool ok = manager.UpdateStatus(workspaceId, WorkspaceStatus.Suspended);
                if (!ok) return "{}";
                return SerializeWorkspace(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] suspend_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 恢复已挂起的工作空间。
        /// </summary>
        [McpTool(Name = "resume_workspace",
                 Description = "恢复已挂起的工作空间，重新开始接受事件推送。")]
        public static string ResumeWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                bool ok = manager.UpdateStatus(workspaceId, WorkspaceStatus.Active);
                if (!ok) return "{}";
                return SerializeWorkspace(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] resume_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 关闭工作空间（完成或废弃）。
        /// </summary>
        [McpTool(Name = "close_workspace",
                 Description = "关闭工作空间，标记为 Completed 或 Abandoned。")]
        public static string CloseWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId,
            [McpParam(Description = "结束类型：Completed 或 Abandoned")] string outcomeType,
            [McpParam(Description = "结束原因描述",
                      Required = McpRequired.False)] string reason = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                WorkspaceStatus targetStatus;
                if (string.Equals(outcomeType, "Completed", StringComparison.OrdinalIgnoreCase))
                    targetStatus = WorkspaceStatus.Completed;
                else if (string.Equals(outcomeType, "Abandoned", StringComparison.OrdinalIgnoreCase))
                    targetStatus = WorkspaceStatus.Abandoned;
                else
                {
                    Log.Warning($"[RimLife.WorkspaceMcp] close_workspace: invalid outcomeType '{outcomeType}', must be Completed or Abandoned.");
                    return "{}";
                }

                bool ok = manager.UpdateStatus(workspaceId, targetStatus, reason);
                if (!ok) return "{}";
                return SerializeWorkspace(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] close_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 分支 / 合并
        // ================================================================

        /// <summary>
        /// 从现有工作空间分叉出新空间。
        /// </summary>
        [McpTool(Name = "branch_workspace",
                 Description = "从父工作空间分叉创建新的子工作空间，复制父空间的事件和角色列表。")]
        public static string BranchWorkspace(
            [McpParam(Description = "父工作空间 ID")] string parentWorkspaceId,
            [McpParam(Description = "新工作空间标签")] string label)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var child = manager.Branch(parentWorkspaceId, label);
                return child != null ? SerializeWorkspace(child) : "{}";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] branch_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 合并两个工作空间。
        /// </summary>
        [McpTool(Name = "merge_workspaces",
                 Description = "将源空间的事件和角色合并到目标空间，源空间标记为 Abandoned。")]
        public static string MergeWorkspaces(
            [McpParam(Description = "源工作空间 ID（将被合并并废弃）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（接收数据）")] string targetWorkspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                bool ok = manager.Merge(sourceWorkspaceId, targetWorkspaceId);
                if (!ok) return "{}";
                return SerializeWorkspace(manager.Get(targetWorkspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] merge_workspaces failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        /// <summary>
        /// 完整序列化单个工作空间。
        /// </summary>
        private static string SerializeWorkspace(WorkspaceState ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(2048);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            if (ws.MergedFromIds != null && ws.MergedFromIds.Count > 0)
                w.Array("mergedFromIds", ws.MergedFromIds);
            w.Array("colonistIds", ws.ColonistIds);
            w.Array("tags", ws.Tags);
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            // PinnedEvents
            if (ws.PinnedEvents != null && ws.PinnedEvents.Count > 0)
            {
                var eventJsons = new List<string>();
                foreach (var evt in ws.PinnedEvents)
                    eventJsons.Add(SerializeEventSummary(evt));
                w.ArrayRaw("pinnedEvents", eventJsons);
            }

            return w.Close();
        }

        /// <summary>
        /// 工作空间摘要列表（不含事件详情）。
        /// </summary>
        private static string SerializeWorkspaceSummaryList(IReadOnlyList<WorkspaceState> workspaces)
        {
            if (workspaces == null || workspaces.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < workspaces.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeWorkspaceSummary(workspaces[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 工作空间摘要序列化（轻量，不含事件列表）。
        /// </summary>
        private static string SerializeWorkspaceSummary(WorkspaceState ws)
        {
            var w = new JsonWriter(256);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            w.Prop("colonistCount", ws.ColonistIds?.Count ?? 0);
            w.Prop("eventCount", ws.PinnedEvents?.Count ?? 0);
            w.Array("tags", ws.Tags);
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);
            return w.Close();
        }

        /// <summary>
        /// 工作空间内的事件摘要（LLM 消费用，无需完整 Payload）。
        /// </summary>
        private static string SerializeEventSummary(WorkspaceEvent evt)
        {
            var w = new JsonWriter(256);
            w.Prop("eventId", evt.EventId ?? "");
            w.Prop("defName", evt.DefName ?? "");
            w.Array("tags", evt.Tags);
            w.Prop("tick", evt.Tick);
            w.Prop("severity", evt.Severity ?? "");
            w.Prop("mapHint", evt.MapHint ?? "");

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

            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                var pw = new JsonWriter(256);
                // 只选取关键字段，控制大小
                var keyFields = new HashSet<string> { "letterLabel", "letterText", "reason",
                    "mentalBreakLabel", "damageType", "changeType", "questName", "stateChange",
                    "raidStrategy", "arrivalMode", "threatPoints" };
                foreach (var kv in evt.Payload)
                {
                    if (keyFields.Contains(kv.Key))
                        pw.Prop(kv.Key, Truncate(kv.Value, 200));
                }
                w.PropRaw("payload", pw.Close());
            }

            return w.Close();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
        }
    }
}
