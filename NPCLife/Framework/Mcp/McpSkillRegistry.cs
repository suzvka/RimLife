using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// MCP Skill 注册表。管理技能的元数据和工具注册。
    /// 纯静态，零 RimWorld 依赖。
    /// 
    /// 激活状态由 WorkspaceManager 独家持有。本注册表提供纯函数：
    /// 给定一组 activeSkillIds，返回对应的工具定义或 skill 列表。
    /// 
    /// 使用流程：
    ///   1. InitializeDefaults() 注册所有 Skill 元数据
    ///   2. RegisterFromType() 扫描工具类，建立 Skill → Tool 映射
    ///   3. GetActiveToolsJson(activeSkillIds) 获取工具定义（用于 prompt 构造）
    ///   4. InvokeTool(activeSkillIds, toolName, jsonArgs) 调用工具
    /// </summary>
    public static class McpSkillRegistry
    {
        /// <summary>
        /// Skill 元数据。轻量 DTO，用于技能列表展示。
        /// </summary>
        public struct SkillMeta
        {
            public string Id;
            public string Name;
            public string Description;
        }

        // skill 元数据
        private static readonly Dictionary<string, SkillMeta> _skillMetas = new(StringComparer.OrdinalIgnoreCase);

        // skill → McpTool 列表
        private static readonly Dictionary<string, List<McpTool>> _skillTools = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new();

        /// <summary>系统技能 ID，对所有 workspace 隐式可用。</summary>
        public const string SystemSkillId = "system";

        // ================================================================
        // 初始化
        // ================================================================

        /// <summary>
        /// 注册 7 个业务技能的元数据。调用一次即可。
        /// </summary>
        public static void InitializeDefaults()
        {
            lock (_lock)
            {
                _skillMetas.Clear();
                _skillTools.Clear();

                RegisterSkill("colony_overview", "殖民地全局",
                    "殖民地概览、近期事件、活跃目标、资源库存");
                RegisterSkill("character_query", "角色查询",
                    "获取角色完整人物卡、按条件筛选殖民者、列出全部角色");
                RegisterSkill("relationship_query", "关系网络",
                    "查询角色社交关系、交互历史流水");
                RegisterSkill("event_query", "事件回溯",
                    "多维事件历史查询（标签、时间、Actor、严重度）");
                RegisterSkill("environment_query", "环境感知",
                    "查询角色当前所处的环境信息（室内外、温光、天气、房间）");
                RegisterSkill("knowledge_management", "知识管理",
                    "词条查询、学习、列表、删除、统计");
                RegisterSkill("workspace_direction", "工作空间(导演)",
                    "剧情线工作空间的创建、分支、合并、生命周期管理。导演专用。");
                RegisterSkill("workspace_writing", "工作空间(编剧)",
                    "查看工作空间完整内容、推送叙事回合、上报推进状态信号。编剧专用。");
            }
        }

        /// <summary>
        /// 注册单个技能的元数据。InitializeDefaults 已包含全部业务技能，
        /// 测试或动态扩展场景可使用此方法注册额外技能。
        /// </summary>
        public static void RegisterSkill(string id, string name, string description)
        {
            _skillMetas[id] = new SkillMeta { Id = id, Name = name, Description = description };
            if (!_skillTools.ContainsKey(id))
                _skillTools[id] = new List<McpTool>();
        }

        // ================================================================
        // 工具注册
        // ================================================================

        /// <summary>
        /// 注册 McpTool 到指定技能。同一工具名不会重复添加。
        /// 这是核心注册入口。
        /// </summary>
        public static bool RegisterTool(string skillId, McpTool tool)
        {
            if (string.IsNullOrEmpty(skillId) || tool == null) return false;

            lock (_lock)
            {
                if (!_skillTools.TryGetValue(skillId, out var list))
                {
                    list = new List<McpTool>();
                    _skillTools[skillId] = list;
                }

                // 按名称去重
                if (!list.Any(t => string.Equals(t.Definition.Name, tool.Definition.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(tool);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 注册 MethodInfo 工具到指定技能。内部包装为 McpTool。
        /// 保留此重载以兼容现有调用方。
        /// </summary>
        public static bool RegisterTool(string skillId, MethodInfo method)
        {
            if (string.IsNullOrEmpty(skillId) || method == null) return false;
            if (method.GetCustomAttribute<McpToolAttribute>() == null) return false;
            return RegisterTool(skillId, McpTool.FromMethod(method));
        }

        /// <summary>
        /// 从类型自动扫描并注册工具到技能。
        /// 优先级：方法级 [McpSkill] > 类级 [McpSkill]。
        /// 无任何 [McpSkill] 标注的方法将被跳过。
        /// </summary>
        public static int RegisterFromType(Type type)
        {
            if (type == null) return 0;

            // 读取类级默认 Skill
            var classSkill = type.GetCustomAttribute<McpSkillAttribute>();
            string classSkillId = classSkill?.SkillId;

            int count = 0;
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.GetCustomAttribute<McpToolAttribute>() == null) continue;

                // 方法级标注优先
                var methodSkill = m.GetCustomAttribute<McpSkillAttribute>();
                string skillId = methodSkill?.SkillId ?? classSkillId;

                if (!string.IsNullOrEmpty(skillId) && RegisterTool(skillId, McpTool.FromMethod(m)))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 从 Hook 提供者注册工具。自动创建/更新对应 Skill 元数据，
        /// 并将提供者的工具注册到该 Skill 下。
        /// </summary>
        /// <returns>成功注册的工具数。</returns>
        public static int RegisterFromProvider(IMcpHookProvider provider)
        {
            if (provider == null) return 0;

            lock (_lock)
            {
                // 确保 Skill 元数据存在（若已存在则覆盖 name/description）
                RegisterSkill(provider.HookId, provider.HookName, provider.HookDescription);

                int count = 0;
                var tools = provider.GetTools();
                if (tools != null)
                {
                    foreach (var tool in tools)
                    {
                        if (RegisterTool(provider.HookId, tool))
                            count++;
                    }
                }
                return count;
            }
        }

        // ================================================================
        // 查询（纯函数：输入 activeSkillIds，输出 JSON）
        // ================================================================

        /// <summary>
        /// 获取轻量技能列表 JSON（含激活状态）。system 始终显示为 active。
        /// </summary>
        /// <param name="activeSkillIds">当前激活的业务 skill ID 集合。</param>
        public static string GetSkillListJson(IEnumerable<string> activeSkillIds)
        {
            lock (_lock)
            {
                var activeSet = activeSkillIds != null
                    ? new HashSet<string>(activeSkillIds, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();

                var skills = new List<string>();

                // system 技能始终最先列出，隐式可用（不在 _skillMetas 中注册）
                int systemToolCount = _skillTools.TryGetValue(SystemSkillId, out var sysTools) ? sysTools.Count : 0;
                var sw = new JsonWriter(128);
                sw.Prop("id", SystemSkillId);
                sw.Prop("name", "系统");
                sw.Prop("description", "系统元工具集（技能列表、激活、反激活）");
                sw.Prop("toolCount", systemToolCount);
                sw.Prop("active", true);
                skills.Add(sw.Close());

                foreach (var kv in _skillMetas)
                {
                    int toolCount = _skillTools.TryGetValue(kv.Key, out var tools) ? tools.Count : 0;
                    bool active = activeSet.Contains(kv.Key);

                    var w = new JsonWriter(128);
                    w.Prop("id", kv.Value.Id);
                    w.Prop("name", kv.Value.Name);
                    w.Prop("description", kv.Value.Description);
                    w.Prop("toolCount", toolCount);
                    w.Prop("active", active);
                    skills.Add(w.Close());
                }

                var sb = new StringBuilder(512);
                sb.Append("{\"skills\":[");
                for (int i = 0; i < skills.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(skills[i]);
                }
                sb.Append(']');

                sb.Append(",\"activeSkillIds\":[");
                bool first = true;
                foreach (var id in activeSet)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(JsonHelper.Escape(id));
                    sb.Append('"');
                }
                sb.Append(']');
                sb.Append('}');
                return sb.ToString();
            }
        }

        /// <summary>
        /// 获取激活工具定义 JSON 数组。system 技能的工具始终包含，然后合并传入的业务技能。
        /// 用于构造发送给 LLM 的 prompt 中的 tools 字段。
        /// </summary>
        /// <param name="activeSkillIds">当前激活的业务 skill ID 集合。</param>
        public static string GetActiveToolsJson(IEnumerable<string> activeSkillIds)
        {
            lock (_lock)
            {
                var jsons = new List<string>();

                // system 技能始终可用
                if (_skillTools.TryGetValue(SystemSkillId, out var sysTools))
                {
                    foreach (var tool in sysTools)
                        jsons.Add(McpToolGenerator.Serialize(tool.Definition));
                }

                // 传入的业务技能
                if (activeSkillIds != null)
                {
                    foreach (var skillId in activeSkillIds)
                    {
                        if (string.IsNullOrEmpty(skillId) || skillId == SystemSkillId) continue;
                        if (_skillTools.TryGetValue(skillId, out var tools))
                        {
                            foreach (var tool in tools)
                                jsons.Add(McpToolGenerator.Serialize(tool.Definition));
                        }
                    }
                }

                if (jsons.Count == 0) return "[]";

                var sb = new StringBuilder("[\n");
                for (int i = 0; i < jsons.Count; i++)
                {
                    if (i > 0) sb.Append(",\n");
                    sb.Append(jsons[i]);
                }
                sb.Append("\n]");
                return sb.ToString();
            }
        }

        /// <summary>
        /// 获取指定技能的工具定义 JSON 数组。
        /// </summary>
        public static string GetSkillToolsJson(string skillId)
        {
            lock (_lock)
            {
                if (!_skillTools.TryGetValue(skillId, out var tools) || tools.Count == 0)
                    return "[]";

                var jsons = new List<string>();
                foreach (var tool in tools)
                    jsons.Add(McpToolGenerator.Serialize(tool.Definition));

                var sb = new StringBuilder("[");
                for (int i = 0; i < jsons.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(jsons[i]);
                }
                sb.Append(']');
                return sb.ToString();
            }
        }

        /// <summary>
        /// 返回所有已注册的技能 ID 列表。
        /// </summary>
        public static IReadOnlyList<string> GetAllSkillIds()
        {
            lock (_lock)
            {
                return _skillMetas.Keys.ToList();
            }
        }

        /// <summary>
        /// 获取已注册技能总数。
        /// </summary>
        public static int SkillCount
        {
            get { lock (_lock) { return _skillMetas.Count; } }
        }

        /// <summary>
        /// 获取已注册工具总数（所有技能）。
        /// </summary>
        public static int TotalToolCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var kv in _skillTools) count += kv.Value.Count;
                    return count;
                }
            }
        }

        // ================================================================
        // 工具调用（纯函数：由调用方提供 activeSkillIds）
        // ================================================================

        /// <summary>
        /// 在给定激活技能范围内查找并调用工具。
        /// 搜索顺序：业务技能 → system 技能（fallback）。
        /// </summary>
        /// <param name="activeSkillIds">当前激活的业务 skill ID 集合。</param>
        /// <param name="toolName">工具名称（Definition.Name）。</param>
        /// <param name="jsonArgs">JSON 对象格式的参数字符串。</param>
        /// <returns>工具返回的 JSON 字符串，未找到或异常时返回 error JSON。</returns>
        public static string InvokeTool(IEnumerable<string> activeSkillIds, string toolName, string jsonArgs)
        {
            if (string.IsNullOrEmpty(toolName))
                return MakeError("toolName is required");

            // 发布工具调用前事件
            EventBus.Publish(FrameworkEvents.ToolInvoking, EventArg.WithPayload(
                ("toolName", toolName),
                ("source", "McpSkillRegistry")
            ));

            string result;
            lock (_lock)
            {
                result = InvokeToolInternal(activeSkillIds, toolName, jsonArgs);
            }

            // 发布工具调用后事件
            EventBus.Publish(FrameworkEvents.ToolInvoked, EventArg.WithPayload(
                ("toolName", toolName),
                ("resultLength", (result?.Length ?? 0).ToString())
            ));

            return result;
        }

        private static string InvokeToolInternal(IEnumerable<string> activeSkillIds, string toolName, string jsonArgs)
        {
            // 1. 先搜传入的业务技能
            if (activeSkillIds != null)
            {
                foreach (var skillId in activeSkillIds)
                {
                    if (string.IsNullOrEmpty(skillId)) continue;
                    if (_skillTools.TryGetValue(skillId, out var tools))
                    {
                        foreach (var tool in tools)
                        {
                            if (string.Equals(tool.Definition.Name, toolName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { return tool.Invoker(jsonArgs ?? "{}"); }
                                catch (Exception ex)
                                {
                                    ErrorHandler.ReportError("McpTool", ex, new System.Collections.Generic.Dictionary<string, string>
                                    {
                                        {"toolName", toolName}, {"skillId", skillId}
                                    });
                                    return "{\"error\":" + JsonHelper.Quote(ex.Message) + "}";
                                }
                            }
                        }
                    }
                }
            }

            // 2. fallback 到 system（始终可用）
            if (_skillTools.TryGetValue(SystemSkillId, out var sysTools))
            {
                foreach (var tool in sysTools)
                {
                    if (string.Equals(tool.Definition.Name, toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        try { return tool.Invoker(jsonArgs ?? "{}"); }
                        catch (Exception ex)
                        {
                            ErrorHandler.ReportError("McpTool", ex, new System.Collections.Generic.Dictionary<string, string>
                            {
                                {"toolName", toolName}, {"skillId", SystemSkillId}
                            });
                            return "{\"error\":" + JsonHelper.Quote(ex.Message) + "}";
                        }
                    }
                }
            }

            return MakeError($"Tool '{toolName}' not available.");
        }

        // ================================================================
        // 公开 JSON 构造工具（供 WorkspaceManager 使用）
        // ================================================================

        /// <summary>构造激活成功结果 JSON。</summary>
        public static string MakeActivateResult(string skillId, string newToolsJson)
        {
            var w = new JsonWriter(512);
            w.Array("activated", new List<string> { skillId });
            w.PropRaw("newTools", newToolsJson);
            return w.Close();
        }

        /// <summary>构造反激活成功结果 JSON。</summary>
        public static string MakeDeactivateResult(string skillId)
        {
            var w = new JsonWriter(256);
            w.Prop("deactivated", skillId);
            return w.Close();
        }

        /// <summary>构造错误结果 JSON。</summary>
        public static string MakeError(string message)
        {
            var w = new JsonWriter(128);
            w.Prop("error", true);
            w.Prop("message", message ?? "");
            return w.Close();
        }
    }
}
