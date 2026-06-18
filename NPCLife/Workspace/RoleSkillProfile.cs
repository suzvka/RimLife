using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 角色 Skill Profile 预设。定义每种 Agent 角色创建工作空间时默认激活的技能集。
    /// 
    /// 设计原则：
    /// - Director（导演）：需要全局视图和结构管理能力，不需要叙事细节
    /// - Screenwriter（编剧）：需要完整的角色/环境/关系/事件查询能力来创作叙事
    /// - Freelancer（临时工）：只需角色和事件的基本查询能力，无剧情上下文
    /// </summary>
    public static class RoleSkillProfile
    {
        /// <summary>
        /// Director 预设：全局感知 + 结构管理。不含关系查询（导演不关心 NPC 间互动细节），
        /// 不含环境查询（导演做结构决策不需要逐格环境数据）。
        /// </summary>
        private static readonly string[] DirectorProfile =
        {
            "workspace_direction",
            "colony_overview",
            "character_query",
            "event_query",
            "knowledge_management"
        };

        /// <summary>
        /// Screenwriter 预设：完整的叙事创作工具集。含关系网络、环境感知等编剧所需的全部上下文。
        /// </summary>
        private static readonly string[] ScreenwriterProfile =
        {
            "workspace_writing",
            "colony_overview",
            "character_query",
            "relationship_query",
            "event_query",
            "environment_query",
            "knowledge_management"
        };

        /// <summary>
        /// Freelancer 预设：轻量查询 + 快速叙事输出。不含关系查询（独立事件不涉及深度社交），
        /// 不含环境查询（日常对话不需要精确环境数据），不含知识管理（临时工不积累知识）。
        /// </summary>
        private static readonly string[] FreelancerProfile =
        {
            "workspace_freelancer",
            "character_query",
            "event_query"
        };

        /// <summary>
        /// 获取指定角色的默认技能 ID 列表。
        /// </summary>
        /// <param name="role">Agent 角色身份。</param>
        /// <returns>该角色默认应激活的技能 ID 列表。</returns>
        public static IReadOnlyList<string> GetDefaultSkillIds(WorkspaceRole role)
        {
            switch (role)
            {
                case WorkspaceRole.Director:
                    return DirectorProfile;
                case WorkspaceRole.Screenwriter:
                    return ScreenwriterProfile;
                case WorkspaceRole.Freelancer:
                    return FreelancerProfile;
                default:
                    return new string[0];
            }
        }
    }
}
