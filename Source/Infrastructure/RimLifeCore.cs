using RimLife.Agent;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Script;
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
        private static ScriptDeliveryService _scriptDeliveryService;

        /// <summary>日志接口。由适配层在启动时注入。</summary>
        public static ILogger Logger { get; internal set; }

        /// <summary>
        /// 人物卡内容提供者注册表（钩子模式）。
        /// 游戏侧注册各维度的 ICharacterContentProvider 实现，
        /// 框架在序列化 CharacterCard 时收集所有 provider 的产出。
        /// </summary>
        public static List<ICharacterContentProvider> ContentProviders { get; } = new List<ICharacterContentProvider>();

        /// <summary>
        /// 台词消费者。游戏侧实现 IScriptConsumer 后注入此处。
        /// ScriptDeliveryService 在收到 script.ready 事件后回调此接口。
        /// </summary>
        public static IScriptConsumer ScriptConsumer { get; set; }

        /// <summary>
        /// 台词占位符解析器。将 pawnId 映射为显示名。
        /// 默认使用 Infrastructure 层的 ScriptLineResolver 实现。
        /// </summary>
        public static IScriptLineResolver ScriptLineResolver { get; set; }

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

            // 清理 Freelancer Agent
            _freelancerAgent?.Dispose();
            _freelancerAgent = null;

            _frameworkConfig = null;
            _skillRegistryInitialized = false;

            // 清理台词推送服务
            _scriptDeliveryService?.Dispose();
            _scriptDeliveryService = null;

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

                // 先标记为初始化中，防止 RegisterHookProvider 递归调用
                _skillRegistryInitialized = true;

                McpSkillRegistry.InitializeDefaults();

                int count = McpSkillRegistry.RegisterFromType(typeof(Mcp.SystemMcpTools));
                count += McpSkillRegistry.RegisterFromType(typeof(Mcp.KnowledgeMcpTools));
                count += RegisterHookProvider(new Workspace.DirectionMcpProvider(() => Workspaces, Logger));
                count += RegisterHookProvider(new Workspace.WritingMcpProvider(() => Workspaces, Logger));
                count += RegisterHookProvider(new Workspace.FreelancerMcpProvider(() => Workspaces, Logger));

                // Hook Providers（游戏侧通过 IMcpHookProvider 实现）
                count += RegisterHookProvider(new Mcp.ColonyOverviewProvider());
                count += RegisterHookProvider(new Mcp.CharacterQueryProvider());
                count += RegisterHookProvider(new Mcp.RelationshipQueryProvider());
                count += RegisterHookProvider(new Mcp.EnvironmentQueryProvider());
                count += RegisterHookProvider(new Mcp.PawnMemoryProvider());

                Logger?.Message($"[RimLife.Core] SkillRegistry initialized: {McpSkillRegistry.SkillCount} skills, {count} tools registered.");

                // ---- 台词推送服务 ----
                if (_scriptDeliveryService == null)
                {
                    _scriptDeliveryService = new ScriptDeliveryService(
                        getWorkspaceManager: () => Workspaces,
                        getConsumer: () => ScriptConsumer,
                        resolver: ScriptLineResolver ?? new DefaultScriptLineResolver(),
                        logger: Logger);
                    Logger?.Message("[RimLife.Core] ScriptDeliveryService initialized.");
                }

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

                // ---- 运行时度量系统 ----
                if (Config.Features?.EnableRuntimeMetrics ?? true)
                {
                    // 注册度量查询 MCP 工具
                    McpSkillRegistry.RegisterFromType(typeof(Framework.Mcp.MetricsMcpTools));

                    // 为三种 Agent 角色分别注册 MetricsInterceptor
                    AgentPipeline.AddInterceptor(
                        new Framework.MetricsInterceptor(Framework.AgentRole.Director), priority: 100);
                    AgentPipeline.AddInterceptor(
                        new Framework.MetricsInterceptor(Framework.AgentRole.Screenwriter), priority: 100);
                    AgentPipeline.AddInterceptor(
                        new Framework.MetricsInterceptor(Framework.AgentRole.Freelancer), priority: 100);

                    // 订阅 LLM 响应事件，采集 Token 消耗
                    EventBus.Subscribe(FrameworkEvents.LlmResponseReceived, args =>
                    {
                        if (args?.Payload == null) return;
                        var sessionId = Framework.MetricsInterceptor.CurrentSessionId;
                        if (sessionId == null) return;

                        args.Payload.TryGetValue("inputTokens", out var itStr);
                        args.Payload.TryGetValue("outputTokens", out var otStr);
                        args.Payload.TryGetValue("cacheReadTokens", out var crStr);
                        args.Payload.TryGetValue("model", out var model);

                        int.TryParse(itStr, out int it);
                        int.TryParse(otStr, out int ot);
                        int.TryParse(crStr, out int cr);

                        RuntimeMetrics.RecordTokenUsage(sessionId, it, ot, cr, model ?? "");
                    });

                    // 订阅工作空间事件，采集操作计数
                    EventBus.Subscribe(FrameworkEvents.WorkspaceCreated, args =>
                        RuntimeMetrics.RecordWorkspaceOperation("created"));
                    EventBus.Subscribe(FrameworkEvents.WorkspaceClosed, args =>
                        RuntimeMetrics.RecordWorkspaceOperation("closed"));
                    EventBus.Subscribe(FrameworkEvents.WorkspaceUpdated, args =>
                        RuntimeMetrics.RecordWorkspaceOperation("updated"));

                    // 注册度量状态报告器
                    FrameworkStatus.RegisterReporter("RuntimeMetrics", () =>
                    {
                        var snap = RuntimeMetrics.GetSnapshot();
                        return new ComponentStatus
                        {
                            Name = "RuntimeMetrics",
                            IsAvailable = true,
                            Detail = $"sessions={snap.TotalSessions}, tokens_in={snap.Tokens?.TotalInput ?? 0}, " +
                                      $"tools={snap.Tools?.Count ?? 0} types, kb_batches={snap.Knowledge?.TotalBatches ?? 0}"
                        };
                    });

                    Logger?.Message("[RimLife.Core] RuntimeMetrics enabled.");
                }
                else
                {
                    Logger?.Message("[RimLife.Core] RuntimeMetrics disabled (FeatureToggle.EnableRuntimeMetrics=false).");
                }
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

        /// <summary>
        /// 将当前内存中的工作空间和交互历史数据刷入 IAuthorityStore。
        /// 应在 RimWorld 存档钩子（ExposeData Saving 分支）中调用。
        /// </summary>
        internal static void FlushToAuthorityStore()
        {
            if (_saveStore == null) return;

            try
            {
                (_workspaces as Workspace.WorkspaceManager)?.SaveToStore();
                (_interactionStore as InteractionHistoryStore)?.SaveToStore();
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] FlushToAuthorityStore failed: {e.Message}");
            }
        }

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
        private static AgentLoop _freelancerAgent;
        private static readonly object _freelancerAgentLock = new object();
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
        /// 获取 Freelancer 所在工作空间。
        /// 按 CreatedByRole == Freelancer &amp;&amp; Status == Active 查找，不存在时自动创建。
        /// </summary>
        public static Workspace.IWorkspace GetFreelancerWorkspace()
        {
            if (Workspaces == null) return null;

            var actives = Workspaces.GetActive();
            foreach (var ws in actives)
            {
                if (ws.CreatedByRole == Workspace.WorkspaceRole.Freelancer)
                    return ws;
            }

            return Workspaces.Create("Freelancer", null, null, Workspace.WorkspaceRole.Freelancer);
        }

        /// <summary>
        /// 获取 Freelancer AgentLoop 实例。绑定 Freelancer 工作空间的 EventPool。
        /// 存档未加载或 LLM 未配置时返回 null。
        /// </summary>
        public static AgentLoop GetFreelancerAgent()
        {
            if (_freelancerAgent == null)
            {
                lock (_freelancerAgentLock)
                {
                    if (_freelancerAgent == null && SaveStore != null && LlmAccessor != null)
                    {
                        var freelancerWs = GetFreelancerWorkspace();
                        if (freelancerWs != null)
                        {
                            _freelancerAgent = new AgentLoop(
                                pool: freelancerWs.EventPool,
                                llm: LlmAccessor,
                                systemPrompt: BuildFreelancerSystemPrompt(freelancerWs),
                                skillIds: new[] { "workspace_freelancer", "character_query", "event_query" },
                                maxRounds: DriverConfig.MaxAgentRounds,
                                logger: Logger,
                                serializer: CardSerializer.Default,
                                knowledgeBase: KnowledgeBase);
                        }
                    }
                }
            }
            return _freelancerAgent;
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
                                skillIds: new[] { "workspace_direction", "colony_overview", "character_query", "event_query", "knowledge_management" },
                                maxRounds: DriverConfig.MaxAgentRounds,
                                logger: Logger,
                                serializer: CardSerializer.Default,
                                contextProvider: () => BuildDirectorWorkspaceSummary(Workspaces),
                                knowledgeBase: KnowledgeBase);
                        }
                    }
                }
            }
            return _directorAgent;
        }

        /// <summary>
        /// 根据工作空间角色创建对应类型的 Agent。
        /// Director 工作空间 → Director Agent；Freelancer → Freelancer Agent；其他 → Screenwriter。
        /// </summary>
        private static void EnsureAgentForWorkspace(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            var ws = Workspaces?.Get(workspaceId);
            if (ws == null) return;

            if (ws.CreatedByRole == Workspace.WorkspaceRole.Director)
                GetDirectorAgent();
            else if (ws.CreatedByRole == Workspace.WorkspaceRole.Freelancer)
                GetFreelancerAgent();
            else
                GetScreenwriter(workspaceId);
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
                    serializer: CardSerializer.Default,
                    knowledgeBase: KnowledgeBase);

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
                if (!string.IsNullOrEmpty(ws.DirectorMessage))
                    sb.Append($" msg={TruncateForSummary(ws.DirectorMessage, 60)}");
            }
            return sb.ToString();
        }

        private static string TruncateForSummary(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
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
            sb.AppendLine("3. 使用 push_line 工具逐句撰写台词（可一次并行调用多个 push_line）");
            sb.AppendLine("4. 台词写完后调用 finish_round 结束本轮，填写 recap/outcome/directorNote");
            sb.AppendLine();
            sb.AppendLine("工作原则：");
            sb.AppendLine("- 优先使用 push_line 逐句输出台词以降低玩家等待延迟");
            sb.AppendLine("- 多句台词可在一个响应中并行调用多个 push_line，减少 API 往返");
            sb.AppendLine("- 台词写完后必须调用 finish_round 收尾");
            sb.AppendLine("- recap (前情提要) 总结本轮叙事起点，outcome 简述剧情发展结果");
            sb.AppendLine("- directorNote 给导演留言：剧情线是否可以继续、期望接收什么类型的事件等");
            sb.AppendLine("- 每次激活只推送 1 个轮次");
            sb.AppendLine("- 如事件不适合本剧情线，可用 route_events 推回导演工作空间");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(ScriptFormat.GetFormatSpec());
            return sb.ToString();
        }

        private static string BuildFreelancerSystemPrompt(Workspace.IWorkspace ws)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是 RimWorld 殖民地的临时任务代理 (Freelancer Agent)。");
            sb.AppendLine();
            sb.AppendLine($"工作空间 ID：{ws.Id}");
            sb.AppendLine($"工作空间：{ws.Label ?? "Freelancer"}");
            var directorWs = GetDirectorWorkspace();
            if (directorWs != null)
                sb.AppendLine($"导演工作空间 ID：{directorWs.Id}");
            sb.AppendLine();
            sb.AppendLine("你的职责：");
            sb.AppendLine("1. 处理突发性、独立性的事件（日常对话、随机遭遇、环境变化等）");
            sb.AppendLine("2. 这些事件不属于任何正在进行的剧情线，你不需要维护跨轮次的剧情上下文");
            sb.AppendLine("3. 调用角色查询、环境感知等工具获取当前状态");
            sb.AppendLine("4. 使用 push_line 工具逐句输出台词，写完后调用 finish_round 收尾");
            sb.AppendLine();
            sb.AppendLine("工作原则：");
            sb.AppendLine("- 每次激活都是独立任务，不维护剧情延续性");
            sb.AppendLine("- 叙事风格保持轻快、即兴，快速响应");
            sb.AppendLine("- 每次激活只处理当前批次事件，输出 1 个轮次");
            sb.AppendLine("- recap 只总结本次事件批次，不需要回顾历史");
            sb.AppendLine("- 多句台词可在一个响应中并行调用多个 push_line");
            sb.AppendLine("- 台词写完后必须调用 finish_round");
            sb.AppendLine("- 如事件更适合某条剧情线，用 route_events 推回导演工作空间");
            sb.AppendLine("- 你不负责汇报剧情线推进状态（那是编剧的职责）");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(ScriptFormat.GetFormatSpec());
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
                            var chain = new Framework.KnowledgeBaseChain(builtIn, gameDef);

                            // 连接知识库查询回调到运行时度量
                            chain.OnLookupResult = (term, hitLayer, isHit) =>
                            {
                                RuntimeMetrics.RecordKnowledgeLookup(
                                    term, hitLayer,
                                    Framework.MetricsInterceptor.CurrentSessionId);
                            };

                            _knowledgeBase = chain;
                        }
                    }
                }
                return _knowledgeBase;
            }
        }

        private static LlmAccessor _llmAccessor;
        private static readonly object _llmAccessorLock = new object();

        /// <summary>
        /// LLM API 访问器实例。首次访问时延迟创建（纯内存，不持久化密钥）。
        /// 配置由前端 LlmCredentialManager 在 Initialize 时注入。
        /// </summary>
        public static LlmAccessor LlmAccessor
        {
            get
            {
                if (_llmAccessor == null)
                {
                    lock (_llmAccessorLock)
                    {
                        if (_llmAccessor == null)
                        {
                            _llmAccessor = new LlmAccessor();
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
                                onWorkspaceReady: wsId => EnsureAgentForWorkspace(wsId));

                            // 为存档中已加载的活跃工作空间补齐对应 Agent
                            // （LoadFromStore 期间 _workspaces 尚未赋值，回调无法生效，此处补调）
                            var actives = _workspaces.GetActive();
                            foreach (var ws in actives)
                            {
                                EnsureAgentForWorkspace(ws.Id);
                            }
                        }
                    }
                }
                return _workspaces;
            }
        }
    }
}
