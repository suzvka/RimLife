using RimLife.Core;
using RimLife.Framework.Mcp;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLife 核心服务定位器。提供持久化存储、事件日志、交互历史、工作空间和知识库的全局访问。
    /// </summary>
    public static class RimLifeCore
    {
        private static bool _skillRegistryInitialized;
        private static readonly object _skillRegistryLock = new object();

        static RimLifeCore()
        {
            // 注入日志出口：知识库模块通过静态回调输出日志，具体实现由 RimWorld 侧提供
            Knowledge.BuiltInKnowledgeBase.LogInfo = Log.Message;
            Knowledge.BuiltInKnowledgeBase.LogWarning = Log.Warning;
        }

        /// <summary>
        /// 初始化 MCP Skill 注册表。注册所有 Skill 元数据，
        /// 扫描 4 个工具类型自动建立 Skill → Tool 映射。
        /// 幂等，可多次调用。
        /// </summary>
        public static void EnsureSkillRegistryInitialized()
        {
            if (_skillRegistryInitialized) return;
            lock (_skillRegistryLock)
            {
                if (_skillRegistryInitialized) return;

                McpSkillRegistry.InitializeDefaults();

                // 注册 4 个工具类（SystemMcpTools 最先注册，确保 system skill 的工具总是可用）
                int count = McpSkillRegistry.RegisterFromType(typeof(Mcp.SystemMcpTools));
                count += McpSkillRegistry.RegisterFromType(typeof(Mcp.DirectorMcpTools));
                count += McpSkillRegistry.RegisterFromType(typeof(Mcp.KnowledgeMcpTools));
                count += McpSkillRegistry.RegisterFromType(typeof(Workspace.WorkspaceMcpTools));

                Log.Message($"[RimLife.Core] SkillRegistry initialized: {McpSkillRegistry.SkillCount} skills, {count} tools registered.");

                _skillRegistryInitialized = true;
            }

            // 冷启动：从缓存恢复预载技能
            LoadAndApplySkillPreloads();
        }

        // ----------------------------------------------------------------
        // 技能预载持久化
        // ----------------------------------------------------------------

        private const string _skillPreloadsCacheKey = "rimlife_skill_preloads";

        /// <summary>
        /// 将当前预载的技能列表持久化到 CacheStore。
        /// </summary>
        internal static void SaveSkillPreloads()
        {
            try
            {
                var preloadIds = McpSkillRegistry.GetPreloadSkillIds();
                CacheStore.Cache(_skillPreloadsCacheKey, preloadIds);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimLife.Core] SaveSkillPreloads failed: {e.Message}");
            }
        }

        /// <summary>
        /// 从 CacheStore 加载预载技能列表并应用到注册表。
        /// 缓存不可用或为空时静默返回。
        /// </summary>
        private static void LoadAndApplySkillPreloads()
        {
            try
            {
                var preloadIds = CacheStore.FetchCache<List<string>>(_skillPreloadsCacheKey);
                if (preloadIds != null && preloadIds.Count > 0)
                {
                    int count = McpSkillRegistry.ApplyPreloads(preloadIds);
                    Log.Message($"[RimLife.Core] Preloaded {count} skills from cache: [{string.Join(", ", preloadIds)}]");
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimLife.Core] LoadAndApplySkillPreloads failed: {e.Message}");
            }
        }

        private static IPersistentStore _saveStore;

        /// <summary>
        /// 权威存储（存档文件）。由 RimWorldSaveStore 在初始化时注册。
        /// 设为新值时自动重置 EventLog 和 InteractionStore，避免跨存档引用失效。
        /// </summary>
        public static IPersistentStore SaveStore
        {
            get => _saveStore;
            internal set
            {
                if (_saveStore != value)
                {
                    _saveStore = value;
                    _eventLog = null;
                    _interactionStore = null;
                    _workspaces = null;
                    _knowledgeBase = null;
                }
            }
        }

        /// <summary>缓存存储（本地文件）。</summary>
        public static IPersistentStore CacheStore { get; } = new LocalFileStore();

        private static IEventLog _eventLog;
        private static readonly object _eventLogLock = new object();

        /// <summary>
        /// 事件日志实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static IEventLog EventLog
        {
            get
            {
                if (_eventLog == null)
                {
                    lock (_eventLogLock)
                    {
                        if (_eventLog == null && SaveStore != null)
                        {
                            _eventLog = new RimWorldEventLog(SaveStore);
                        }
                    }
                }
                return _eventLog;
            }
        }

        private static IInteractionStore _interactionStore;
        private static readonly object _interactionStoreLock = new object();

        /// <summary>
        /// 交互历史存储实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static IInteractionStore InteractionStore
        {
            get
            {
                if (_interactionStore == null)
                {
                    lock (_interactionStoreLock)
                    {
                        if (_interactionStore == null && SaveStore != null)
                        {
                            _interactionStore = new InteractionHistoryStore(SaveStore);
                        }
                    }
                }
                return _interactionStore;
            }
        }

        private static IKnowledgeBase _knowledgeBase;
        private static readonly object _knowledgeBaseLock = new object();

        /// <summary>
        /// 知识库实例。首次访问时从 CacheStore 延迟创建 BuiltInKnowledgeBase，
        /// 并包装在 KnowledgeBaseChain 中（默认仅 L1，后续可追加 L2/L3）。
        /// CacheStore 不可用时返回 null。
        /// </summary>
        public static IKnowledgeBase KnowledgeBase
        {
            get
            {
                if (_knowledgeBase == null)
                {
                    lock (_knowledgeBaseLock)
                    {
                        if (_knowledgeBase == null && CacheStore != null)
                        {
                            var builtIn = new Knowledge.BuiltInKnowledgeBase(CacheStore);
                            var gameDef = new Knowledge.GameDefKnowledgeBase();
                            _knowledgeBase = new Framework.KnowledgeBaseChain(builtIn, gameDef);
                        }
                    }
                }
                return _knowledgeBase;
            }
        }

        private static Workspace.WorkspaceManager _workspaces;
        private static readonly object _workspacesLock = new object();

        /// <summary>
        /// 工作空间管理器实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static Workspace.WorkspaceManager Workspaces
        {
            get
            {
                if (_workspaces == null)
                {
                    lock (_workspacesLock)
                    {
                        if (_workspaces == null && SaveStore != null)
                        {
                            _workspaces = new Workspace.WorkspaceManager(SaveStore);
                        }
                    }
                }
                return _workspaces;
            }
        }
    }
}
