using System;

namespace RimLife.Skills
{
    /// <summary>
    /// 标记一个方法为 Skill 工具。RimLife 自动扫描并注册为 MCP 工具。
    /// 第三方 Skill 只需添加此特性，无需了解 NPCLife 的 McpTool 机制。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class SkillToolAttribute : Attribute
    {
        /// <summary>工具名称（LLM 可见）。若未设置则使用方法名。</summary>
        public string Name { get; set; }

        /// <summary>工具描述（LLM 可见）。</summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// 标记工具方法的参数元数据。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class SkillParamAttribute : Attribute
    {
        /// <summary>参数描述（LLM 可见）。</summary>
        public string Description { get; set; }

        /// <summary>是否必填。默认 true。设为 false 表示可选参数。</summary>
        public bool Required { get; set; } = true;
    }
}
