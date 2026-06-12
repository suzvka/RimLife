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
    [McpSkill("workspace_management")]
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
        /// 获取单个工作空间的完整信息（含轮次列表）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的完整信息，包括关联角色、标签、当前前情提要和全部轮次记录。")]
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
        // 回合推送
        // ================================================================

        /// <summary>
        /// 推送一个新轮次到工作空间。Agent 每轮生成结束后调用。
        /// </summary>
        [McpTool(Name = "push_round",
                 Description = "向工作空间推送一个新轮次：前情提要 + 正式台词。只有 Active 空间可推送。推送后 CurrentRecap 自动更新。")]
        public static string PushRound(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "本轮前情提要：Agent 对本轮叙事起点的总结。建议包含主要角色状态和当前剧情线。")]
            string recap,
            [McpParam(Description = "正式台词：Agent 的叙事输出。")]
            string narrative,
            [McpParam(Description = "本轮触发的事件 ID 列表，逗号分隔。仅作溯源，不注入 prompt。",
                      Required = McpRequired.False)] string triggerEventIds = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var eventIdList = ParseStringList(triggerEventIds);
                bool ok = manager.PushRound(workspaceId, recap, narrative, eventIdList);
                if (!ok) return "{}";

                return SerializeWorkspace(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WorkspaceMcp] push_round failed: {e.Message}");
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
                 Description = "挂起指定工作空间，保留数据但停止回合推送。")]
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
                 Description = "恢复已挂起的工作空间，重新开始接受回合推送。")]
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
                 Description = "从父工作空间分叉创建新的子工作空间。需要 Agent 提供分支前情提要作为新空间的初始上下文。拷贝父空间的轮次历史，追加一条 Branch 轮。")]
        public static string BranchWorkspace(
            [McpParam(Description = "父工作空间 ID")] string parentWorkspaceId,
            [McpParam(Description = "新工作空间标签")] string label,
            [McpParam(Description = "分支前情提要：Agent 对为什么要开分支以及新线当前状态的总结。将作为子空间的初始 CurrentRecap。")]
            string branchRecap)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var child = manager.Branch(parentWorkspaceId, label, branchRecap);
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
                 Description = "将源空间的轮次合并到目标空间，然后废弃源空间。需要 Agent 提供合并前情提要作为合并后的起点。按 Seq 去重，追加一条 Merge 轮。")]
        public static string MergeWorkspaces(
            [McpParam(Description = "源工作空间 ID（将被合并并废弃）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（接收数据）")] string targetWorkspaceId,
            [McpParam(Description = "合并前情提要：Agent 对两条线合并后的叙事状态总结。将作为目标空间的新 CurrentRecap。")]
            string mergeRecap)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                bool ok = manager.Merge(sourceWorkspaceId, targetWorkspaceId, mergeRecap);
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
        /// 完整序列化单个工作空间（含轮次列表）。
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

            // CurrentRecap — 核心上下文窗口
            w.Prop("currentRecap", ws.CurrentRecap ?? "");

            // Rounds — Agent 写作日志
            if (ws.Rounds != null && ws.Rounds.Count > 0)
            {
                var roundJsons = new List<string>();
                foreach (var r in ws.Rounds)
                    roundJsons.Add(SerializeRound(r));
                w.ArrayRaw("rounds", roundJsons);
            }

            return w.Close();
        }

        /// <summary>
        /// 工作空间摘要列表（不含轮次详情，仅统计）。
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
        /// 工作空间摘要序列化（轻量，不含轮次内容，仅含统计和当前 recap 摘要）。
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
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            w.Array("tags", ws.Tags);
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);
            // 当前 recap 的首行摘要（截取前 80 字符）
            if (!string.IsNullOrEmpty(ws.CurrentRecap))
                w.Prop("recapPreview", Truncate(ws.CurrentRecap, 80));
            return w.Close();
        }

        /// <summary>
        /// 单个轮次的序列化。
        /// </summary>
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
