using NPCLife.Agent;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Script;
using NPCLife.Framework.Mcp;
using NPCLife.Infrastructure;
using NPCLife.Infrastructure.Knowledge;
using NPCLife.Infrastructure.Llm;
using NPCLife.Skills;
using NPCLife.Workspace;
using RimLife.Infrastructure.Knowledge;
using RimLife.Mappers;
using RimLife.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLife 核心服务定位器。提供持久化存储、事件日志、交互历史、
    /// 工作空间、知识库和 LLM API 访问的全局访问。
    /// </summary>
    /// <remarks>
    /// 部分实现拆分：
    /// <list type="bullet">
    /// <item><c>RimLifeCore.Timing.cs</c> — 定时器脉冲驱动与事件缓冲 flush</item>
    /// <item><c>RimLifeCore.Agents.cs</c> — Agent 创建、系统提示词构建与重建</item>
    /// <item><c>RimLifeCore.Config.cs</c> — DriverConfig 与 PromptAdditions 持久化</item>
    /// </list>
    /// </remarks>
    public static partial class RimLifeCore
    {
        private static bool _skillRegistryInitialized;
        private static readonly object _skillRegistryLock = new object();
        private static FrameworkConfig _frameworkConfig;
        private static ScriptDeliveryService _scriptDeliveryService;
        private static DialogueConsumer _dialogueConsumer;
        private static PromptAdditions _promptAdditions;
        private static readonly object _promptAdditionsLock = new object();
        private static EventBuffer _eventBuffer;

        /// <summary>日志接口。由适配层在启动时注入。</summary>
        public static ILogger Logger { get; internal set; }

        /// <summary>
        /// 人物卡内容提供者注册表（钩子模式）。
        /// 游戏侧注册各维度的 ICharacterContentProvider 实现，
        /// 框架在序列化 CharacterCard 时收集所有 provider 的产出。
        /// </summary>
        public static List<ICharacterContentProvider> ContentProviders { get; } = new List<ICharacterContentProvider>();

        /// <summary>
        /// 游戏侧事件缓冲。收集 Harmony 钩子产生的事件，
        /// 在无新事件的指定 tick 数后批量推送到框架侧 EventPool，实现事件合并去抖。
        /// </summary>
        public static EventBuffer EventBuffer
        {
            get
            {
                if (_eventBuffer == null)
                    _eventBuffer = new EventBuffer();
                return _eventBuffer;
            }
        }

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
        /// 框架只原样透传，不解析语义。时间信息随事件注入，Agent 无需主动查询。</summary>
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

        private static NPCLife.Core.IFrameworkFactory _frameworkFactory;

        /// <summary>
        /// 框架组件工厂。封装 NPCLife 具体类的创建细节（WorkspaceManager、BuiltInKnowledgeBase、MetricsInterceptor）。
        /// 首次访问时自动以当前 DriverConfig 初始化。
        /// </summary>
        public static NPCLife.Core.IFrameworkFactory FrameworkFactory
        {
            get
            {
                if (_frameworkFactory == null)
                {
                    var dc = DriverConfig;
                    Logger?.Message($"[RimLife.DIAG] FrameworkFactory: creating new DefaultFrameworkFactory. DirectorTimerInterval={dc?.DirectorTimerInterval ?? -1}, ImproviserTimerInterval={dc?.ImproviserTimerInterval ?? -1}");
                    _frameworkFactory = new NPCLife.Infrastructure.DefaultFrameworkFactory(DriverConfig);
                }
                return _frameworkFactory;
            }
        }

        /// <summary>当前框架配置。未配置时从 CacheStore 延迟加载，无持久化数据时返回默认配置。</summary>
        public static FrameworkConfig Config
        {
            get
            {
                if (_frameworkConfig == null)
                    _frameworkConfig = LoadFrameworkConfig();
                return _frameworkConfig;
            }
        }

        /// <summary>
        /// 游戏侧附加指令与 LLM 采样参数。从 CacheStore 延迟加载，UI 修改后通过 SetPromptAdditions 写回。
        /// 基础身份由 NPCLife 的 PromptConfig 静态成员持有，此处仅保存"附加"部分。
        /// </summary>
        public static PromptAdditions PromptAdditions
        {
            get
            {
                lock (_promptAdditionsLock)
                {
                    if (_promptAdditions == null)
                        _promptAdditions = LoadPromptAdditions();
                    return _promptAdditions;
                }
            }
        }

        /// <summary>
        /// 更新附加指令与 LLM 参数并持久化到 CacheStore。
        /// 修改后需调用 RebuildAgents() 才能生效。
        /// </summary>
        public static void SetPromptAdditions(PromptAdditions additions)
        {
            if (additions == null) throw new ArgumentNullException(nameof(additions));
            lock (_promptAdditionsLock)
            {
                _promptAdditions = additions;
                SavePromptAdditions(additions);
            }
        }

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
            // 关键：mutate 现有 DriverConfig 对象而非替换引用。
            // WorkspaceManager → WorkspaceEventPool 在创建时捕获了 DriverConfig 的引用，
            // 替换引用会导致 EventPool 使用过时阈值（见 CheckThreshold），
            // 而 TickTimerPulses 使用新阈值，引发阈值不匹配。
            ApplyDriverConfigInPlace(config.Driver);
            var d = config.Driver;
            Logger?.Message($"[RimLife.Core] Configure: timerInterval={d?.DirectorTimerInterval ?? 0}s, countThreshold={d?.DirectorCountThreshold ?? 0}, importanceThreshold={d?.DirectorImportanceThreshold ?? 0:F1}");
            _timerConfigLogged = false; // 配置变更后重新打印定时器摘要
            ErrorHandler.DiagnosticMode = config.Diagnostics?.EnableVerboseLogging ?? false;

            config.Freeze();
            SaveFrameworkConfig(config);
            LifecycleManager.NotifyConfigReady();
            FrameworkFactory.Events.Publish(FrameworkEvents.ConfigReady);

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
            FrameworkFactory.Status.Clear();
            ErrorHandler.ClearHandlers();

            // 清理所有 Agent（直接访问字段，避免触发属性 getter）
            if (_orchestrator != null)
            {
                _orchestrator.DisposeAll();
                _orchestrator = null;
            }

            _frameworkConfig = null;
            _promptAdditions = null;
            _eventBuffer?.Reset();
            _eventBuffer = null;
            _skillRegistryInitialized = false;
            SkillRegistry.Clear();

            // 清理知识服务
            _knowledgeService = null;

            // 清理 LLM 组件
            _llmAccessor?.Dispose();
            _llmAccessor = null;
            _credentialManager = null;

            // 清理台词推送服务
            _scriptDeliveryService?.Dispose();
            _scriptDeliveryService = null;

            // 清理台词消费者
            _dialogueConsumer?.Dispose();
            _dialogueConsumer = null;
            ScriptConsumer = null;

            Logger?.Message("[RimLife.Core] Shutdown complete.");
        }

        /// <summary>
        /// 初始化 MCP Skill 注册表。注册所有 Skill 元数据，
        /// 扫描工具类型自动建立 Skill → Tool 映射。
        /// 幂等，可多次调用。
        /// </summary>
        public static void EnsureSkillRegistryInitialized()
        {
            // [DIAG] 入口日志：记录调用栈和当前状态
            Logger?.Message($"[RimLife.DIAG] EnsureSkillRegistryInitialized called. _skillRegistryInitialized={_skillRegistryInitialized}, _frameworkFactory={_frameworkFactory != null}, SaveStore={SaveStore != null}");

            if (_skillRegistryInitialized)
            {
                Logger?.Message("[RimLife.DIAG] EnsureSkillRegistryInitialized: SKIPPED (already initialized).");
                return;
            }
            lock (_skillRegistryLock)
            {
                if (_skillRegistryInitialized)
                {
                    Logger?.Message("[RimLife.DIAG] EnsureSkillRegistryInitialized: SKIPPED after lock (already initialized).");
                    return;
                }

                // 提前置位以阻断重入：RegisterHookProvider 可能回调本方法。
                _skillRegistryInitialized = true;

                try
                {
                    Logger?.Message("[RimLife.Core] EnsureSkillRegistryInitialized: starting...");

                    var ff = FrameworkFactory;
                    Logger?.Message($"[RimLife.DIAG] FrameworkFactory obtained. Skills={ff.Skills != null}, SkillCount(before)={ff.Skills.SkillCount}");

                    ff.Skills.InitializeDefaults();
                    Logger?.Message($"[RimLife.Core] McpSkillRegistry.InitializeDefaults completed. SkillCount(after)={ff.Skills.SkillCount}");

                    int count = 0;
                    int sysCount = RegisterHookProvider(new SystemMcpProvider(() => Workspaces, () => TimeProvider?.Invoke() ?? "", Logger));
                    Logger?.Message($"[RimLife.DIAG] SystemMcpProvider registered: {sysCount} tools.");
                    count += sysCount;

                    int knCount = RegisterHookProvider(new KnowledgeMcpProvider(() => KnowledgeService, Logger));
                    Logger?.Message($"[RimLife.DIAG] KnowledgeMcpProvider registered: {knCount} tools.");
                    count += knCount;

                    int dirCount = RegisterHookProvider(new DirectionMcpProvider(() => Workspaces, Logger));
                    Logger?.Message($"[RimLife.DIAG] DirectionMcpProvider registered: {dirCount} tools.");
                    count += dirCount;

                    int wriCount = RegisterHookProvider(new WritingMcpProvider(() => Workspaces, Logger));
                    Logger?.Message($"[RimLife.DIAG] WritingMcpProvider registered: {wriCount} tools.");
                    count += wriCount;

                    // Hook Providers（游戏侧通过 IMcpHookProvider 实现）
                    int colCount = RegisterHookProvider(new RimLife.Infrastructure.Mcp.ColonyOverviewProvider());
                    Logger?.Message($"[RimLife.DIAG] ColonyOverviewProvider registered: {colCount} tools.");
                    count += colCount;

                    int charCount = RegisterHookProvider(new RimLife.Infrastructure.Mcp.CharacterQueryProvider());
                    Logger?.Message($"[RimLife.DIAG] CharacterQueryProvider registered: {charCount} tools.");
                    count += charCount;

                    int relCount = RegisterHookProvider(new RimLife.Infrastructure.Mcp.RelationshipQueryProvider());
                    Logger?.Message($"[RimLife.DIAG] RelationshipQueryProvider registered: {relCount} tools.");
                    count += relCount;

                    int envCount = RegisterHookProvider(new RimLife.Infrastructure.Mcp.EnvironmentQueryProvider());
                    Logger?.Message($"[RimLife.DIAG] EnvironmentQueryProvider registered: {envCount} tools.");
                    count += envCount;

                    int memCount = RegisterHookProvider(new RimLife.Infrastructure.Mcp.PawnMemoryProvider());
                    Logger?.Message($"[RimLife.DIAG] PawnMemoryProvider registered: {memCount} tools.");
                    count += memCount;

                    // ---- Skill 模块自动发现（Skills/ 目录） ----
                    // 扫描 Skills/ 目录，自动加载内置 + 第三方技能模块。
                    // 已通过硬编码路径注册的模块不会被重复注册（McpSkillRegistry 按工具名去重）。
                    try
                    {
                        string skillsDir = ResolveSkillsDirectory();
                        var loader = new SkillModuleLoader(Logger);
                        int moduleCount = loader.DiscoverAndLoad(skillsDir, ff.Skills);
                        Logger?.Message($"[RimLife.Core] SkillModuleLoader: {moduleCount} module(s) auto-discovered from '{skillsDir}'.");
                        count += moduleCount;
                    }
                    catch (Exception ex)
                    {
                        Logger?.Warning($"[RimLife.Core] SkillModuleLoader failed: {ex.Message}");
                    }

                    Logger?.Message($"[RimLife.Core] SkillRegistry initialized: {FrameworkFactory.Skills.SkillCount} skills, {count} tools registered. TotalToolCount={FrameworkFactory.Skills.TotalToolCount}");
                }
                catch (Exception ex)
                {
                    _skillRegistryInitialized = false; // 回退标记，允许后续重试
                    Logger?.Warning($"[RimLife.Core] SkillRegistry initialization failed: {ex.Message}");
                    Logger?.Warning($"[RimLife.Core] Stack: {ex.StackTrace}");
                    throw;
                }

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

                // ---- 台词消费者 + 调度器 ----
                if (_dialogueConsumer == null)
                {
                    _dialogueConsumer = new DialogueConsumer(
                        getWorkspaceManager: () => Workspaces,
                        logger: Logger);
                    ScriptConsumer = _dialogueConsumer;
                    Logger?.Message("[RimLife.Core] DialogueConsumer initialized.");
                }

                // 注入 Logger 到基础设施组件
                FrameworkFactory.Events.Logger = Logger;
                LifecycleManager.Logger = Logger;
                ErrorHandler.Logger = Logger;
                FrameworkFactory.Pipeline.Logger = Logger;

                // 初始化生命周期管理器
                LifecycleManager.Initialize();

                // 注册 FrameworkStatus 报告器
                RegisterStatusReporters();

                // 注册基础能力标识
                FrameworkFactory.Status.RegisterCapability("mcp_tools", true);
                FrameworkFactory.Status.RegisterCapability("event_bus", true);
                FrameworkFactory.Status.RegisterCapability("agent_pipeline", true);
                FrameworkFactory.Status.RegisterCapability("lifecycle_hooks", true);

                // ---- 知识上下文自动注入 ----
                // 扫描事件 Payload 中的 knowledge_tags，自动查询知识库并注入到 LLM 提示词。
                // 优先级 10（低于度量的 100），确保在度量采集前完成上下文构建。
                FrameworkFactory.Pipeline.AddInterceptor(
                    new NPCLife.Infrastructure.KnowledgeContextInterceptor(
                        () => KnowledgeService,
                        Logger),
                    priority: 10);

                // ---- 第二轮警告注入 ----
                // Agent 循环 Round >= 1 时，向 LLM 注入补救提示词，防止 LLM 在中间轮次"开新对话"。
                // 优先级 5（在知识注入之前），确保提示词尽早生效。
                FrameworkFactory.Pipeline.AddInterceptor(
                    new RoundTwoWarningInterceptor(),
                    priority: 1000);

                // ---- 运行时度量系统（永久启用） ----
                {
                    // MetricsMcpTools 已从 NPCLife 移除，度量数据通过 RuntimeMetrics 静态类 + MetricsInterceptor 采集

                    // 为三种 Agent 角色分别注册度量拦截器
                    FrameworkFactory.Pipeline.AddInterceptor(
                        FrameworkFactory.CreateMetricsInterceptor(NPCLife.Framework.AgentRole.Director), priority: 100);
                    FrameworkFactory.Pipeline.AddInterceptor(
                        FrameworkFactory.CreateMetricsInterceptor(NPCLife.Framework.AgentRole.Screenwriter), priority: 100);
                    FrameworkFactory.Pipeline.AddInterceptor(
                        FrameworkFactory.CreateMetricsInterceptor(NPCLife.Framework.AgentRole.Improviser), priority: 100);

                    // 订阅 LLM 响应事件，采集 Token 消耗
                    FrameworkFactory.Events.Subscribe(FrameworkEvents.LlmResponseReceived, args =>
                    {
                        if (args?.Payload == null) return;
                        var sessionId = NPCLife.Framework.MetricsInterceptor.CurrentSessionId;
                        if (sessionId == null) return;

                        args.Payload.TryGetValue("inputTokens", out var itStr);
                        args.Payload.TryGetValue("outputTokens", out var otStr);
                        args.Payload.TryGetValue("cacheReadTokens", out var crStr);
                        args.Payload.TryGetValue("model", out var model);

                        int.TryParse(itStr, out int it);
                        int.TryParse(otStr, out int ot);
                        int.TryParse(crStr, out int cr);

                        FrameworkFactory.Metrics.RecordTokenUsage(sessionId, it, ot, cr, model ?? "");
                    });

                    // 订阅工作空间事件，采集操作计数
                    FrameworkFactory.Events.Subscribe(FrameworkEvents.WorkspaceCreated, args =>
                        FrameworkFactory.Metrics.RecordWorkspaceOperation("created"));
                    FrameworkFactory.Events.Subscribe(FrameworkEvents.WorkspaceClosed, args =>
                        FrameworkFactory.Metrics.RecordWorkspaceOperation("closed"));
                    FrameworkFactory.Events.Subscribe(FrameworkEvents.WorkspaceUpdated, args =>
                        FrameworkFactory.Metrics.RecordWorkspaceOperation("updated"));

                    // 注册度量状态报告器
                    FrameworkFactory.Status.RegisterReporter("RuntimeMetrics", () =>
                    {
                        var snap = FrameworkFactory.Metrics.GetSnapshot();
                        return new ComponentStatus
                        {
                            Name = "RuntimeMetrics",
                            IsAvailable = true,
                            Detail = $"sessions={snap.TotalSessions}, tokens_in={snap.Tokens?.TotalInput ?? 0}, " +
                                      $"tools={snap.Tools?.Count ?? 0} types, kb_batches={snap.Knowledge?.TotalBatches ?? 0}"
                        };
                    });

                    Logger?.Message("[RimLife.Core] RuntimeMetrics enabled.");

                    // ---- 会话追踪（全文记录，供 Dashboard 展示） ----
                    // 单实例即可，内部用 ThreadStatic 拥有者模式避免 3x 重复采集。
                    FrameworkFactory.Pipeline.AddInterceptor(
                        new SessionTraceInterceptor(), priority: 200);
                    Logger?.Message("[RimLife.Core] SessionTrace enabled.");
                }
            }
        }

        private static void RegisterStatusReporters()
        {
            FrameworkFactory.Status.RegisterReporter("Llm", () => new ComponentStatus
            {
                Name = "Llm",
                IsAvailable = _credentialManager != null && _credentialManager.HasCredentials,
                Detail = _credentialManager != null ? $"{_credentialManager.GetActivationOrder().Count} active" : "not initialized"
            });

            FrameworkFactory.Status.RegisterReporter("Workspace", () => new ComponentStatus
            {
                Name = "Workspace",
                IsAvailable = _workspaces != null,
                Detail = _workspaces != null ? "active" : "not initialized"
            });

            FrameworkFactory.Status.RegisterReporter("KnowledgeBase", () => new ComponentStatus
            {
                Name = "KnowledgeBase",
                IsAvailable = _knowledgeService != null,
                Detail = _knowledgeService != null ? $"{_knowledgeService.ListAll().Count} entries" : "not initialized"
            });
        }

        // ----------------------------------------------------------------
        // Skill 模块自动发现辅助
        // ----------------------------------------------------------------

        /// <summary>
        /// 解析 Skills/ 目录的绝对路径。
        /// 从 RimLife.dll 所在位置向上两级找到 Mod 根目录，再拼接 Skills/。
        /// </summary>
        private static string ResolveSkillsDirectory()
        {
            try
            {
                // RimLife.dll 位于 {ModRoot}/1.6/Assemblies/
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(asmDir))
                    return Path.Combine(".", "Skills");

                // 向上一级到 1.6/，再上一级到 Mod 根目录
                var modRoot = Path.GetFullPath(Path.Combine(asmDir, "..", ".."));
                var skillsDir = Path.Combine(modRoot, "Skills");
                return skillsDir;
            }
            catch
            {
                return Path.Combine(".", "Skills");
            }
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
            if (provider == null)
            {
                Logger?.Message("[RimLife.DIAG] RegisterHookProvider called with null provider.");
                return 0;
            }

            try
            {
                Logger?.Message($"[RimLife.DIAG] RegisterHookProvider: entering '{provider.HookId}', _frameworkFactory={_frameworkFactory != null}");
                EnsureSkillRegistryInitialized();
                var ff = FrameworkFactory;
                int count = ff.Skills.RegisterFromProvider(provider);
                Logger?.Message($"[RimLife.Core] HookProvider '{provider.HookId}' registered: {count} tools. TotalToolCount now={ff.Skills.TotalToolCount}");
                return count;
            }
            catch (System.Exception e)
            {
                Logger?.Warning($"[RimLife.Core] RegisterHookProvider({provider.HookId}) failed: {e.Message}");
                Logger?.Warning($"[RimLife.Core] Stack: {e.StackTrace}");
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
                        FrameworkFactory.Events.Publish(FrameworkEvents.SaveUnloaded);
                    }

                    // 级联销毁旧组件
                    (_workspaces as IDisposable)?.Dispose();
                    _llmAccessor?.Dispose();

                    // 清理所有 Agent（直接访问字段，避免触发 Orchestrator 属性 getter 导致懒初始化）
                    if (_orchestrator != null)
                    {
                        _orchestrator.DisposeAll();
                        _orchestrator = null;
                    }

                    _saveStore = value;
                    _workspaces = null;
                    _knowledgeService = null;
                    _interactionStore = null;

                    // 重置缓存存储与依赖缓存的配置，确保绑定到新存档
                    _cacheStore = null;         // 重建 CacheStore，绑定新存档文件
                    _driverConfig = null;       // 重新从新 CacheStore 加载
                    _frameworkConfig = null;    // 重新从新 CacheStore 加载
                    _promptAdditions = null;    // 重新从新 CacheStore 加载
                    _frameworkFactory = null;   // 重新以新 DriverConfig 创建

                    // [DIAG] 关键观察：_skillRegistryInitialized 未被重置！
                    // 如果之前注册失败或只部分成功，后续调用 EnsureSkillRegistryInitialized 会被短路
                    Logger?.Message($"[RimLife.DIAG] SaveStore reset: _frameworkFactory=null, _skillRegistryInitialized={_skillRegistryInitialized} (NOT reset!)");

                    if (_saveStore != null)
                    {
                        FrameworkFactory.Events.Publish(FrameworkEvents.SaveLoaded);
                        Logger?.Message("[RimLife.Core] SaveStore switched, components reset.");
                    }
                }
            }
        }

        private static ICacheStore _cacheStore;

        /// <summary>
        /// 缓存存储（本地文件）。随存档切换自动重建，
        /// 绑定到当前存档的 GUID 对应的缓存文件。
        /// </summary>
        public static ICacheStore CacheStore
        {
            get
            {
                if (_cacheStore == null)
                    _cacheStore = new LocalFileStore();
                return _cacheStore;
            }
        }

        /// <summary>
        /// 将当前内存中的工作空间和交互历史数据刷入 IAuthorityStore。
        /// 应在 RimWorld 存档钩子（ExposeData Saving 分支）中调用。
        /// </summary>
        internal static void FlushToAuthorityStore()
        {
            if (_saveStore == null) return;

            try
            {
                // 强制排空事件缓冲，避免未推送事件在存档时丢失
                FlushEventBuffer(Find.TickManager?.TicksGame ?? 0, force: true);
                _workspaces?.Persist();
                _interactionStore?.Persist();
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] FlushToAuthorityStore failed: {e.Message}");
            }
        }

        // ================================================================
        // 延迟初始化的服务属性
        // ================================================================

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

        private static IKnowledgeService _knowledgeService;
        private static readonly object _knowledgeServiceLock = new object();

        /// <summary>
        /// 知识服务实例。首次访问时从 CacheStore 延迟创建 BuiltInKnowledgeBase，
        /// 并与 GameDefKnowledgeBase 外部源组装为 KnowledgeService，外层包裹指标装饰器。
        /// CacheStore 不可用时返回 null。
        /// </summary>
        public static IKnowledgeService KnowledgeService
        {
            get
            {
                if (_knowledgeService == null)
                {
                    lock (_knowledgeServiceLock)
                    {
                        if (_knowledgeService == null && CacheStore != null)
                        {
                            var builtIn = FrameworkFactory.CreateKnowledgeBase(CacheStore, Logger);
                            var gameDef = new Knowledge.GameDefKnowledgeBase();
                            var externals = new List<IExternalKnowledgeSource> { gameDef };
                            var knowledge = new KnowledgeService(builtIn, externals);
                            _knowledgeService = new MetricsKnowledgeService(knowledge, FrameworkFactory.Metrics);
                        }
                    }
                }
                return _knowledgeService;
            }
        }

        private static LlmAccessor _llmAccessor;
        private static readonly object _llmAccessorLock = new object();

        /// <summary>
        /// LLM API 访问器实例（无状态，纯函数）。
        /// 首次访问时延迟创建。凭证由 CredentialRegistry 管理。
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

        private static CredentialRegistry _credentialManager;
        private static readonly object _credentialManagerLock = new object();

        /// <summary>
        /// 凭证管理器实例。管理凭证名 → 凭证的映射。
        /// 首次访问时延迟创建，从 RimLifeModSettings 加载持久化状态。
        /// </summary>
        public static ICredentialManager CredentialManager
        {
            get
            {
                if (_credentialManager == null)
                {
                    lock (_credentialManagerLock)
                    {
                        if (_credentialManager == null)
                        {
                            string initialJson = null;
                            try
                            {
                                var settings = Settings.RimLifeModSettings.Instance;
                                initialJson = settings?.LlmCredentialsJson;
                            }
                            catch { }

                            _credentialManager = new CredentialRegistry(
                                persistAction: json =>
                                {
                                    try
                                    {
                                        var settings = Settings.RimLifeModSettings.Instance;
                                        if (settings != null)
                                        {
                                            settings.LlmCredentialsJson = json;
                                            settings.SaveNow();
                                        }
                                    }
                                    catch { }
                                },
                                initialJson: initialJson);
                        }
                    }
                }
                return _credentialManager;
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
                            Logger?.Message($"[RimLife.DIAG] Workspaces: creating. _skillRegistryInitialized={_skillRegistryInitialized}, _frameworkFactory={_frameworkFactory != null}");
                            _workspaces = FrameworkFactory.CreateWorkspaceManager(SaveStore, Logger,
                                () => TimeProvider?.Invoke() ?? "",
                                onWorkspaceReady: wsId =>
                                {
                                    // 直接访问 _orchestrator 字段避免触发 Orchestrator 属性 getter，
                                    // 后者会在 WorkspaceManager 构造期间造成循环依赖。
                                    var orch = _orchestrator;
                                    if (orch != null)
                                        orch.OnWorkspaceReady(wsId);
                                    // 动态创建的工作空间（如编剧）需要同步模型配置
                                    SyncModelConfigToWorkspaces();
                                });

                            // 为存档中已加载的活跃工作空间补齐对应 Agent
                            // （LoadFromStore 期间 _orchestrator 尚未初始化，回调无法生效，此处补调）
                            InitializeAgentOrchestrator();
                            var actives = _workspaces.GetActive();
                            Logger?.Message($"[RimLife.DIAG] Workspaces: GetActive returned {actives?.Count ?? 0} workspaces.");
                            foreach (var ws in actives)
                            {
                                _orchestrator.OnWorkspaceReady(ws.Id);
                            }

                            // 将全局模型配置同步到工作空间
                            SyncModelConfigToWorkspaces();
                        }
                    }
                }
                return _workspaces;
            }
        }
    }
}
