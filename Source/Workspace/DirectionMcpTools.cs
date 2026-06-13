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
    /// 导演 Agent 的 MCP 工具集。提供工作空间的结构管理能力：
    /// 创建、查询（仅看信号/统计，不看叙事内容）、分支、合并、生命周期控制。
    /// 导演的职责是管理分支结构，不接触具体叙事内容。
    /// </summary>
    [McpSkill("workspace_direction")]
    public static class DirectionMcpTools
    {
        // ================================================================
        // 创建
        // ================================================================

        /// <summary>
        /// 创建新的剧情线工作空间。创建者角色固定为 Director。
        /// </summary>
        [McpTool(Name = "create_workspace",
                 Description = "创建新的上下文空间（剧情线工作空间），返回工作空间完整信息。创建者角色为 Director。")]
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

                var ws = manager.Create(label, colonistList, tagList, WorkspaceRole.Director);
                return SerializeDirectorView(ws);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] create_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 列出工作空间摘要（导演视图：含信号、统计，不含叙事内容）。
        /// </summary>
        [McpTool(Name = "list_workspaces",
                 Description = "列出所有工作空间摘要（导演视图）。含信号、轮次数、角色数，不含叙事内容。可按状态过滤。")]
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
                return SerializeDirectorSummaryList(workspaces);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] list_workspaces failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取单个工作空间的导演视图（含信号、统计，不含叙事内容）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的导演视图：含角色、标签、信号、轮次统计，不含叙事台词内容。")]
        public static string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeDirectorView(ws);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 生命周期
        // ================================================================

        /// <summary>
        /// 挂起工作空间。仅 Director 可调用（入口层面由 Skill 归属保证）。
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
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] suspend_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 恢复已挂起的工作空间。仅 Director 可调用。
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
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] resume_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 关闭工作空间（完成或废弃）。仅 Director 可调用。
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
                    Log.Warning($"[RimLife.DirectionMcp] close_workspace: invalid outcomeType '{outcomeType}', must be Completed or Abandoned.");
                    return "{}";
                }

                bool ok = manager.UpdateStatus(workspaceId, targetStatus, reason);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] close_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 分支 / 合并
        // ================================================================

        /// <summary>
        /// 从现有工作空间分叉出新空间。仅 Director 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "branch_workspace",
                 Description = "从父工作空间分叉创建新的子工作空间。拷贝父空间的轮次历史，追加一条 Branch 轮。")]
        public static string BranchWorkspace(
            [McpParam(Description = "父工作空间 ID")] string parentWorkspaceId,
            [McpParam(Description = "新工作空间标签")] string label,
            [McpParam(Description = "分支前情提要：编剧对为什么要开分支以及新线当前状态的总结。")]
            string branchRecap)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var child = manager.Branch(parentWorkspaceId, label, branchRecap, WorkspaceRole.Director);
                return child != null ? SerializeDirectorView(child) : "{}";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] branch_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 合并两个工作空间。仅 Director 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "merge_workspaces",
                 Description = "将源空间的轮次合并到目标空间，然后废弃源空间。按 Seq 去重，追加一条 Merge 轮。")]
        public static string MergeWorkspaces(
            [McpParam(Description = "源工作空间 ID（将被合并并废弃）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（接收数据）")] string targetWorkspaceId,
            [McpParam(Description = "合并前情提要：编剧对两条线合并后的叙事状态总结。")]
            string mergeRecap)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                bool ok = manager.Merge(sourceWorkspaceId, targetWorkspaceId, mergeRecap, WorkspaceRole.Director);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(targetWorkspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.DirectionMcp] merge_workspaces failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 导演视图序列化（不含叙事内容，含信号和统计）
        // ================================================================

        /// <summary>
        /// 导演视图序列化：含元数据、信号、轮次统计，不含具体叙事内容。
        /// </summary>
        private static string SerializeDirectorView(WorkspaceState ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(1024);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            w.Prop("createdByRole", ws.CreatedByRole.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            if (ws.MergedFromIds != null && ws.MergedFromIds.Count > 0)
                w.Array("mergedFromIds", ws.MergedFromIds);
            w.Array("colonistIds", ws.ColonistIds);
            w.Array("tags", ws.Tags);
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            // LastSignal — 导演决策的关键依据
            if (ws.LastSignal.HasValue)
            {
                var sig = ws.LastSignal.Value;
                var sigW = new JsonWriter(256);
                sigW.Prop("type", sig.Type.ToString());
                sigW.Prop("reportedAtTick", sig.ReportedAtTick);
                if (!string.IsNullOrEmpty(sig.Note))
                    sigW.Prop("note", sig.Note);
                if (!string.IsNullOrEmpty(sig.SuggestedTargetId))
                    sigW.Prop("suggestedTargetId", sig.SuggestedTargetId);
                w.PropRaw("lastSignal", sigW.Close());
            }

            return w.Close();
        }

        /// <summary>
        /// 导演视图摘要列表（轻量，不含轮次详情，含信号）。
        /// </summary>
        private static string SerializeDirectorSummaryList(IReadOnlyList<WorkspaceState> workspaces)
        {
            if (workspaces == null || workspaces.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < workspaces.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeDirectorSummary(workspaces[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 单个工作空间的导演摘要（轻量）。
        /// </summary>
        private static string SerializeDirectorSummary(WorkspaceState ws)
        {
            var w = new JsonWriter(256);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            w.Prop("createdByRole", ws.CreatedByRole.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            w.Prop("colonistCount", ws.ColonistIds?.Count ?? 0);
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            w.Array("tags", ws.Tags);
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);
            // 信号摘要
            if (ws.LastSignal.HasValue)
            {
                var sig = ws.LastSignal.Value;
                w.Prop("signalType", sig.Type.ToString());
                if (!string.IsNullOrEmpty(sig.Note))
                    w.Prop("signalNote", Truncate(sig.Note, 80));
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
