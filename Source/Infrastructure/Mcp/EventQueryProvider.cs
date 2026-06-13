using RimLife.Core;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 事件回溯 Skill 的 Hook Provider。
    /// 提供多维事件历史查询能力。
    /// </summary>
    public class EventQueryProvider : IMcpHookProvider
    {
        public string HookId => "event_query";
        public string HookName => "事件回溯";
        public string HookDescription => "多维事件历史查询（标签、时间、Actor、严重度）";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(EventQueryProvider).GetMethod(nameof(QueryEvents))),
            };
        }

        /// <summary>
        /// 多维事件历史查询。
        /// </summary>
        [McpTool(Name = "query_events",
                 Description = "多维事件历史查询。支持按标签(OR/AND)、时间范围、Actor、严重度筛选。")]
        public static string QueryEvents(
            [McpParam(Description = "OR标签：命中任一即匹配，逗号分隔。如 Combat,Raid",
                      Required = McpRequired.False)] string tagsAny = null,
            [McpParam(Description = "AND标签：必须全部命中，逗号分隔。如 Combat,Death",
                      Required = McpRequired.False)] string tagsAll = null,
            [McpParam(Description = "起始 tick（含）",
                      Required = McpRequired.False)] int? sinceTick = null,
            [McpParam(Description = "参与角色 ID",
                      Required = McpRequired.False)] string actorId = null,
            [McpParam(Description = "严重度：Minor/Major/Extreme",
                      Required = McpRequired.False)] string severity = null,
            [McpParam(Description = "最大返回数，默认 20")] int limit = 20)
        {
            try
            {
                var eventLog = RimLifeCore.EventLog;
                if (eventLog == null) return "[]";

                var query = new EventQuery
                {
                    TagsAny = PawnQueryHelper.ParseTagList(tagsAny),
                    TagsAll = PawnQueryHelper.ParseTagList(tagsAll),
                    SinceTick = sinceTick,
                    ActorId = !string.IsNullOrEmpty(actorId) ? actorId : null,
                    Severity = !string.IsNullOrEmpty(severity) ? severity : null,
                    Limit = limit
                };

                var results = eventLog.Query(query);
                return CardSerializer.SerializeEventList(results);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.EventQueryProvider] query_events failed: {e.Message}");
                return "[]";
            }
        }
    }
}
