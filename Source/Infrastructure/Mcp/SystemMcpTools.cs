using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 系统的 MCP 元工具集。提供 Skill 列表查询、激活、反激活能力。
    /// 始终激活，不归属任何业务 Skill。
    /// 每个静态方法对应一个 MCP 工具，通过 [McpTool] / [McpParam] / [McpSkill] 标注。
    /// </summary>
    [McpSkill(McpSkillRegistry.SystemSkillId)]
    public static class SystemMcpTools
    {
        /// <summary>
        /// 列出所有可用的 Skill 及其激活状态。
        /// </summary>
        [McpTool(Name = "list_skills",
                 Description = "列出所有可用的技能分组及激活状态。激活后才能使用对应技能的工具。")]
        public static string ListSkills()
        {
            try
            {
                return McpSkillRegistry.GetSkillListJson();
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.SystemMcp] list_skills failed: {e.Message}");
                return "{\"skills\":[],\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 激活一个 Skill，使其工具可用。累积叠加。
        /// </summary>
        [McpTool(Name = "activate_skill",
                 Description = "激活一个技能分组，使其中的工具在当前对话中可用。可多次调用叠加激活。返回新激活的工具定义。")]
        public static string ActivateSkill(
            [McpParam(Description = "要激活的技能 ID，如 colony_overview / character_query / knowledge_management 等。使用 list_skills 查看全部可用技能。")]
            string skillId)
        {
            try
            {
                return McpSkillRegistry.ActivateSkill(skillId);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.SystemMcp] activate_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 反激活一个 Skill，释放上下文。
        /// </summary>
        [McpTool(Name = "deactivate_skill",
                 Description = "反激活一个技能分组，释放上下文。system 技能不可反激活。已反激活的技能的工具将不再可用。")]
        public static string DeactivateSkill(
            [McpParam(Description = "要反激活的技能 ID。使用 list_skills 查看当前激活状态。")]
            string skillId)
        {
            try
            {
                return McpSkillRegistry.DeactivateSkill(skillId);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.SystemMcp] deactivate_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 预先注入一个 Skill：立即激活，并登记到工作空间缓存中。
        /// 下次冷启动时自动激活，无需 agent 再次调用 activate_skill。
        /// 适用于高频技能，可节省一轮 list_skills → activate_skill 的往返。
        /// </summary>
        [McpTool(Name = "preload_skill",
                 Description = "预先注入一个技能分组。立即激活该技能，并将其登记到工作空间缓存中，下次冷启动时自动激活。适用于 agent 确认某个高频技能在后续会话中也经常使用的情况。")]
        public static string PreloadSkill(
            [McpParam(Description = "要预载的技能 ID。使用 list_skills 查看所有可用技能。")]
            string skillId)
        {
            try
            {
                string result = McpSkillRegistry.AddPreload(skillId);
                RimLifeCore.SaveSkillPreloads();
                return result;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.SystemMcp] preload_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 解除一个 Skill 的预先注入。当前会话不受影响，但下次冷启动不再自动激活。
        /// </summary>
        [McpTool(Name = "unpreload_skill",
                 Description = "解除一个技能分组的预先注入。当前会话中该技能保持激活（需手动 deactivate），但下次冷启动不再自动激活。system 技能不可解除预载。")]
        public static string UnpreloadSkill(
            [McpParam(Description = "要解除预载的技能 ID。使用 list_skills 查看当前预载状态。")]
            string skillId)
        {
            try
            {
                string result = McpSkillRegistry.RemovePreload(skillId);
                RimLifeCore.SaveSkillPreloads();
                return result;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.SystemMcp] unpreload_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }
    }
}
