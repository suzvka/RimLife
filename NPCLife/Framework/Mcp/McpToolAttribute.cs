using System;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 将方法标记为 MCP 工具。可选覆盖名称与描述，未设置时从方法签名自动推导。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class McpToolAttribute : Attribute
    {
        /// <summary>覆盖工具名称。未设置时使用方法名。</summary>
        public string Name { get; set; }

        /// <summary>工具描述。</summary>
        public string Description { get; set; }
    }
}
