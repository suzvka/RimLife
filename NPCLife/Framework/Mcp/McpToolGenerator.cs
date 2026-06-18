using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// MCP 工具定义生成器。通过反射将方法签名转换为标准 MCP 工具 JSON 提示词。
    /// 纯静态，零外部依赖，直接复用 JsonWriter / JsonHelper。
    /// </summary>
    public static class McpToolGenerator
    {
        /// <summary>
        /// 从 MethodInfo 生成 McpToolDefinition。
        /// 自动读取 [McpTool] / [McpParam] 特性，缺失时从方法签名推导。
        /// required 逻辑：显式设 [McpParam(Required=...)] 优先，否则从 C# 默认值自动推断。
        /// </summary>
        public static McpToolDefinition GenerateDefinition(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            // 读取 [McpTool]
            var toolAttr = method.GetCustomAttribute<McpToolAttribute>();
            string name = toolAttr?.Name;
            if (string.IsNullOrEmpty(name)) name = method.Name;
            string description = toolAttr?.Description ?? string.Empty;

            // 读取参数
            var parameters = method.GetParameters();
            var properties = new Dictionary<string, McpParamSchema>();
            var required = new List<string>();

            foreach (var p in parameters)
            {
                var paramAttr = p.GetCustomAttribute<McpParamAttribute>();
                string paramName = paramAttr?.Name;
                if (string.IsNullOrEmpty(paramName)) paramName = p.Name;

                string paramDesc = paramAttr?.Description ?? string.Empty;

                bool isRequired;
                if (paramAttr != null && paramAttr.Required != McpRequired.Auto)
                    isRequired = paramAttr.Required == McpRequired.True;
                else
                    isRequired = !p.IsOptional;

                var schemaType = McpTypeMapper.GetSchemaType(p.ParameterType);
                var schema = new McpParamSchema
                {
                    Type = schemaType,
                    Description = paramDesc
                };

                // 数组元素类型
                if (schemaType == "array")
                {
                    var elemType = McpTypeMapper.GetElementType(p.ParameterType);
                    if (elemType != null)
                        schema.ItemsType = McpTypeMapper.GetSchemaType(elemType);
                }

                properties[paramName] = schema;
                if (isRequired) required.Add(paramName);
            }

            return new McpToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = new McpInputSchema
                {
                    Type = "object",
                    Properties = properties,
                    Required = required.Count > 0 ? required : null
                }
            };
        }

        /// <summary>
        /// 将 McpToolDefinition 序列化为标准 MCP 工具提示词 JSON 字符串。
        /// OpenAI/DeepSeek 兼容格式：包含 type="function" 字段。
        /// </summary>
        public static string Serialize(McpToolDefinition def)
        {
            var w = new JsonWriter(512);
            
            // DeepSeek/OpenAI 要求必须有 type 字段
            w.Prop("type", "function");
            
            // function 对象：name, description, parameters
            var funcW = new JsonWriter(256);
            funcW.Prop("name", def.Name ?? string.Empty);
            funcW.Prop("description", def.Description ?? string.Empty);
            
            // inputSchema → parameters (OpenAI 标准命名)
            funcW.PropRaw("parameters", SerializeInputSchema(def.InputSchema));
            
            w.PropRaw("function", funcW.Close());
            
            return w.Close();
        }

        /// <summary>
        /// 便捷方法：MethodInfo → JSON 字符串。
        /// </summary>
        public static string SerializeMethod(MethodInfo method)
        {
            var def = GenerateDefinition(method);
            return Serialize(def);
        }

        /// <summary>
        /// 便捷方法：McpTool → JSON 字符串。
        /// 直接序列化其 Definition，MethodInfo 工具与 Hook 工具通用。
        /// </summary>
        public static string Serialize(McpTool tool)
        {
            if (tool == null) return "{}";
            return Serialize(tool.Definition);
        }

        /// <summary>
        /// 从类型中扫描所有标记 [McpTool] 的静态方法，返回 JSON 数组。
        /// </summary>
        public static string SerializeAllFrom(Type type)
        {
            if (type == null) return "[]";
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var jsonList = new List<string>();
            foreach (var m in methods)
            {
                if (m.GetCustomAttribute<McpToolAttribute>() != null)
                    jsonList.Add(SerializeMethod(m));
            }

            if (jsonList.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < jsonList.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(jsonList[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 获取已激活全部 Skill 的工具定义 JSON 数组。
        /// 用于构造发送给 LLM 的 prompt 中的 tools 字段。
        /// </summary>
        /// <param name="activeSkillIds">当前激活的业务 skill ID 集合。</param>
        public static string SerializeAllActiveTools(IEnumerable<string> activeSkillIds)
        {
            return McpSkillRegistry.GetActiveToolsJson(activeSkillIds);
        }

        /// <summary>
        /// 获取所有 Skill 的轻量列表 JSON（含激活状态）。
        /// 供 Agent 调用 list_skills 时返回。
        /// </summary>
        /// <param name="activeSkillIds">当前激活的业务 skill ID 集合。</param>
        public static string SerializeSkillList(IEnumerable<string> activeSkillIds)
        {
            return McpSkillRegistry.GetSkillListJson(activeSkillIds);
        }

        // ================================================================
        // 内部序列化
        // ================================================================

        private static string SerializeInputSchema(McpInputSchema schema)
        {
            var w = new JsonWriter(256);
            w.Prop("type", schema.Type ?? "object");

            // properties
            if (schema.Properties != null && schema.Properties.Count > 0)
            {
                var pw = new JsonWriter(256);
                foreach (var kv in schema.Properties)
                {
                    pw.PropRaw(kv.Key, SerializeParamSchema(kv.Value));
                }
                w.PropRaw("properties", pw.Close());
            }

            // required
            if (schema.Required != null && schema.Required.Count > 0)
            {
                w.Array("required", schema.Required);
            }

            return w.Close();
        }

        private static string SerializeParamSchema(McpParamSchema param)
        {
            var w = new JsonWriter(64);
            w.Prop("type", param.Type ?? "string");
            if (!string.IsNullOrEmpty(param.Description))
                w.Prop("description", param.Description);
            if (!string.IsNullOrEmpty(param.ItemsType))
            {
                // items: { "type": "string" }
                var itemsWriter = new JsonWriter(32);
                itemsWriter.Prop("type", param.ItemsType);
                w.PropRaw("items", itemsWriter.Close());
            }
            return w.Close();
        }
    }
}
