using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;

namespace RimLife.Skills
{
    /// <summary>
    /// 将 RimLife ISkillProvider 适配为 NPCLife IMcpHookProvider。
    /// 内部实现，第三方无需了解。
    /// 
    /// 核心工作：
    /// 1. 将 ISkillProvider 的元数据（ModuleId/Name/Description/Roles）映射到 Hook 接口
    /// 2. 扫描 [SkillTool] 方法，构造 McpTool 数组（Definition + Invoker）
    /// 3. Invoker 委托到 McpToolInvoker.Invoke()，参数名解析兼容 [SkillParam]
    /// </summary>
    internal class McpSkillAdapter : IMcpHookProvider
    {
        private readonly ISkillProvider _skill;
        private readonly ILogger _logger;

        public McpSkillAdapter(ISkillProvider skill, ILogger logger)
        {
            _skill = skill ?? throw new ArgumentNullException(nameof(skill));
            _logger = logger;
        }

        public string HookId => _skill.ModuleId;
        public string HookName => _skill.ModuleName;
        public string HookDescription => _skill.ModuleDescription;
        public string PromptInstruction => _skill.PromptInstruction;

        public IReadOnlyList<McpTool> GetTools()
        {
            var tools = new List<McpTool>();
            var type = _skill.GetType();

            // 扫描声明类型上的所有公开实例方法（含继承），只取带 [SkillTool] 的
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var toolAttr = method.GetCustomAttribute<SkillToolAttribute>();
                if (toolAttr == null) continue;

                try
                {
                    var tool = CreateMcpTool(method, toolAttr);
                    tools.Add(tool);
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"[RimLife.Skills] Failed to create McpTool for {type.Name}.{method.Name}: {ex.Message}");
                }
            }

            return tools;
        }

        private McpTool CreateMcpTool(MethodInfo method, SkillToolAttribute toolAttr)
        {
            // 1. 构造 Definition
            var definition = BuildDefinition(method, toolAttr);

            // 2. 构造 Invoker：委托到 McpToolInvoker.Invoke()
            // McpToolInvoker 通过反射调用方法，参数名从 [McpParam].Name 或 p.Name 获取
            // [SkillParam] 不影响参数名解析（McpToolInvoker 不读 [SkillParam]），
            // 所以参数名仍是 C# 方法参数名，这对 JSON args 解析来说是正常的
            Func<string, string> invoker = jsonArgs =>
            {
                try
                {
                    return McpToolInvoker.Invoke(method, _skill, jsonArgs);
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"[RimLife.Skills] Tool '{definition.Name}' invocation failed: {ex.Message}");
                    return "{\"error\":true,\"message\":" + JsonHelper.Quote(ex.Message) + "}";
                }
            };

            return new McpTool { Definition = definition, Invoker = invoker };
        }

        private static McpToolDefinition BuildDefinition(MethodInfo method, SkillToolAttribute toolAttr)
        {
            string name = !string.IsNullOrEmpty(toolAttr.Name) ? toolAttr.Name : method.Name;
            string description = toolAttr.Description ?? string.Empty;

            var parameters = method.GetParameters();
            var properties = new Dictionary<string, McpParamSchema>();
            var required = new List<string>();

            foreach (var p in parameters)
            {
                var paramAttr = p.GetCustomAttribute<SkillParamAttribute>();
                string paramName = p.Name;

                string paramDesc = paramAttr?.Description ?? string.Empty;
                bool isRequired = paramAttr?.Required ?? !p.IsOptional;

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

        /// <summary>NPCLite JsonHelper 引用（避免在适配器中重复 using）。</summary>
        private static class JsonHelper
        {
            public static string Quote(string s) => NPCLife.Framework.JsonHelper.Quote(s);
        }
    }
}
