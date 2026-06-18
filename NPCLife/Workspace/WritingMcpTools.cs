using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 编剧 Agent 的 MCP 工具提供者。通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + ILogger），
    /// 不再直接引用 Infrastructure 或 RimWorld。
    /// </summary>
    public class WritingMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        public WritingMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "workspace_writing";
        public string HookName => "工作空间(编剧)";
        public string HookDescription => "查看工作空间完整内容、推送逐句台词、结束本轮叙事。编剧专用。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(GetWorkspace)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushLine)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(FinishRound)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(RouteEvents)), this),
            };
        }
        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 获取单个工作空间的编剧视图（含完整轮次列表和叙事内容）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的编剧视图：含关联角色、标签、当前前情提要和全部轮次记录（含叙事台词）。")]
        public string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeWriterView(ws);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 逐句台词推送
        // ================================================================

        /// <summary>
        /// 推送单句台词到工作空间。每句立即投递到游戏侧显示。可并行调用多句。
        /// </summary>
        [McpTool(Name = "push_line",
                 Description = "推送单句台词到工作空间，立即在游戏内显示。\n" +
                               "可一次并行调用多个 push_line 来输出多句台词，减少 API 往返。\n" +
                               "type: dialogue(角色对话) / narration(旁白/环境描写) / action(动作描写) / pause(纯停顿)。")]
        public string PushLine(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "说话者的 ThingID。dialogue 类型必填，其他类型省略。")]
            string speakerId,
            [McpParam(Description = "台词/描述文本。pause 类型时可为空。")]
            string text,
            [McpParam(Description = "本行之前的等待秒数，默认 0。")]
            double delay = 0,
            [McpParam(Description = "类型：dialogue/narration/action/pause，默认 dialogue。")]
            string type = "dialogue")
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                bool ok = ws.PushLine(speakerId, text, (float)delay, type,
                                      WorkspaceRole.Screenwriter);
                return ok ? "{\"ok\":true}" : "{}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] push_line failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 结束本轮
        // ================================================================

        /// <summary>
        /// 结束本轮叙事。归档 recap + outcome，给导演留言。编剧所有台词推送完毕后必须调用。
        /// </summary>
        [McpTool(Name = "finish_round",
                 Description = "结束本轮叙事。归档前情提要和剧情发展结果，给导演留言说明后续状态。\n" +
                               "编剧在 push_line 推送完所有台词后必须调用此工具。")]
        public string FinishRound(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "本轮前情提要：编剧对本轮叙事起点的总结。")]
            string recap,
            [McpParam(Description = "本轮剧情发展结果简述。如：角色关系进展、事件解决方式、情绪走向等。")]
            string outcome,
            [McpParam(Description = "给导演的留言：说明剧情线是否可继续(继续推进/需要新事件/可关闭/建议分支等)、期望接收什么类型的事件。")]
            string directorNote,
            [McpParam(Description = "本轮触发的事件 ID 列表，逗号分隔。仅作溯源。",
                      Required = McpRequired.False)] string triggerEventIds = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                var eventIdList = ParseStringList(triggerEventIds);
                bool ok = ws.FinishRound(recap, outcome, directorNote, eventIdList,
                                         WorkspaceRole.Screenwriter);
                if (!ok) return "{}";

                return SerializeWriterView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] finish_round failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 事件路由（通用：源工作空间 → 目标工作空间）
        // ================================================================

        /// <summary>
        /// 将事件从源工作空间推送到目标工作空间。编剧可用此工具将不适合本线的事件推回导演。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "将事件从源工作空间的事件池推送到目标工作空间。可附加留言和知识库查询关键词。编剧可将不适合本剧情线的事件推回导演工作空间。")]
        public string RouteEvents(
            [McpParam(Description = "源工作空间 ID（事件从这里取）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（事件推送到这里）")] string targetWorkspaceId,
            [McpParam(Description = "要路由的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "可选留言：附带给目标工作空间的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "可选知识库查询关键词，逗号分隔。Agent 激活时自动收集所有事件的关键词去重后查询知识库，命中结果注入提示词。",
                      Required = McpRequired.False)] string keywords = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null)
                    return "{\"success\":false,\"error\":\"WorkspaceManager unavailable\"}";

                var ids = ParseStringList(eventIds);
                if (ids.Count == 0)
                    return "{\"success\":false,\"error\":\"no eventIds provided\"}";

                var sourceWs = manager.Get(sourceWorkspaceId);
                if (sourceWs == null)
                    return "{\"success\":false,\"error\":\"source workspace not found\"}";

                var keywordList = ParseStringList(keywords);

                var events = new List<IGameEvent>();
                foreach (var id in ids)
                {
                    var evt = sourceWs.EventPool?.GetById(id);
                    if (evt != null)
                    {
                        if (keywordList.Count > 0)
                        {
                            // 深拷贝事件并附加关键词
                            var copy = EventCardData.From(evt);
                            foreach (var kw in keywordList)
                            {
                                if (!copy.Keywords.Contains(kw))
                                    copy.Keywords.Add(kw);
                            }
                            events.Add(copy);
                        }
                        else
                        {
                            events.Add(evt);
                        }
                    }
                }

                int routed = 0;
                if (events.Count > 0 && manager.RouteEvents(targetWorkspaceId, events))
                    routed = events.Count;

                var w = new JsonWriter(128);
                w.Prop("success", routed > 0);
                w.Prop("routed", routed);
                w.Prop("total", ids.Count);
                if (!string.IsNullOrEmpty(message))
                    w.Prop("message", message);
                if (routed < ids.Count)
                    w.Prop("warning", $"{ids.Count - routed} event(s) not found or target workspace inactive");
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 编剧视图序列化（含完整叙事内容）
        // ================================================================

        /// <summary>
        /// 编剧视图序列化：含完整轮次列表和叙事内容。
        /// </summary>
        private string SerializeWriterView(IWorkspace ws)
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
            w.Prop("createdAt", ws.CreatedAt ?? "");
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            w.Prop("currentRecap", ws.CurrentRecap ?? "");

            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                w.Prop("directorMessage", ws.DirectorMessage);

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
        /// 单个轮次的序列化（含完整叙事内容和作者信息）。
        /// </summary>
        private string SerializeRound(WorkspaceRound r)
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

        // ================================================================
        // 辅助
        // ================================================================

        private List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
