using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 单个参数的 JSON Schema 描述。
    /// </summary>
    public struct McpParamSchema
    {
        /// <summary>JSON Schema 类型字符串：string / integer / number / boolean / array / object。</summary>
        public string Type;

        /// <summary>参数描述。</summary>
        public string Description;

        /// <summary>当 Type 为 array 时，数组元素的类型。</summary>
        public string ItemsType;
    }

    /// <summary>
    /// inputSchema 的完整描述。
    /// </summary>
    public struct McpInputSchema
    {
        /// <summary>固定为 "object"。</summary>
        public string Type;

        /// <summary>参数名 → 参数 schema 映射。</summary>
        public Dictionary<string, McpParamSchema> Properties;

        /// <summary>必填参数名列表。</summary>
        public List<string> Required;
    }

    /// <summary>
    /// 一个完整的 MCP 工具定义，可直接序列化为 MCP 标准 JSON 提示词。
    /// </summary>
    public struct McpToolDefinition
    {
        /// <summary>工具名称。</summary>
        public string Name;

        /// <summary>工具描述。</summary>
        public string Description;

        /// <summary>输入参数 schema。</summary>
        public McpInputSchema InputSchema;
    }
}
