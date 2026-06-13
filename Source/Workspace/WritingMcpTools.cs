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
    /// 编剧 Agent 的 MCP 工具集。提供叙事创作能力：
    /// 查看完整工作空间（含轮次/前情提要）、推送回合、上报推进信号。
    /// 编剧没有分支/合并/关闭权限，只能通过信号建议导演操作。
    /// </summary>
    [McpSkill("workspace_writing")]
    public static class WritingMcpTools
    {
        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 获取单个工作空间的编剧视图（含完整轮次列表和叙事内容）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的编剧视图：含关联角色、标签、当前前情提要和全部轮次记录（含叙事台词）。")]
        public static string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeWriterView(ws);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WritingMcp] get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 回合推送
        // ================================================================

        /// <summary>
        /// 推送一个新轮次到工作空间。仅 Screenwriter 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "push_round",
                 Description = "向工作空间推送一个新轮次：前情提要 + 正式台词。只有 Active 空间可推送。推送后 CurrentRecap 自动更新。")]
        public static string PushRound(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "本轮前情提要：编剧对本轮叙事起点的总结。")]
            string recap,
            [McpParam(Description = "正式台词：编剧的叙事输出。")]
            string narrative,
            [McpParam(Description = "本轮触发的事件 ID 列表，逗号分隔。仅作溯源，不注入 prompt。",
                      Required = McpRequired.False)] string triggerEventIds = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                var eventIdList = ParseStringList(triggerEventIds);
                bool ok = manager.PushRound(workspaceId, recap, narrative, eventIdList,
                                            WorkspaceRole.Screenwriter);
                if (!ok) return "{}";

                return SerializeWriterView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WritingMcp] push_round failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 推进信号
        // ================================================================

        /// <summary>
        /// 编剧上报剧情线推进状态信号。仅 Screenwriter 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "signal_workspace_status",
                 Description = "上报剧情线推进状态信号。导演据此信号做分支/合并/关闭决策。\n" +
                               "signalType: Progressing(正常推进) / StorylineComplete(走到终点) / " +
                               "NeedsBranch(需要分叉) / Stuck(僵局) / ReadyForMerge(建议合并)。\n" +
                               "编剧只上报状态，不透露具体叙事内容。")]
        public static string SignalWorkspaceStatus(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "信号类型：Progressing/StorylineComplete/NeedsBranch/Stuck/ReadyForMerge")]
            string signalType,
            [McpParam(Description = "编剧给导演的简短说明（≤200字）。结构化摘要，不透露剧情细节。",
                      Required = McpRequired.False)] string note = null,
            [McpParam(Description = "ReadyForMerge 时：建议合并到的目标工作空间 ID。其他类型留空。",
                      Required = McpRequired.False)] string suggestedTargetId = null)
        {
            try
            {
                var manager = Infrastructure.RimLifeCore.Workspaces;
                if (manager == null) return "{}";

                if (!Enum.TryParse<SignalType>(signalType, true, out var parsedType))
                {
                    Log.Warning($"[RimLife.WritingMcp] signal_workspace_status: invalid signalType '{signalType}'.");
                    return "{}";
                }

                bool ok = manager.ReportSignal(workspaceId, parsedType, note, suggestedTargetId,
                                               WorkspaceRole.Screenwriter);
                if (!ok) return "{}";

                return SerializeWriterView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.WritingMcp] signal_workspace_status failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 编剧视图序列化（含完整叙事内容）
        // ================================================================

        /// <summary>
        /// 编剧视图序列化：含完整轮次列表和叙事内容。
        /// </summary>
        private static string SerializeWriterView(WorkspaceState ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(2048);
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
            w.Prop("createdAtTick", ws.CreatedAtTick);
            w.Prop("lastActivityTick", ws.LastActivityTick);
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            // CurrentRecap — 编剧的上下文窗口
            w.Prop("currentRecap", ws.CurrentRecap ?? "");

            // Rounds — 编剧的写作日志（含叙事内容）
            if (ws.Rounds != null && ws.Rounds.Count > 0)
            {
                var roundJsons = new List<string>();
                foreach (var r in ws.Rounds)
                    roundJsons.Add(SerializeRound(r));
                w.ArrayRaw("rounds", roundJsons);
            }

            // LastSignal — 编剧也可看到自己上报的信号
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
        /// 单个轮次的序列化（含完整叙事内容和作者信息）。
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

            w.Prop("authorRole", r.AuthorRole.ToString());
            if (!string.IsNullOrEmpty(r.AuthorId))
                w.Prop("authorId", r.AuthorId);

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
    }
}
