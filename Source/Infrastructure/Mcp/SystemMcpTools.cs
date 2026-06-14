using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 系统的 MCP 元工具集。提供 Skill 列表查询、激活、反激活能力。
    /// 属于 system skill，对所有 workspace 隐式可用。
    /// 每个静态方法对应一个 MCP 工具，通过 [McpTool] / [McpParam] / [McpSkill] 标注。
    /// </summary>
    [McpSkill(McpSkillRegistry.SystemSkillId)]
    public static class SystemMcpTools
    {
        /// <summary>
        /// 列出指定工作空间的所有可用 Skill 及其激活状态。
        /// </summary>
        [McpTool(Name = "list_skills",
                 Description = "列出指定工作空间的所有可用技能分组及激活状态。激活后才能使用对应技能的工具。")]
        public static string ListSkills(
            [McpParam(Description = "工作空间唯一 ID")]
            string workspaceId)
        {
            try
            {
                var wm = RimLifeCore.Workspaces;
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                var activeIds = wm.GetActiveSkillIds(workspaceId);
                return McpSkillRegistry.GetSkillListJson(activeIds);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.SystemMcp] list_skills({workspaceId}) failed: {e.Message}");
                return "{\"skills\":[],\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 为指定工作空间激活一个 Skill，使其工具可用。
        /// </summary>
        [McpTool(Name = "activate_skill",
                 Description = "为指定工作空间激活一个技能分组，使其中的工具在当前对话中可用。可多次调用叠加激活。返回新激活的工具定义。")]
        public static string ActivateSkill(
            [McpParam(Description = "工作空间唯一 ID")]
            string workspaceId,
            [McpParam(Description = "要激活的技能 ID，如 colony_overview / character_query / knowledge_management 等。使用 list_skills 查看全部可用技能。")]
            string skillId)
        {
            try
            {
                var wm = RimLifeCore.Workspaces;
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                return wm.ActivateSkill(workspaceId, skillId);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.SystemMcp] activate_skill({workspaceId}, {skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 为指定工作空间反激活一个 Skill。
        /// </summary>
        [McpTool(Name = "deactivate_skill",
                 Description = "为指定工作空间反激活一个技能分组。system 技能不可反激活。已反激活的技能的工具将不再可用。")]
        public static string DeactivateSkill(
            [McpParam(Description = "工作空间唯一 ID")]
            string workspaceId,
            [McpParam(Description = "要反激活的技能 ID。使用 list_skills 查看当前激活状态。")]
            string skillId)
        {
            try
            {
                var wm = RimLifeCore.Workspaces;
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                return wm.DeactivateSkill(workspaceId, skillId);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.SystemMcp] deactivate_skill({workspaceId}, {skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 获取当前游戏时间字符串。时间信息通常随 Agent 唤醒事件一同注入，
        /// 此工具仅在 Agent 需要主动获取当前时间时使用。
        /// </summary>
        [McpTool(Name = "get_current_time",
                 Description = "获取当前游戏时间的格式化字符串。返回值为游戏侧提供的原样时间文本（如 '第2年·夏季·第5天·14h'）。")]
        public static string GetCurrentTime()
        {
            try
            {
                var provider = RimLifeCore.TimeProvider;
                if (provider == null)
                    return "{\"error\":true,\"message\":\"TimeProvider not set.\"}";
                string time = provider();
                return "{\"time\":" + JsonHelper.Quote(time ?? "") + "}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.SystemMcp] get_current_time failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }
    }
}
