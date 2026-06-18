using System;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 必填状态：Auto = 从 C# 默认值自动推断；True/False = 显式覆盖。
    /// </summary>
    public enum McpRequired
    {
        /// <summary>自动推断：参数无默认值 → 必填，有默认值 → 可选。</summary>
        Auto = 0,
        /// <summary>强制必填。</summary>
        True = 1,
        /// <summary>强制可选。</summary>
        False = 2
    }

    /// <summary>
    /// 覆盖 MCP 工具参数的名称与描述。未设置时从参数名自动推导。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public class McpParamAttribute : Attribute
    {
        /// <summary>覆盖参数名。未设置时使用 C# 参数名。</summary>
        public string Name { get; set; }

        /// <summary>参数描述。</summary>
        public string Description { get; set; }

        /// <summary>是否必填。默认 Auto（自动推断）。</summary>
        public McpRequired Required { get; set; } = McpRequired.Auto;
    }
}
