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
    /// 临时任务代理 (Freelancer) 的 MCP 工具提供者。通过 IMcpHookProvider 接口注入依赖。
    ///
    /// 与 WritingMcpProvider 的关键区别：
    /// - 无 get_workspace（含叙事内容）—— Freelancer 不维护剧情上下文
    /// - 无 finish_round 的 outcome/directorNote —— Freelancer 不汇报剧情线推进状态
    /// - push_line / finish_round 使用 WorkspaceRole.Freelancer 身份
    /// - route_events 可将不适合的事件推回导演工作空间
    /// </summary>
    public class FreelancerMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        public FreelancerMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "workspace_freelancer";
        public string HookName => "工作空间(临时任务代理)";
        public string HookDescription => "处理日常对话、突发独立事件的叙事输出。逐句台词推送，与编剧同构但无剧情上下文。Freelancer 专用。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(FreelancerMcpProvider).GetMethod(nameof(GetWorkspace)), this),
                McpTool.FromMethod(typeof(FreelancerMcpProvider).GetMethod(nameof(PushLine)), this),
                McpTool.FromMethod(typeof(FreelancerMcpProvider).GetMethod(nameof(FinishRound)), this),
                McpTool.FromMethod(typeof(FreelancerMcpProvider).GetMethod(nameof(RouteEvents)), this),
            };
        }

        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 获取工作空间轻量信息。Freelancer 不维护剧情上下文，因此只返回元数据和事件摘要，
        /// 不包含叙事轮次历史。
        /// </summary>
        [McpTool(Name = "fre_get_workspace",
                 Description = "获取工作空间轻量信息：元数据、关联角色、标签、待处理事件摘要和轮次统计。不含叙事历史内容。")]
        public string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeResult(ws);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.FreelancerMcp] fre_get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 逐句台词推送
        // ================================================================

        /// <summary>
        /// 推送单句台词到工作空间。Freelancer 身份调用。
        /// </summary>
        [McpTool(Name = "push_line",
                 Description = "推送单句台词到工作空间，立即在游戏内显示。可一次并行调用多个。\n" +
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
                                      WorkspaceRole.Freelancer);
                return ok ? "{\"ok\":true}" : "{}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.FreelancerMcp] push_line failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 结束本轮
        // ================================================================

        /// <summary>
        /// 结束本轮叙事。Freelancer 身份调用，无 outcome/directorNote。
        /// </summary>
        [McpTool(Name = "finish_round",
                 Description = "结束本轮叙事。Freelancer 不维护剧情上下文，recap 为本轮摘要即可。\n" +
                               "在 push_line 推送完所有台词后必须调用。")]
        public string FinishRound(
            [McpParam(Description = "目标工作空间 ID")] string workspaceId,
            [McpParam(Description = "本轮摘要：Freelancer 对当前事件批次的简要总结。")]
            string recap,
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
                bool ok = ws.FinishRound(recap, null, null, eventIdList,
                                         WorkspaceRole.Freelancer);
                if (!ok) return "{}";

                return SerializeResult(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.FreelancerMcp] finish_round failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 事件路由（通用：源工作空间 → 目标工作空间）
        // ================================================================

        /// <summary>
        /// 将事件从当前工作空间推送到目标工作空间。Freelancer 可将不适合的事件推回导演。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "将事件从源工作空间的事件池推送到目标工作空间。可附加留言和知识库查询关键词。Freelancer 可将不适合的事件推回导演工作空间。")]
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
                _logger.Warning($"[NPCLife.FreelancerMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // Freelancer 视图序列化（轻量，不含叙事历史）
        // ================================================================

        /// <summary>
        /// Freelancer 视图：元数据 + 关联角色 + 标签 + 事件池摘要，不含完整轮次列表。
        /// Freelancer 不维护剧情上下文，因此无需返回历史叙事。
        /// </summary>
        private string SerializeResult(IWorkspace ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(512);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            if (ws.ColonistIds != null && ws.ColonistIds.Count > 0)
                w.Array("colonistIds", ws.ColonistIds);
            if (ws.Tags != null && ws.Tags.Count > 0)
                w.Array("tags", ws.Tags);
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            w.Prop("pendingEventCount", ws.EventPool?.PendingCount ?? 0);
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
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
