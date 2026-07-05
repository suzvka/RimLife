using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;

namespace RimLife.Skills
{
    /// <summary>
    /// Skill 模块发现与加载器。
    /// 
    /// 扫描 Skills/ 目录 → 解析 manifest.json → 加载入口类型 →
    /// 适配为 IMcpHookProvider → 注册到 NPCLife McpSkillRegistry。
    /// 
    /// 兼容两种入口类型：
    /// - ISkillProvider（新路径，第三方推荐）
    /// - IMcpHookProvider（旧路径，向后兼容）
    /// </summary>
    public class SkillModuleLoader
    {
        private readonly ILogger _logger;

        public SkillModuleLoader(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 扫描并加载 Skills/ 目录下所有模块。
        /// 返回成功加载的模块数。
        /// </summary>
        /// <param name="skillsDirectory">Skills/ 目录的绝对路径。</param>
        /// <param name="npcRegistry">NPCLife 的 MCP 技能注册表。</param>
        public int DiscoverAndLoad(string skillsDirectory, IMcpSkillRegistry npcRegistry)
        {
            if (!Directory.Exists(skillsDirectory))
            {
                _logger.Message($"[RimLife.Skills] Skills directory not found: {skillsDirectory}. Skipping module discovery.");
                return 0;
            }

            // 1. 发现所有模块
            var manifests = Discover(skillsDirectory);
            _logger.Message($"[RimLife.Skills] Discovered {manifests.Count} skill module(s) in {skillsDirectory}.");

            if (manifests.Count == 0) return 0;

            // 2. 拓扑排序（按依赖）
            manifests = TopologicalSort(manifests);

            // 3. 逐个加载
            int loaded = 0;
            foreach (var manifest in manifests)
            {
                try
                {
                    if (LoadModule(manifest, npcRegistry))
                        loaded++;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[RimLife.Skills] Failed to load skill '{manifest.Id}': {ex.Message}");
                }
            }

            _logger.Message($"[RimLife.Skills] Loaded {loaded}/{manifests.Count} skill module(s).");
            return loaded;
        }

        // ================================================================
        // 发现
        // ================================================================

        /// <summary>扫描目录，返回所有有效 manifest。</summary>
        public List<SkillManifest> Discover(string skillsDirectory)
        {
            var manifests = new List<SkillManifest>();

            if (!Directory.Exists(skillsDirectory)) return manifests;

            foreach (var subDir in Directory.GetDirectories(skillsDirectory))
            {
                var manifestPath = Path.Combine(subDir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var manifest = ParseManifest(manifestPath);
                    if (manifest == null) continue;

                    manifest.Directory = subDir;

                    if (!manifest.IsValid(out var error))
                    {
                        _logger.Warning($"[RimLife.Skills] Invalid manifest in {subDir}: {error}");
                        continue;
                    }

                    manifests.Add(manifest);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[RimLife.Skills] Failed to parse manifest in {subDir}: {ex.Message}");
                }
            }

            return manifests;
        }

        private SkillManifest ParseManifest(string path)
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var dict = JsonParser.ParseDict(json);
            if (dict == null || dict.Count == 0) return null;

            var manifest = new SkillManifest
            {
                Id = GetDictValue(dict, "id"),
                Version = GetDictValue(dict, "version") ?? "0.0.0",
                ApiVersion = GetDictValue(dict, "apiVersion") ?? "1.0",
                EntryPoint = GetDictValue(dict, "entryPoint"),
                Dependencies = ParseStringArray(dict, "dependencies") ?? new List<string>()
            };

            return manifest;
        }

        // ================================================================
        // 加载
        // ================================================================

