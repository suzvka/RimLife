using System;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 将方法或类标记为属于某个 MCP Skill。
    /// 标注在类上时，该类中所有 [McpTool] 方法默认归入该 Skill；
    /// 标注在方法上时，覆盖类级别的设定。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class McpSkillAttribute : Attribute
    {
        /// <summary>所属技能的 ID。</summary>
        public string SkillId { get; }

        public McpSkillAttribute(string skillId)
        {
            SkillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
        }
    }
}
