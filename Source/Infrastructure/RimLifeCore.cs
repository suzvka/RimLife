using RimLife.Agent;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Driver;
using RimLife.Framework.Mcp;
using RimLife.Infrastructure.Llm;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLife 核心服务定位器。提供持久化存储、事件日志、交互历史、
    /// 工作空间、知识库和 LLM API 访问的全局访问。
    /// </summary>
    public static class RimLifeCore
    {
        private static bool _skillRegistryInitialized;
        private static readonly object _skillRegistryLock = new object();
        private static FrameworkConfig _frameworkConfig;

        /// <summary>日志接口。由适配层在启动时注入。</summary>
        public static ILogger Logger { get; internal set; }

        /// <summary>Pawn 语义提示词提供者。游戏侧实现，提供各维度的自然语言描述。</summary>
        public static IPawnPromptProvider PromptProvider { get; internal set; }

        /// <summary>时间字符串提供者。游戏侧注入，返回当前游戏时间的格式化字符串。
        /// 框架只原样透传，不解析语义。Agent 可通过 get_current_time 工具获取。</summary>
        public static Func<string> TimeProvider { get; internal set; }

        /// <summary>当前框架配置。未配置时返回默认配置。</summary>
        public static FrameworkConfig Config => _frameworkConfig ?? FrameworkConfig.CreateDefault();

        /// <summary>
        /// 统一配置入口。设置全局配置并触发配置就绪通知。
        /// 配置合并优先级：默认值 &lt; 配置文件 &lt; 代码覆盖。
        /// </summary>
        public static void Configure(FrameworkConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var errors = config.Validate();
            if (errors.Count > 0)
            {
                Logger?.Warning($"[RimLife.Core] Config validation failed: {string.Join("; ", errors)}");
                return;
            }

            _frameworkConfig = config;

            // 同步到各子系统
            _driverConfig = config.ToDriverConfig();
            ErrorHandler.DiagnosticMode = config.Diagnostics?.EnableVerboseLogging ?? false;

            config.Freeze();
            LifecycleManager.NotifyConfigReady();
            EventBus.Publish(FrameworkEvents.ConfigReady);

            Logger?.Message("[RimLife.Core] Configuration applied and frozen.");
        }

        /// <summary>
        /// 关闭框架。级联销毁所有组件，清空事件总线。
        /// 应在 Mod 卸载或游戏退出时调用。
        /// </summary>
        public static void Shutdown()
        {
            Logger?.Message("[RimLife.Core] Shutting down...");
            LifecycleManager.Shutdown();
            FrameworkStatus.Clear();
            ErrorHandler.ClearHandlers();
            _frameworkConfig = null;
            _skillRegistryInitialized = false;
            Logger?.Message("[RimLife.Core] Shutdown complete.");
        }

        /// <summary>
        /// 初始化 MCP Skill 注册表。注册所有 Skill 元数据，
        /// 扫描工具类型自动建立 Skill → Tool 映射。
        /// 幂等，可多次调用。
        /// </summary>
        public static void EnsureSkillRegistryInitialized()
        {
            if (_skillRegistryInitialized) return;
            lock (_skillRegistryLock)
            {
                if (_skillRegistryInitialized) return;

                McpSkillRegistry.InitializeDefaults();

                int count = McpSkillRegistry.RegisterFromType(typeof(Mcp.SystemMcpTools));
                count += McpSkillRegistry.RegisterFromType(typeof(Mcp.KnowledgeMcpTools));
                count += RegisterHookProvider(new Workspace.DirectionMcpProvider(() => Workspaces, Logger));
                count += RegisterHookProvider(new Workspace.WritingMcpProvider(() => Workspaces, Logger));

                // Hook Providers（游戏侧通过 IMcpHookProvider 实现）
                count += RegisterHookProvider(new Mcp.ColonyOverviewProvider());
                count += RegisterHookProvider(new Mcp.CharacterQueryProvider());
                count += RegisterHookProvider(new Mcp.EventQueryProvider());
                count += RegisterHookProvider(new Mcp.RelationshipQueryProvider());
                count += RegisterHookProvider(new Mcp.EnvironmentQueryProvider());
                count += RegisterHookProvider(new Mcp.PawnMemoryProvider());

                Logger?.Message($"[RimLife.Core] SkillRegistry initialized: {McpSkillRegistry.SkillCount} skills, {count} tools registered.");

                _skillRegistryInitialized = true;

                // 注入 Logger 到基础设施组件
                EventBus.Logger = Logger;
                LifecycleManager.Logger = Logger;
                ErrorHandler.Logger = Logger;
                AgentPipeline.Logger = Logger;

                // 初始化生命周期管理器
                LifecycleManager.Initialize();

                // 注册 FrameworkStatus 报告器
                RegisterStatusReporters();

                // 注册基础能力标识
                FrameworkStatus.RegisterCapability("mcp_tools", true);
                FrameworkStatus.RegisterCapability("event_bus", true);
                FrameworkStatus.RegisterCapability("agent_pipeline", true);
                FrameworkStatus.RegisterCapability("lifecycle_hooks", true);
            }
        }

        private static void RegisterStatusReporters()
        {
            FrameworkStatus.RegisterReporter("Llm", () => new ComponentStatus
            {
                Name = "Llm",
                IsAvailable = _llmAccessor != null && _llmAccessor.IsConfigured,
                Detail = _llmAccessor != null ? $"{_llmAccessor.AdapterTypeName}" : "not initialized"
            });

            FrameworkStatus.RegisterReporter("EventLog", () => new ComponentStatus
            {
                Name = "EventLog",
                IsAvailable = _eventLog != null,
                Detail = _eventLog != null ? $"{_eventLog.TotalAppended} events appended" : "not initialized"
            });

            FrameworkStatus.RegisterReporter("Workspace", () => new ComponentStatus
            {
                Name = "Workspace",
                IsAvailable = _workspaces != null,
                Detail = _workspaces != null ? "active" : "not initialized"
            });

            FrameworkStatus.RegisterReporter("KnowledgeBase", () => new ComponentStatus
            {
                Name = "KnowledgeBase",
                IsAvailable = _knowledgeBase != null,
                Detail = _knowledgeBase != null ? $"{_knowledgeBase.Count} entries" : "not initialized"
            });
        }

        // ----------------------------------------------------------------
        // Hook Provider 注册
        // ----------------------------------------------------------------

        /// <summary>
        /// 注册一个 MCP Hook 提供者。适配器侧实现 IMcpHookProvider，
        /// 通过此方法将外部工具注册到 Skill 系统中。
        /// 应在 EnsureSkillRegistryInitialized() 之后调用。
        /// </summary>
        public static int RegisterHookProvider(IMcpHookProvider provider)
        {
            if (provider == null) return 0;

            try
            {
                EnsureSkillRegistryInitialized();
                int count = McpSkillRegistry.RegisterFromProvider(provider);
                Logger?.Message($"[RimLife.Core] HookProvider '{provider.HookId}' registered: {count} tools.");
                return count;
            }
            catch (System.Exception e)
            {
                Logger?.Warning($"[RimLife.Core] RegisterHookProvider({provider.HookId}) failed: {e.Message}");
                return 0;
            }
        }

        private static IPersistentStore _saveStore;

        /// <summary>
        /// 权威存储（存档文件）。由 RimWorldSaveStore 在初始化时注册。
        /// 设为新值时自动重置 EventLog、AgentDriver 和 InteractionStore，避免跨存档引用失效。
        /// </summary>
        public static IPersistentStore SaveStore
        {
            get => _saveStore;
            internal set
            {
                if (_saveStore != value)
                {
                    // 存档切换：发布卸载事件 + 重置生命周期
                    if (_saveStore != null)
                    {
                        EventBus.Publish(FrameworkEvents.SaveUnloaded);
                    }

                    // 级联销毁旧组件
                    _directorAgent?.Dispose();
                    (_eventLog as IDisposable)?.Dispose();
                    (_interactionStore as IDisposable)?.Dispose();
                    _workspaces?.Dispose();
                    _llmAccessor?.Dispose();

                    _saveStore = value;
                    _eventLog = null;
                    _directorAgent = null;
                    _interactionStore = null;
                    _workspaces = null;
                    _knowledgeBase = null;

                    if (_saveStore != null)
                    {
                        EventBus.Publish(FrameworkEvents.SaveLoaded);
                        Logger?.Message("[RimLife.Core] SaveStore switched, components reset.");
                    }
                }
            }
        }

        /// <summary>缓存存储（本地文件）。</summary>
        public static IPersistentStore CacheStore { get; } = new LocalFileStore();

        private static IEventLog _eventLog;
        private static readonly object _eventLogLock = new object();

        /// <summary>
        /// 事件池实例（AgentEventPool）。
        /// 首次访问时从 SaveStore 延迟创建。存档未加载时返回 null。
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
                            var config = LoadDriverConfig();
                            _eventLog = new AgentEventPool(config);
                        }
                    }
                }
                return _eventLog;
            }
        }

        private static DriverConfig _driverConfig;
        private static readonly object _driverConfigLock = new object();

        /// <summary>
        /// Agent 驱动配置。从 CacheStore 加载，未配置时返回默认值。
        /// </summary>
        public static DriverConfig DriverConfig
        {
            get
            {
                lock (_driverConfigLock)
                {
                    if (_driverConfig == null)
                        _driverConfig = LoadDriverConfig();
                    return _driverConfig;
                }
            }
        }

        private static AgentLoop _directorAgent;
        private static readonly object _directorAgentLock = new object();

        /// <summary>
        /// 导演 AgentLoop 实例。首次访问时延迟创建，自动订阅 EventLog 的回调。
        /// 存档未加载或 LLM 未配置时返回 null。
        /// </summary>
        public static AgentLoop DirectorAgent
        {
            get
            {
                if (_directorAgent == null)
                {
                    lock (_directorAgentLock)
                    {
                        if (_directorAgent == null && SaveStore != null && LlmAccessor != null)
                        {
                            var pool = EventLog as AgentEventPool;
                            if (pool != null)
                            {
                                _directorAgent = new AgentLoop(
                                    pool: pool,
                                    llm: LlmAccessor,
                                    systemPrompt: BuildDirectorSystemPrompt(),
                                    skillIds: new[] { "workspace_direction" },
                                    maxRounds: DriverConfig.MaxAgentRounds,
                                    logger: Logger);
                            }
                        }
                    }
                }
                return _directorAgent;
            }
        }

        private static string BuildDirectorSystemPrompt()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是 RimWorld 殖民地的剧情导演 (Director Agent)。");
            sb.AppendLine();
            sb.AppendLine("你的职责：");
            sb.AppendLine("1. 审查以下积累的事件列表");
            sb.AppendLine("2. 挑选值得发展为剧情线的事件");
            sb.AppendLine("3. 为选中的事件创建或分支工作空间（workspace）");
            sb.AppendLine("4. 未被挑选的事件将被丢弃");
            sb.AppendLine();
            sb.AppendLine("决策原则：");
            sb.AppendLine("- 优先处理 Extreme 和 Major 严重度的事件");
            sb.AppendLine("- 相关事件可合并到同一个工作空间（如同一场袭击中的多个角色受伤）");
            sb.AppendLine("- 互不相关的事件应创建独立工作空间");
            sb.AppendLine("- 如无值得发展的内容，可以不创建任何工作空间");
            sb.AppendLine("- 可使用 get_workspace / list_workspaces 查看现有工作空间状态");
            sb.AppendLine("- 对已有工作空间可用 branch_workspace 创建分支、merge_workspaces 合并");
            return sb.ToString();
        }

        private static DriverConfig LoadDriverConfig()
        {
            try
            {
                var config = CacheStore?.FetchOrRebuild("rimlife_driver_config",
                    () => DriverConfig.CreateDefault());
                return config ?? DriverConfig.CreateDefault();
            }
            catch
            {
                return DriverConfig.CreateDefault();
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
                            var builtIn = new Knowledge.BuiltInKnowledgeBase(CacheStore, Logger);
                            var gameDef = new Knowledge.GameDefKnowledgeBase();
                            _knowledgeBase = new Framework.KnowledgeBaseChain(builtIn, gameDef);
                        }
                    }
                }
                return _knowledgeBase;
            }
        }

        private static LlmAccessor _llmAccessor;
        private static readonly object _llmAccessorLock = new object();

        /// <summary>
        /// LLM API 访问器实例。首次访问时从 CacheStore 延迟创建。
        /// </summary>
        public static LlmAccessor LlmAccessor
        {
            get
            {
                if (_llmAccessor == null)
                {
                    lock (_llmAccessorLock)
                    {
                        if (_llmAccessor == null && CacheStore != null)
                        {
                            _llmAccessor = new LlmAccessor(CacheStore);
                        }
                    }
                }
                return _llmAccessor;
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
                            _workspaces = new Workspace.WorkspaceManager(SaveStore, Logger,
                                () => TimeProvider?.Invoke() ?? "");
                        }
                    }
                }
                return _workspaces;
            }
        }
    }
}