        private bool LoadModule(SkillManifest manifest, IMcpSkillRegistry npcRegistry)
        {
            _logger.Message($"[RimLife.Skills] Loading skill '{manifest.Id}' from {manifest.Directory}...");

            // 1. 解析入口类型
            var entryType = ResolveType(manifest);
            if (entryType == null)
            {
                _logger.Warning($"[RimLife.Skills] Failed to resolve entry type '{manifest.EntryPoint}' for skill '{manifest.Id}'.");
                return false;
            }

            // 2. 实例化
            object instance;
            try
            {
                instance = Activator.CreateInstance(entryType);
            }
            catch (Exception ex)
            {
                _logger.Warning($"[RimLife.Skills] Failed to instantiate '{entryType.FullName}' for skill '{manifest.Id}': {ex.Message}");
                return false;
            }

            // 3. 判断接口类型并创建/获取适配器
            IMcpHookProvider hookProvider;

            if (instance is ISkillProvider skillProvider)
            {
                // 新路径
                hookProvider = new McpSkillAdapter(skillProvider, _logger);
                SkillRegistry.Register(manifest, skillProvider.ModuleName, skillProvider.ModuleDescription, skillProvider.TargetRoles);
            }
            else if (instance is IMcpHookProvider legacyProvider)
            {
                // 向后兼容旧 Provider
                hookProvider = legacyProvider;
                // 旧 Provider 的角色信息已在 SkillCatalog 中，此处仅记录 manifest
                SkillRegistry.Register(manifest, legacyProvider.HookName, legacyProvider.HookDescription,
                    Array.Empty<string>()); // 角色由 SkillCatalog 管理
            }
            else
            {
                _logger.Warning($"[RimLife.Skills] Entry type '{entryType.FullName}' implements neither ISkillProvider nor IMcpHookProvider.");
                return false;
            }

            // 4. 注册到 NPCLife
            int toolCount = npcRegistry.RegisterFromProvider(hookProvider);
            _logger.Message($"[RimLife.Skills] Skill '{manifest.Id}' registered: {toolCount} tool(s) [{hookProvider.HookName}].");

            return true;
        }

        // ================================================================
        // 类型解析
        // ================================================================

        private Type ResolveType(SkillManifest manifest)
        {
            var entryPoint = manifest.EntryPoint;

            // 格式: "FullTypeName, AssemblyName"
            var parts = entryPoint.Split(new[] { ',' }, 2);
            if (parts.Length < 2)
            {
                _logger.Warning($"[RimLife.Skills] Invalid entryPoint format '{entryPoint}'. Expected 'TypeName, AssemblyName'.");
                return null;
            }

            string typeName = parts[0].Trim();
            string assemblyName = parts[1].Trim();

            // 尝试从已加载的程序集中查找
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    var type = asm.GetType(typeName, throwOnError: false);
                    if (type != null) return type;
                }
            }

            // 尝试从 Skills 目录加载外部 DLL
            var dllPath = Path.Combine(manifest.Directory, assemblyName + ".dll");
            if (File.Exists(dllPath))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dllPath);
                    var type = asm.GetType(typeName, throwOnError: false);
                    if (type != null) return type;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[RimLife.Skills] Failed to load assembly '{dllPath}': {ex.Message}");
                }
            }

            return null;
        }

        // ================================================================
        // 依赖排序
        // ================================================================

        private List<SkillManifest> TopologicalSort(List<SkillManifest> manifests)
        {
            var sorted = new List<SkillManifest>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = new Dictionary<string, SkillManifest>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in manifests)
                index[m.Id] = m;

            foreach (var m in manifests)
            {
                if (!visited.Contains(m.Id))
                    Visit(m, index, visited, inStack, sorted);
            }

            return sorted;
        }

        private void Visit(SkillManifest current,
            Dictionary<string, SkillManifest> index,
            HashSet<string> visited,
            HashSet<string> inStack,
            List<SkillManifest> sorted)
        {
            visited.Add(current.Id);
            inStack.Add(current.Id);

            foreach (var depId in current.Dependencies)
            {
                if (inStack.Contains(depId))
                {
                    _logger.Warning($"[RimLife.Skills] Circular dependency detected: {current.Id} -> {depId}");
                    continue;
                }

                if (index.TryGetValue(depId, out var dep) && !visited.Contains(depId))
                    Visit(dep, index, visited, inStack, sorted);
            }

            inStack.Remove(current.Id);
            sorted.Add(current);
        }

        // ================================================================
        // JSON 解析辅助
        // ================================================================

        private static string GetDictValue(Dictionary<string, string> dict, string key)
        {
            if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return null;
        }

        private static List<string> ParseStringArray(Dictionary<string, string> dict, string key)
        {
            if (!dict.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return null;

            // raw 是 JSON 数组字符串，如 ["a","b"]
            return JsonParser.ParseStringArray(raw)?.ToList();
        }
    }
}
