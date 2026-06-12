using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RimLife.Framework.Mcp
{
    /// <summary>
    /// MCP Skill 注册表。管理技能的元数据、工具注册和激活状态。
    /// 纯静态，零 RimWorld 依赖。
    /// 
    /// 使用流程：
    ///   1. InitializeDefaults() 注册所有 Skill 元数据
    ///   2. RegisterFromType() 扫描工具类，建立 Skill → Tool 映射
    ///   3. GetActiveToolsJson() 获取当前激活的全部工具定义（用于 prompt 构造）
    ///   4. ActivateSkill() / DeactivateSkill() 管理激活状态
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

        // 预载集合：agent 指定的高频技能，冷启动自动激活（由 RimLifeCore 持久化到 CacheStore）
        private static readonly HashSet<string> _preloadedSkills = new(StringComparer.OrdinalIgnoreCase);

        // 当前激活的 skill 集合（始终包含 "system"）
        private static readonly HashSet<string> _activeSkills = new(StringComparer.OrdinalIgnoreCase) { "system" };

        private static readonly object _lock = new();

        /// <summary>系统技能 ID，始终激活。</summary>
        public const string SystemSkillId = "system";

        // ================================================================
        // 初始化
        // ================================================================

        /// <summary>
        /// 注册 7 个业务技能的元数据。调用一次即可。
        /// 调用后自动将 system skill 设为激活。
        /// </summary>
        public static void InitializeDefaults()
        {
            lock (_lock)
            {
                _skillMetas.Clear();
                _skillTools.Clear();
                _preloadedSkills.Clear();
                _activeSkills.Clear();
                _activeSkills.Add(SystemSkillId);

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
                RegisterSkill("workspace_management", "工作空间",
                    "剧情线工作空间的创建、查询、分支、合并、生命周期管理");
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
        // 查询
        // ================================================================

        /// <summary>
        /// 获取轻量技能列表 JSON（所有已注册技能，含激活状态）。
        /// 格式: {"skills": [{"id":"...","name":"...","desc":"...","toolCount":N,"active":bool}, ...]}
        /// </summary>
        public static string GetSkillListJson()
        {
            lock (_lock)
            {
                var skills = new List<string>();
                foreach (var kv in _skillMetas)
                {
                    int toolCount = _skillTools.TryGetValue(kv.Key, out var tools) ? tools.Count : 0;
                    bool active = _activeSkills.Contains(kv.Key);

                    var w = new JsonWriter(128);
                    w.Prop("id", kv.Value.Id);
                    w.Prop("name", kv.Value.Name);
                    w.Prop("description", kv.Value.Description);
                    w.Prop("toolCount", toolCount);
                    w.Prop("active", active);
                // preloaded 状态
                w.Prop("preloaded", _preloadedSkills.Contains(kv.Key));
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

                // 附加已激活列表
                sb.Append(",\"activeSkillIds\":[");
                bool first = true;
                foreach (var id in _activeSkills)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(JsonHelper.Escape(id));
                    sb.Append('"');
                }
                sb.Append(']');

                // 附加预载列表
                sb.Append(",\"preloadedSkillIds\":[");
                first = true;
                foreach (var id in _preloadedSkills)
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
        /// 获取当前激活技能的全部工具定义 JSON 数组。
        /// 用于构造发送给 LLM 的 prompt 中的 tools 字段。
        /// </summary>
        public static string GetActiveToolsJson()
        {
            lock (_lock)
            {
                var jsons = new List<string>();
                foreach (var skillId in _activeSkills)
                {
                    if (_skillTools.TryGetValue(skillId, out var tools))
                    {
                        foreach (var tool in tools)
                            jsons.Add(McpToolGenerator.Serialize(tool.Definition));
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
        /// 获取当前已激活的技能 ID 列表。
        /// </summary>
        public static IReadOnlyList<string> GetActiveSkillIds()
        {
            lock (_lock)
            {
                return _activeSkills.ToList();
            }
        }

        /// <summary>
        /// 判断技能是否已激活。
        /// </summary>
        public static bool IsActive(string skillId)
        {
            lock (_lock)
            {
                return _activeSkills.Contains(skillId);
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
        /// 获取已激活技能总数（含 system）。
        /// </summary>
        public static int ActiveSkillCount
        {
            get { lock (_lock) { return _activeSkills.Count; } }
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
        // 工具调用
        // ================================================================

        /// <summary>
        /// 在当前已激活技能中查找指定名称的工具并调用。
        /// 对 MethodInfo 工具和 Hook 工具统一处理。
        /// </summary>
        /// <param name="toolName">工具名称（Definition.Name）。</param>
        /// <param name="jsonArgs">JSON 对象格式的参数字符串。</param>
        /// <returns>工具返回的 JSON 字符串，未找到或异常时返回 error JSON。</returns>
        public static string InvokeTool(string toolName, string jsonArgs)
        {
            if (string.IsNullOrEmpty(toolName))
                return MakeError("toolName is required");

            lock (_lock)
            {
                foreach (var skillId in _activeSkills)
                {
                    if (_skillTools.TryGetValue(skillId, out var tools))
                    {
                        foreach (var tool in tools)
                        {
                            if (string.Equals(tool.Definition.Name, toolName, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    return tool.Invoker(jsonArgs ?? "{}");
                                }
                                catch (Exception ex)
                                {
                                    return "{\"error\":" + JsonHelper.Quote(ex.Message) + "}";
                                }
                            }
                        }
                    }
                }
            }

            return MakeError($"Tool '{toolName}' not found or not active.");
        }

        // ================================================================
        // 预载管理（冷启动自动激活，持久化由 RimLifeCore 负责）
        // ================================================================

        /// <summary>
        /// 将一个技能加入预载列表并立即激活。预载技能在每次冷启动时自动激活。
        /// system 技能始终预载，无需手动添加。
        /// </summary>
        public static string AddPreload(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return MakeError("skillId is required");

            lock (_lock)
            {
                if (!_skillMetas.ContainsKey(skillId))
                    return MakeError($"Skill '{skillId}' not found.");

                if (!_preloadedSkills.Add(skillId))
                {
                    // 已预载，直接返回当前状态
                    _activeSkills.Add(skillId); // 确保激活
                    return MakePreloadResult(skillId, "already_preloaded");
                }

                _activeSkills.Add(skillId);
                return MakePreloadResult(skillId, "preloaded");
            }
        }

        /// <summary>
        /// 从预载列表中移除技能，但不反激活（agent 可以继续在当前会话中使用）。
        /// system 技能不可移除预载。
        /// </summary>
        public static string RemovePreload(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return MakeError("skillId is required");

            lock (_lock)
            {
                if (string.Equals(skillId, SystemSkillId, StringComparison.OrdinalIgnoreCase))
                    return MakeError($"Cannot remove preload for system skill '{SystemSkillId}'.");

                if (!_skillMetas.ContainsKey(skillId))
                    return MakeError($"Skill '{skillId}' not found.");

                bool wasRemoved = _preloadedSkills.Remove(skillId);
                return MakePreloadResult(skillId, wasRemoved ? "unpreloaded" : "was_not_preloaded");
            }
        }

        /// <summary>
        /// 获取当前预载的技能 ID 列表（供 RimLifeCore 持久化）。
        /// </summary>
        public static IReadOnlyList<string> GetPreloadSkillIds()
        {
            lock (_lock)
            {
                return _preloadedSkills.ToList();
            }
        }

        /// <summary>
        /// 将指定技能列表批量激活（用于冷启动时从 CacheStore 恢复预载配置）。
        /// 未知的技能 ID 静默跳过。
        /// </summary>
        /// <returns>成功激活的技能数。</returns>
        public static int ApplyPreloads(IEnumerable<string> skillIds)
        {
            if (skillIds == null) return 0;
            int count = 0;
            lock (_lock)
            {
                foreach (var id in skillIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!_skillMetas.ContainsKey(id)) continue;
                    if (_preloadedSkills.Add(id)) count++;
                    _activeSkills.Add(id);
                }
            }
            return count;
        }

        /// <summary>
        /// 将当前已预载的技能全部激活（冷启动快捷入口，ApplyPreloads 的零参数版本）。
        /// </summary>
        /// <returns>成功激活的技能数。</returns>
        public static int ApplyPreloads()
        {
            lock (_lock)
            {
                int count = 0;
                foreach (var id in _preloadedSkills)
                {
                    if (_activeSkills.Add(id)) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 判断技能是否已预载。
        /// </summary>
        public static bool IsPreloaded(string skillId)
        {
            lock (_lock)
            {
                return _preloadedSkills.Contains(skillId);
            }
        }

        // ================================================================
        // 激活管理
        // ================================================================

        /// <summary>
        /// 激活一个技能。若技能不存在或已激活则静默返回。
        /// 返回 JSON: {"activated":["skillId"],"newTools":[...], "activeSkills":[...]}
        /// </summary>
        public static string ActivateSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return MakeError("skillId is required");

            lock (_lock)
            {
                if (!_skillMetas.ContainsKey(skillId))
                    return MakeError($"Skill '{skillId}' not found. Use list_skills to see available skills.");

                if (_activeSkills.Contains(skillId))
                {
                    // 已激活，返回空工具列表
                    return MakeActivateResult(skillId, "[]");
                }

                _activeSkills.Add(skillId);

                string newToolsJson = GetSkillToolsJson(skillId);
                return MakeActivateResult(skillId, newToolsJson);
            }
        }

        /// <summary>
        /// 反激活一个技能。system 技能不可反激活。
        /// 返回 JSON: {"deactivated":"skillId","activeSkills":[...]}
        /// </summary>
        public static string DeactivateSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return MakeError("skillId is required");

            lock (_lock)
            {
                if (string.Equals(skillId, SystemSkillId, StringComparison.OrdinalIgnoreCase))
                    return MakeError($"Cannot deactivate system skill '{SystemSkillId}'.");

                if (!_skillMetas.ContainsKey(skillId))
                    return MakeError($"Skill '{skillId}' not found.");

                if (!_activeSkills.Remove(skillId))
                {
                    // 未激活，也算成功
                }

                return MakeDeactivateResult(skillId);
            }
        }

        /// <summary>
        /// 重置激活状态为仅 system。
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _activeSkills.Clear();
                _activeSkills.Add(SystemSkillId);
            }
        }

        // ================================================================
        // 内部 JSON 构造
        // ================================================================

        private static string MakeActivateResult(string skillId, string newToolsJson)
        {
            var w = new JsonWriter(512);
            w.Array("activated", new List<string> { skillId });
            w.PropRaw("newTools", newToolsJson);
            w.Array("activeSkills", _activeSkills.ToList());
            return w.Close();
        }

        private static string MakeDeactivateResult(string skillId)
        {
            var w = new JsonWriter(256);
            w.Prop("deactivated", skillId);
            w.Array("activeSkills", _activeSkills.ToList());
            return w.Close();
        }

        private static string MakePreloadResult(string skillId, string action)
        {
            var w = new JsonWriter(256);
            w.Prop("action", action);
            w.Prop("skillId", skillId);
            w.Array("activeSkills", _activeSkills.ToList());
            w.Array("preloadedSkills", _preloadedSkills.ToList());
            return w.Close();
        }

        private static string MakeError(string message)
        {
            var w = new JsonWriter(128);
            w.Prop("error", true);
            w.Prop("message", message ?? "");
            return w.Close();
        }
    }
}
