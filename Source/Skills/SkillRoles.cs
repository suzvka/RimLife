using System;
using NPCLife.Workspace;

namespace RimLife.Skills
{
    /// <summary>
    /// Skill 角色常量。第三方 Skill 通过字符串声明目标角色，
    /// 无需引用 NPCLife 的 WorkspaceRole 枚举。
    /// 此处是唯一的角色映射点。
    /// </summary>
    public static class SkillRoles
    {
        public const string Director = "director";
        public const string Screenwriter = "screenwriter";
        public const string Improviser = "improviser";

        /// <summary>将 RimLife 角色字符串映射到 NPCLife WorkspaceRole。内部使用。</summary>
        internal static WorkspaceRole ToNpcRole(string role)
        {
            if (string.IsNullOrEmpty(role))
                throw new ArgumentNullException(nameof(role));

            switch (role.ToLowerInvariant())
            {
                case Director:     return WorkspaceRole.Director;
                case Screenwriter: return WorkspaceRole.Screenwriter;
                case Improviser:   return WorkspaceRole.Improviser;
                default:
                    throw new ArgumentException($"Unknown role: '{role}'. Valid roles: {Director}, {Screenwriter}, {Improviser}");
            }
        }
    }
}
