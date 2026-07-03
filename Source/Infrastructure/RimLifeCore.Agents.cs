using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NPCLife.Agent;
using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.Script;
using NPCLife.Workspace;
using RimLife.Data;
using RimLife.Infrastructure.Mcp;
using RimLife.Mappers;
using RimWorld;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLifeCore 的 Agent 管理部分。
    /// 包含 Agent 创建、销毁、系统提示词构建和重建逻辑。
    /// </summary>
    public static partial class RimLifeCore
    {
        private static IAgentOrchestrator _orchestrator;
        private static readonly object _orchestratorLock = new object();

        /// <summary>当前上下文中收集的推荐角色 ID，供 preQueriedProvider 读取。</summary>
        private static HashSet<string> _currentPawnIds;

        /// <summary>
        /// Agent 编排器。框架负责 Agent 生命周期，游戏侧通过 InitializeAgentOrchestrator
        /// 注册 AgentFactory 委托（注入系统提示词、动态上下文等游戏特定内容）。
        /// </summary>
        public static IAgentOrchestrator Orchestrator
        {
            get
            {
                if (_orchestrator == null)
                {
                    lock (_orchestratorLock)
                    {
                        if (_orchestrator == null)
                            InitializeAgentOrchestrator();
                    }
                }
                return _orchestrator;
            }
        }

        /// <summary>
        /// 初始化 Agent 编排器。注册三种角色的 AgentFactory 委托。
        /// 必须在 Workspaces 和 LlmAccessor 就绪后调用。
        /// </summary>
        internal static void InitializeAgentOrchestrator()
        {
            if (_orchestrator != null) return;

            var orchestrator = FrameworkFactory.CreateAgentOrchestrator(Workspaces);

            // 导演 Agent 工厂
            orchestrator.Register(WorkspaceRole.Director, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildDirectorSystemPrompt(),
                    contextProvider: () => BuildDirectorWorkspaceSummary(mgr),
                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));
            });

            // 即兴编剧 Agent 工厂
            orchestrator.Register(WorkspaceRole.Improviser, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildImproviserSystemPrompt(ws),
                    contextProvider: () => BuildImproviserContext(ws),
                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));
            });

            // 编剧 Agent 工厂
            orchestrator.Register(WorkspaceRole.Screenwriter, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildScreenwriterSystemPrompt(ws),
                    contextProvider: () => BuildScreenwriterContext(ws),
                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));
            });

            _orchestrator = orchestrator;

            // 初始化即兴剧情线的标题和简介
            var improWs = orchestrator.GetOrCreateWorkspace(WorkspaceRole.Improviser);
            if (improWs != null && Workspaces != null)
            {
                Workspaces.SetLabel(improWs.Id, "即兴剧情");
                Workspaces.SetDirectorMessage(improWs.Id, "无长期记忆的常驻剧情线，可根据任意一组事件创作剧情。无论事件数量每个推送轮次都会触发");
            }

            Logger?.Message("[RimLife.Core] AgentOrchestrator initialized with 3 role factories.");
        }

        /// <summary>
        /// 获取导演工作空间（兼容旧 API，委托给 Orchestrator）。
        /// </summary>
        public static NPCLife.Workspace.IWorkspace GetDirectorWorkspace()
        {
            return Orchestrator?.GetOrCreateWorkspace(WorkspaceRole.Director);
        }

        /// <summary>
        /// 获取即兴编剧工作空间（兼容旧 API，委托给 Orchestrator）。
        /// </summary>
        public static NPCLife.Workspace.IWorkspace GetImproviserWorkspace()
        {
            return Orchestrator?.GetOrCreateWorkspace(WorkspaceRole.Improviser);
        }

        /// <summary>
        /// 构建 AgentLoop 共享基础设施依赖。
        /// 所有角色共用同一组 LLM 服务、凭证存储、日志、序列化器、行为配置。
        /// </summary>
        private static AgentLoopDependencies BuildAgentDeps()
        {
            return new AgentLoopDependencies
            {
                Llm = LlmAccessor,
                CredentialStore = CredentialManager,
                Logger = Logger,
                Serializer = CardSerializer.Default,
                MaxRounds = DriverConfig.MaxAgentRounds,
                Temperature = PromptAdditions.Temperature
            };
        }

        // ================================================================
        // 系统提示词构建
        // ================================================================

        private static string BuildDirectorSystemPrompt()
        {
            var pa = PromptAdditions;
            var sb = new System.Text.StringBuilder(NPCLife.Driver.PromptConfig.DefaultDirectorPrompt);
            AppendAdditions(sb, pa.DirectorAdditions, "RimWorld 导演附加指令");
            AppendStyleInstruction(sb, pa);
            return sb.ToString();
        }

        private static string BuildDirectorWorkspaceSummary(IWorkspaceManager manager)
        {
            var sb = new StringBuilder();

            // 当前时间
            try
            {
                var timeStr = TimeProvider?.Invoke();
                if (!string.IsNullOrEmpty(timeStr))
                {
                    sb.AppendLine("## 当前时间");
                    sb.AppendLine(timeStr);
                    sb.AppendLine();
                }
            }
            catch { }

            // 当前全局状态
            try
            {
                var state = GlobalStateMapper.Create();
                if (state != null)
                {
                    sb.AppendLine("## 当前全局状态");
                    sb.AppendLine(GlobalStateMapper.Serialize(state));
                    sb.AppendLine();
                }
            }
            catch { }

            // 推荐角色：随机选取 1-3 个可见角色作为叙事引子
            // 不依赖事件关联，让编剧自行从推荐角色出发探索周围角色构建场景
            try
            {
                var rng = new System.Random();
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p =>
                        p != null && !p.Dead && (p.RaceProps.Humanlike || p.RaceProps.Animal)));
                }
                // 随机洗牌取 1-3 个
                var selected = allPawns.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();
                var pawnIds = new HashSet<string>(selected.Select(p => p.ThingID));

                var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                if (!string.IsNullOrEmpty(summary) && summary != "[]")
                {
                    sb.AppendLine("## 推荐本轮叙事主角");
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }

                _currentPawnIds = pawnIds;
                AppendRelationshipSummary(sb, pawnIds);
            }
            catch { }

            // 活跃目标
            try
            {
                var objectives = ObjectiveCardMapper.GetActive();
                if (objectives != null && objectives.Count > 0)
                {
                    sb.AppendLine("## 活跃目标");
                    sb.AppendLine(CardSerializer.Default.SerializeObjectiveList(objectives));
                    sb.AppendLine();
                }
            }
            catch { }

            // 知识库已有词条（仅列词条名，避免重复创建）
            try
            {
                var knSvc = KnowledgeService;
                if (knSvc != null)
                {
                    var all = knSvc.ListAll();
                    if (all != null && all.Count > 0)
                    {
                        sb.AppendLine("## 知识库已有词条");
                        int limit = 50;
                        var shown = all.Take(limit);
                        foreach (var entry in shown)
                        {
                            sb.Append("- ");
                            sb.Append(entry.Term ?? "?");
                            if (!string.IsNullOrEmpty(entry.Source))
                            {
                                sb.Append(" (`");
                                sb.Append(entry.Source);
                                sb.Append("`)");
                            }
                            sb.AppendLine();
                        }
                        if (all.Count > limit)
                            sb.AppendLine($"  （共 {all.Count} 条，仅显示前 {limit} 条）");
                        sb.AppendLine();
                    }
                }
            }
            catch { }

            // 当前活跃剧情线（通过框架 GetStorylines 获取，角色可见性由框架封装）
            if (manager == null)
            {
                sb.AppendLine("## 当前活跃剧情线\n（无）");
            }
            else
            {
                try
                {
                    var storylines = manager.GetStorylines(WorkspaceStatus.Active);

                    if (storylines.Count == 0)
                    {
                        sb.AppendLine("## 当前活跃剧情线\n（无）");
                    }
                    else
                    {
                        var items = new List<string>();
                        foreach (var ws in storylines)
                        {
                            var w = new JsonWriter(192);
                            w.Prop("id", ws.Id ?? "");
                            w.Prop("label", ws.Label ?? "");
                            w.Prop("status", ws.Status.ToString());
                            w.Prop("createdByRole", ws.CreatedByRole.ToString());
                            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
                            w.Prop("createdAt", ws.CreatedAt ?? "");
                            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
                            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                                w.Prop("directorMessage", ws.DirectorMessage.Length > 120 ? ws.DirectorMessage.Substring(0, 120) + "..." : ws.DirectorMessage);
                            items.Add(w.Close());
                        }
                        sb.AppendLine("## 当前活跃剧情线");
                        sb.AppendLine("[" + string.Join(",", items) + "]");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"## 当前活跃剧情线\n（获取失败：{ex.Message}）");
                }
            }

            return sb.ToString();
        }

        private static string TruncateForSummary(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }

        /// <summary>
        /// 构建编剧激活时的动态上下文：殖民地快照、当前时间、聚焦角色摘要。
        /// 每轮激活时注入，替代 get_colony_overview 等工具查询。
        /// </summary>
        private static string BuildScreenwriterContext(NPCLife.Workspace.IWorkspace ws)
        {
            var sb = new StringBuilder();

            // 当前时间
            try
            {
                var timeStr = TimeProvider?.Invoke();
                if (!string.IsNullOrEmpty(timeStr))
                {
                    sb.AppendLine($"当前时间：{timeStr}");
                    sb.AppendLine();
                }
            }
            catch { }

            // 当前全局状态
            try
            {
                var state = GlobalStateMapper.Create();
                if (state != null)
                {
                    sb.AppendLine("## 当前全局状态");
                    sb.AppendLine(GlobalStateMapper.Serialize(state));
                    sb.AppendLine();
                }
            }
            catch { }

            // 推荐角色：随机选取 1-3 个可见角色作为叙事引子
            // 不依赖事件关联，让编剧自行从推荐角色出发探索周围角色构建场景
            try
            {
                var rng = new System.Random();
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p =>
                        p != null && !p.Dead && (p.RaceProps.Humanlike || p.RaceProps.Animal)));
                }
                // 随机洗牌取 1-3 个
                var selected = allPawns.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();
                var pawnIds = new HashSet<string>(selected.Select(p => p.ThingID));

                var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                if (!string.IsNullOrEmpty(summary) && summary != "[]")
                {
                    sb.AppendLine("## 推荐本轮叙事主角");
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }

                _currentPawnIds = pawnIds;
                AppendRelationshipSummary(sb, pawnIds);
            }
            catch { }

            // 聚焦角色摘要（导演指定的关联角色，static view）
            if (ws.FocusCharacterIds != null && ws.FocusCharacterIds.Count > 0)
            {
                try
                {
                    var cards = new List<string>();
                    foreach (var id in ws.FocusCharacterIds)
                    {
                        var pawn = PawnQueryHelper.FindPawnById(id);
                        if (pawn != null)
                        {
                            var card = PawnQueryHelper.BuildCharacterCard(pawn, "static");
                            var json = CardSerializer.Default.SerializeCharacterCard(card, "static", ContentProviders);
                            cards.Add(json);
                        }
                    }
                    if (cards.Count > 0)
                    {
                        sb.AppendLine("## 导演指定聚焦角色");
                        sb.AppendLine(PawnQueryHelper.SerializeJsonArray(cards));
                        sb.AppendLine();
                    }
                }
                catch { }
            }

            return sb.ToString();
        }

        private static string BuildScreenwriterSystemPrompt(NPCLife.Workspace.IWorkspace ws)
        {
            var pa = PromptAdditions;
            var sb = new System.Text.StringBuilder(NPCLife.Driver.PromptConfig.DefaultScreenwriterPrompt);
            AppendAdditions(sb, pa.ScreenwriterAdditions, "RimWorld 编剧附加指令");

            // 编剧侧不再注入工作空间信息。事件统一通过 route_events 工具发送，
            // 无需知晓导演工作空间 ID 或自身工作空间 ID。
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            AppendStyleInstruction(sb, pa);
            return sb.ToString();
        }

        private static string BuildImproviserSystemPrompt(NPCLife.Workspace.IWorkspace ws)
        {
            var pa = PromptAdditions;
            var sb = new System.Text.StringBuilder(NPCLife.Driver.PromptConfig.DefaultImproviserPrompt);
            AppendAdditions(sb, pa.ImproviserAdditions, "RimWorld 即兴编剧附加指令");

            // 即兴编剧侧同样不注入工作空间信息。
            // 事件统一通过 route_events 工具发送。
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            AppendStyleInstruction(sb, pa);
            return sb.ToString();
        }

        /// <summary>
        /// 构建即兴编剧激活时的动态上下文：当前时间、殖民地快照、聚焦角色摘要。
        /// 与 Screenwriter 完全相同（即兴编剧只是不保存长期上下文的编剧，能力无区别）。
        /// </summary>
        private static string BuildImproviserContext(NPCLife.Workspace.IWorkspace ws)
        {
            var sb = new StringBuilder();

            // 当前时间
            try
            {
                var timeStr = TimeProvider?.Invoke();
                if (!string.IsNullOrEmpty(timeStr))
                {
                    sb.AppendLine($"当前时间：{timeStr}");
                    sb.AppendLine();
                }
            }
            catch { }

            // 当前全局状态
            try
            {
                var state = GlobalStateMapper.Create();
                if (state != null)
                {
                    sb.AppendLine("## 当前全局状态");
                    sb.AppendLine(GlobalStateMapper.Serialize(state));
                    sb.AppendLine();
                }
            }
            catch { }

            // 推荐角色：随机选取 1-3 个可见角色作为叙事引子
            // 不依赖事件关联，让编剧自行从推荐角色出发探索周围角色构建场景
            try
            {
                var rng = new System.Random();
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p =>
                        p != null && !p.Dead && (p.RaceProps.Humanlike || p.RaceProps.Animal)));
                }
                // 随机洗牌取 1-3 个
                var selected = allPawns.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();
                var pawnIds = new HashSet<string>(selected.Select(p => p.ThingID));

                var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                if (!string.IsNullOrEmpty(summary) && summary != "[]")
                {
                    sb.AppendLine("## 推荐本轮叙事主角");
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }

                _currentPawnIds = pawnIds;
                AppendRelationshipSummary(sb, pawnIds);
            }
            catch { }

            // 聚焦角色摘要（导演指定的关联角色，static view）
            if (ws.FocusCharacterIds != null && ws.FocusCharacterIds.Count > 0)
            {
                try
                {
                    var cards = new List<string>();
                    foreach (var id in ws.FocusCharacterIds)
                    {
                        var pawn = PawnQueryHelper.FindPawnById(id);
                        if (pawn != null)
                        {
                            var card = PawnQueryHelper.BuildCharacterCard(pawn, "static");
                            var json = CardSerializer.Default.SerializeCharacterCard(card, "static", ContentProviders);
                            cards.Add(json);
                        }
                    }
                    if (cards.Count > 0)
                    {
                        sb.AppendLine("## 导演指定聚焦角色");
                        sb.AppendLine(PawnQueryHelper.SerializeJsonArray(cards));
                        sb.AppendLine();
                    }
                }
                catch { }
            }

            return sb.ToString();
        }

        /// <summary>将角色附加指令追加到系统提示词中（仅在非空时生效）。</summary>
        private static void AppendAdditions(System.Text.StringBuilder sb, string additions, string sectionTitle)
        {
            if (!string.IsNullOrEmpty(additions))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine($"## {sectionTitle}");
                sb.AppendLine(additions);
            }
        }

        /// <summary>将全局风格指令追加到提示词末尾（共用辅助）。</summary>
        private static void AppendStyleInstruction(System.Text.StringBuilder sb, PromptAdditions pa)
        {
            if (!string.IsNullOrEmpty(pa?.StyleInstruction))
            {
                sb.AppendLine();
                sb.AppendLine("## 叙事风格");
                sb.AppendLine(pa.StyleInstruction);
            }
        }

        // ================================================================
        // 关系/交互摘要：一行文本替代 get_relationships + get_interaction_history
        // ================================================================

        /// <summary>
        /// 为推荐角色追加一行紧凑的交互摘要。
        /// 替代 LLM 手动调用 get_relationships + get_interaction_history，消除 1 轮工具调用开销。
        /// </summary>
        private static void AppendRelationshipSummary(StringBuilder sb, HashSet<string> pawnIds)
        {
            if (pawnIds == null || pawnIds.Count == 0) return;

            var parts = new List<string>();
            foreach (var id in pawnIds)
            {
                try
                {
                    var pawn = PawnQueryHelper.FindPawnById(id);
                    if (pawn == null) continue;
                    string name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?";
                    string opinion = GetColonyOpinion(pawn);
                    int histCount = GetInteractionCount(pawn);
                    string hist = histCount > 0 ? $"{histCount}条交互" : "无";
                    parts.Add($"{name}({opinion},{hist})");
                }
                catch { }
            }

            if (parts.Count > 0)
            {
                sb.Append("角色关系/交互: ");
                sb.AppendLine(string.Join("; ", parts));
                sb.AppendLine();
            }
        }

        private static string GetColonyOpinion(Pawn p)
        {
            try
            {
                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists == null || colonists.Count == 0) return "?";
                float sum = 0f; int cnt = 0;
                foreach (var c in colonists)
                {
                    if (c == p || c?.relations == null) continue;
                    try { sum += c.relations.OpinionOf(p); cnt++; } catch { }
                }
                if (cnt == 0) return "?";
                return SemanticLabels.MapOpinionTier(sum / cnt);
            }
            catch { return "?"; }
        }

        private static int GetInteractionCount(Pawn p)
        {
            try
            {
                if (p?.interactions == null) return 0;
                return 0; // RimWorld 1.6 交互历史需通过 InteractionLog 查询，留作占位
            }
            catch { return 0; }
        }

        // ================================================================
        // Phase 1 预注入：角色卡 + 关系 + 派系词条
        // 模拟 LLM 已自行查询这些数据，消除轮次 0-1 的机械查询开销。
        // ================================================================

        /// <summary>
        /// 构建伪造的工具调用对话链，使 LLM 相信自己已查询过推荐角色的信息。
        /// 返回的消息列表会被注入到 AgentLoop 的初始消息中。
        /// </summary>
        private static List<LlmMessage> BuildPreQueriedMessages(HashSet<string> pawnIds)
        {
            var result = new List<LlmMessage>();
            if (pawnIds == null || pawnIds.Count == 0) return result;

            // --- 第一段：get_character_card ---
            var charToolCalls = new List<LlmToolCall>();
            var charResults = new List<(string toolCallId, string json)>();
            foreach (var id in pawnIds)
            {
                try
                {
                    var pawn = PawnQueryHelper.FindPawnById(id);
                    if (pawn == null) continue;
                    var card = PawnQueryHelper.BuildCharacterCard(pawn, "static");
                    var json = CardSerializer.Default.SerializeCharacterCard(card, "static", ContentProviders);
                    if (json == null || json == "{}") continue;

                    var tcId = $"pq_char_{id}";
                    charToolCalls.Add(new LlmToolCall
                    {
                        Id = tcId,
                        Name = "get_character_card",
                        Arguments = $"{{\"pawnId\":\"{id}\",\"view\":\"static\"}}"
                    });
                    charResults.Add((tcId, json));
                }
                catch { }
            }

            if (charToolCalls.Count > 0)
            {
                result.Add(LlmMessage.AssistantWithTools(charToolCalls));
                foreach (var (tcId, json) in charResults)
                    result.Add(LlmMessage.ToolResult(tcId, json));
            }

            // 收尾：角色卡已含社交数据（social / colonyOpinion），无需单独伪造 get_relationships
            if (result.Count > 0)
                result.Add(LlmMessage.Assistant("好的，我了解了一些基础信息，接下来将仔细阅读它们，然后按照推荐步骤开始工作。"));
                result.Add(LlmMessage.User("请继续"));
            return result;
        }

        /// <summary>
        /// 为推荐的 pawnIds 注入完整角色卡和关系数据到 user message。
        /// （保留此方法用于备选路径：当 preQueriedProvider 不生效时回退）
        /// </summary>
        private static void InjectPreQueriedData(StringBuilder sb, HashSet<string> pawnIds)
        {
            // --- 角色卡 ---
            try
            {
                var cards = new List<string>();
                foreach (var id in pawnIds)
                {
                    var pawn = PawnQueryHelper.FindPawnById(id);
                    if (pawn == null) continue;
                    var card = PawnQueryHelper.BuildCharacterCard(pawn, "static");
                    var json = CardSerializer.Default.SerializeCharacterCard(card, "static", ContentProviders);
                    if (json != null && json != "{}")
                        cards.Add(json);
                }
                if (cards.Count > 0)
                {
                    sb.AppendLine("## 角色详情（系统已查询）");
                    sb.AppendLine(PawnQueryHelper.SerializeJsonArray(cards));
                    sb.AppendLine();
                }
            }
            catch { }

            // --- 关系网络 ---
            try
            {
                var rels = new List<string>();
                foreach (var id in pawnIds)
                {
                    var json = RelationshipQueryProvider.GetRelationships(id);
                    if (json != null && json != "{}")
                        rels.Add(json);
                }
                if (rels.Count > 0)
                {
                    sb.AppendLine("## 角色关系（系统已查询）");
                    sb.AppendLine(PawnQueryHelper.SerializeJsonArray(rels));
                    sb.AppendLine();
                }
            }
            catch { }

            // --- 派系词条 ---
            try
            {
                var state = GlobalStateMapper.Create();
                var knSvc = KnowledgeService;
                if (state?.MapFactionPresence != null && knSvc != null && state.MapFactionPresence.Count > 0)
                {
                    var factionLookups = new List<string>();
                    foreach (var kv in state.MapFactionPresence)
                    {
                        var factionName = kv.Key;
                        if (string.IsNullOrEmpty(factionName) || factionName == "无派系")
                            continue;
                        try
                        {
                            var results = knSvc.Lookup(factionName);
                            if (results != null && results.Count > 0)
                            {
                                foreach (var e in results)
                                {
                                    var w = new NPCLife.Framework.JsonWriter(128);
                                    w.Prop("term", e.Term ?? factionName);
                                    w.Prop("definition", e.Definition ?? "");
                                    w.Prop("source", e.Source ?? "");
                                    factionLookups.Add(w.Close());
                                }
                            }
                        }
                        catch { }
                    }
                    if (factionLookups.Count > 0)
                    {
                        sb.AppendLine("## 派系背景（系统已查询）");
                        sb.AppendLine(PawnQueryHelper.SerializeJsonArray(factionLookups));
                        sb.AppendLine();
                    }
                }
            }
            catch { }
        }

        // ================================================================
        // Agent 重建
        // ================================================================

        /// <summary>
        /// 重建所有 Agent。修改提示词或驱动参数后调用，
        /// 委托给 AgentOrchestrator 执行销毁 + 重建。
        /// </summary>
        public static void RebuildAgents()
        {
            Logger?.Message("[RimLife.Core] Rebuilding agents...");
            Orchestrator?.RebuildAll();
            Logger?.Message("[RimLife.Core] Agents rebuilt successfully.");
        }
    }
}
