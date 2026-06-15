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

        /// <summary>
        /// 人物卡内容提供者注册表（钩子模式）。
        /// 游戏侧注册各维度的 ICharacterContentProvider 实现，
        /// 框架在序列化 CharacterCard 时收集所有 provider 的产出。
        /// </summary>
        public static List<ICharacterContentProvider> ContentProviders { get; } = new List<ICharacterContentProvider>();

        /// <summary>
        /// 注册一个人物卡内容提供者。
        /// 应在 EnsureSkillRegistryInitialized() 之前调用，确保工具注册前提供者已就绪。
        /// </summary>
        public static void RegisterContentProvider(ICharacterContentProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            ContentProviders.Add(provider);
        }

        /// <summary>时间字符串提供者。游戏侧注入，返回当前游戏时间的格式化字符串。
        /// 框架只原样透传，不解析语义。Agent 可通过 get_current_time 工具获取。</summary>
        public static Func<string> TimeProvider { get; internal set; }

        /// <summary>
        /// 统一适配器注入入口。
        /// 游戏侧在初始化时调用此方法一次性注入所有必需的适配器。
        /// </summary>
        /// <param name="logger">日志接口（必需）。</param>
        /// <param name="timeProvider">时间字符串提供者（可选）。</param>
        public static void InitializeAdapter(
            ILogger logger,
            Func<string> timeProvider = null)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            TimeProvider = timeProvider;
        }

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
            _driverConfig = config.Driver;
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

            // 清理编剧 Agent
            lock (_screenwritersLock)
            {
                foreach (var kv in _screenwriters)
                    kv.Value?.Dispose();
                _screenwriters.Clear();
            }

            // 清理导演 Agent
            _directorAgent?.Dispose();
            _directorAgent = null;

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

        private static IAuthorityStore _saveStore;

        /// <summary>
        /// 权威存储（存档文件）。由 RimWorldSaveStore 在初始化时注册。
        /// 设为新值时自动重置所有依赖存档的组件，避免跨存档引用失效。
        /// </summary>
        public static IAuthorityStore SaveStore
        {
            get => _saveStore;
            internal set
            {
                if (_saveStore != value)
                {
                    if (_saveStore != null)
                    {
                        EventBus.Publish(FrameworkEvents.SaveUnloaded);
                    }

                    // 级联销毁旧组件
                    (_workspaces as IDisposable)?.Dispose();
                    _llmAccessor?.Dispose();

                    // 清理所有编剧 Agent
                    lock (_screenwritersLock)
                    {
                        foreach (var kv in _screenwriters)
                            kv.Value?.Dispose();
                        _screenwriters.Clear();
                    }

                    _saveStore = value;
                    _workspaces = null;
                    _knowledgeBase = null;
                    _interactionStore = null;

                    if (_saveStore != null)
                    {
                        EventBus.Publish(FrameworkEvents.SaveLoaded);
                        Logger?.Message("[RimLife.Core] SaveStore switched, components reset.");
                    }
                }
            }
        }

        /// <summary>缓存存储（本地文件）。</summary>
        public static ICacheStore CacheStore { get; } = new LocalFileStore();

        private static DriverConfig _driverConfig;
        private static readonly object _driverConfigLock = new object();

        /// <summary>
        /// Agent 驱动配置。从 CacheStore 加载，未配置时返回默认值。
        /// </summary>
        internal static DriverConfig DriverConfig
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
        private static readonly Dictionary<string, AgentLoop> _screenwriters = new Dictionary<string, AgentLoop>();
        private static readonly object _screenwritersLock = new object();

        /// <summary>
        /// 获取导演所在工作空间。
        /// 按 CreatedByRole == Director &amp;&amp; Status == Active 查找，不存在时自动创建。
        /// </summary>
        public static Workspace.IWorkspace GetDirectorWorkspace()
        {
            if (Workspaces == null) return null;

            var actives = Workspaces.GetActive();
            foreach (var ws in actives)
            {
                if (ws.CreatedByRole == Workspace.WorkspaceRole.Director)
                    return ws;
            }

            return Workspaces.Create("Director", null, null, Workspace.WorkspaceRole.Director);
        }

        /// <summary>
        /// 获取导演 AgentLoop 实例。绑定导演工作空间的 EventPool。
        /// 存档未加载或 LLM 未配置时返回 null。
        /// </summary>
        public static AgentLoop GetDirectorAgent()
        {
            if (_directorAgent == null)
            {
                lock (_directorAgentLock)
                {
                    if (_directorAgent == null && SaveStore != null && LlmAccessor != null)
                    {
                        var directorWs = GetDirectorWorkspace();
                        if (directorWs != null)
                        {
                            _directorAgent = new AgentLoop(
                                pool: directorWs.EventPool,
                                llm: LlmAccessor,
                                systemPrompt: BuildDirectorSystemPrompt(),
                                skillIds: new[] { "workspace_direction" },
                                maxRounds: DriverConfig.MaxAgentRounds,
                                logger: Logger,
                                serializer: CardSerializer.Default,
                                contextProvider: () => BuildDirectorWorkspaceSummary(Workspaces));
                        }
                    }
                }
            }
            return _directorAgent;
        }

        /// <summary>
        /// 获取或创建指定工作空间的编剧 Agent。
        /// 由 WorkspaceManager 的 onWorkspaceReady 回调触发。
        /// </summary>
        public static AgentLoop GetScreenwriter(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return null;

            lock (_screenwritersLock)
            {
                if (_screenwriters.TryGetValue(workspaceId, out var existing) && existing != null)
                    return existing;

                if (Workspaces == null || LlmAccessor == null) return null;
                var ws = Workspaces.Get(workspaceId);
                if (ws == null) return null;

                var skillIds = new List<string> { "workspace_writing" };
                if (ws.SkillSlot?.ActiveSkillIds != null)
                    skillIds.AddRange(ws.SkillSlot.ActiveSkillIds);

                var agent = new AgentLoop(
                    pool: ws.EventPool,
                    llm: LlmAccessor,
                    systemPrompt: BuildScreenwriterSystemPrompt(ws),
                    skillIds: skillIds.ToArray(),
                    maxRounds: DriverConfig.MaxAgentRounds,
                    logger: Logger,
                    serializer: CardSerializer.Default);

                _screenwriters[workspaceId] = agent;
                Logger?.Message($"[RimLife.Core] ScreenwriterAgent created for workspace '{ws.Label}' ({workspaceId})");
                return agent;
            }
        }

        /// <summary>
        /// 释放指定工作空间的编剧 Agent。
        /// </summary>
        public static void DisposeScreenwriter(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            lock (_screenwritersLock)
            {
                if (_screenwriters.TryGetValue(workspaceId, out var agent))
                {
                    agent?.Dispose();
                    _screenwriters.Remove(workspaceId);
                    Logger?.Message($"[RimLife.Core] ScreenwriterAgent disposed for workspace {workspaceId}");
                }
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
            sb.AppendLine("4. 使用 route_events 将事件路由到对应工作空间");
            sb.AppendLine("5. 未被路由的事件将被丢弃");
            sb.AppendLine();
            sb.AppendLine("决策原则：");
            sb.AppendLine("- 优先处理 Extreme 和 Major 严重度的事件");
            sb.AppendLine("- 相关事件可合并到同一个工作空间（如同一场袭击中的多个角色受伤）");
            sb.AppendLine("- 互不相关的事件应创建独立工作空间");
            sb.AppendLine("- 如无值得发展的内容，可以不创建任何工作空间");
            sb.AppendLine("- 对已有工作空间可用 branch_workspace 创建分支、merge_workspaces 合并");
            sb.AppendLine();
            sb.AppendLine("事件路由：");
            sb.AppendLine("- 每条事件都有 eventId，使用 route_events 将事件推送到对应工作空间");
            sb.AppendLine("- 如无合适的工作空间，先 create_workspace 再用 route_events");
            return sb.ToString();
        }

        private static string BuildDirectorWorkspaceSummary(IWorkspaceManager manager)
        {
            if (manager == null) return "## 当前活跃工作空间\n（无）";

            var workspaces = manager.GetActive();
            if (workspaces == null || workspaces.Count == 0)
                return "## 当前活跃工作空间\n（无）";

            var sb = new System.Text.StringBuilder("## 当前活跃工作空间");
            foreach (var ws in workspaces)
            {
                sb.AppendLine();
                sb.Append($"- {ws.Label} (id={ws.Id})");
                if (ws.Tags != null && ws.Tags.Count > 0)
                    sb.Append($" tags=[{string.Join(",", ws.Tags)}]");
                sb.Append($" rounds={ws.Rounds?.Count ?? 0}");
                if (ws.LastSignal.HasValue)
                    sb.Append($" signal={ws.LastSignal.Value.Type}");
            }
            return sb.ToString();
        }

        private static string BuildScreenwriterSystemPrompt(Workspace.IWorkspace ws)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是 RimWorld 殖民地某条剧情线的编剧 (Screenwriter Agent)。");
            sb.AppendLine();
            sb.AppendLine($"工作空间 ID：{ws.Id}");
            sb.AppendLine($"工作空间：{ws.Label ?? "Unnamed"}");
            var directorWs = GetDirectorWorkspace();
            if (directorWs != null)
                sb.AppendLine($"导演工作空间 ID：{directorWs.Id}");
            if (ws.ColonistIds != null && ws.ColonistIds.Count > 0)
                sb.AppendLine($"关联角色：{string.Join(", ", ws.ColonistIds)}");
            sb.AppendLine();
            sb.AppendLine("你的职责：");
            sb.AppendLine("1. 审查推送到本工作空间的事件");
            sb.AppendLine("2. 根据需要调用角色查询、环境感知等工具获取上下文");
            sb.AppendLine("3. 使用 push_round 工具撰写叙事内容（recap + narrative）");
            sb.AppendLine("4. 在同一响应中调用 signal_workspace_status 上报推进状态");
            sb.AppendLine();
            sb.AppendLine("工作原则：");
            sb.AppendLine("- push_round 和 signal_workspace_status 应在同一次响应中调用");
            sb.AppendLine("- 每次激活只推送 1 个轮次");
            sb.AppendLine("- 前情提要 (recap) 总结当前叙事起点，台词 (narrative) 是正式的叙事输出");
            sb.AppendLine("- 剧情完结时 signal_workspace_status 上报 StorylineComplete");
            sb.AppendLine("- 遇到剧情瓶颈时上报 NeedsBranch 或 Stuck");
            sb.AppendLine("- 如事件不适合本剧情线，可用 route_events 推回导演工作空间");
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

        private static IWorkspaceManager _workspaces;
        private static readonly object _workspacesLock = new object();

        /// <summary>
        /// 工作空间管理器实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static IWorkspaceManager Workspaces
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
                                () => TimeProvider?.Invoke() ?? "", DriverConfig,
                                onWorkspaceReady: wsId => GetScreenwriter(wsId));
                        }
                    }
                }
                return _workspaces;
            }
        }
    }
}
