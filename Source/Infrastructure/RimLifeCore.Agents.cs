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
            if (Workspaces == null) return; // SaveStore 未就绪，等待 Workspaces 首次创建时自动触发

            var orchestrator = FrameworkFactory.CreateAgentOrchestrator(Workspaces);

            // 导演 Agent 工厂
            orchestrator.Register(WorkspaceRole.Director, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildDirectorSystemPrompt(),
                    contextProvider: () => BuildDirectorWorkspaceSummary(mgr));
            });

            // 即兴编剧 Agent 工厂
            orchestrator.Register(WorkspaceRole.Improviser, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildImproviserSystemPrompt(ws),
                    contextProvider: () => BuildImproviserContext(ws));
            });

            // 编剧 Agent 工厂
            orchestrator.Register(WorkspaceRole.Screenwriter, (ws, mgr) =>
            {
                if (SaveStore == null || LlmAccessor == null) return null;
                return new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildScreenwriterSystemPrompt(ws),
                    contextProvider: () => BuildScreenwriterContext(ws));
            });

            _orchestrator = orchestrator;

            // 创建常驻工作空间：导演 + 即兴编剧（编剧由导演按需创建，不在此预创建）
            var directorWs = orchestrator.GetOrCreateWorkspace(WorkspaceRole.Director);
            if (directorWs != null && Workspaces != null)
            {
                Workspaces.SetLabel(directorWs.Id, "导演");
            }

            var improWs = orchestrator.GetOrCreateWorkspace(WorkspaceRole.Improviser);
            if (improWs != null && Workspaces != null)
            {
                Workspaces.SetLabel(improWs.Id, "即兴剧情");
                Workspaces.SetDirectorMessage(improWs.Id, "无长期记忆的常驻剧情线，可根据任意一组事件创作剧情。无论事件数量每个推送轮次都会触发");
            }

            // 将全局模型配置同步到新创建的工作空间（导演 + 即兴编剧）
            SyncModelConfigToWorkspaces();

            Logger?.Message("[RimLife.Core] AgentOrchestrator initialized with 3 role factories.");
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
                MaxRounds = DriverConfig.MaxAgentRounds
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

            // 推荐角色：随机选取 1-3 个可见人类角色作为叙事引子
            // 不依赖事件关联，让编剧自行从推荐角色出发探索周围角色构建场景
            try
            {
                var rng = new System.Random();
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p =>
                        p != null && !p.Dead && p.RaceProps.Humanlike));
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

                AppendRelationshipSummary(sb, pawnIds);
                InjectPreQueriedData(sb, pawnIds);
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

            // 推荐角色：随机选取 1-3 个可见人类角色作为叙事引子
            // 不依赖事件关联，让编剧自行从推荐角色出发探索周围角色构建场景
            try
            {
                var rng = new System.Random();
                var allPawns = new List<Pawn>();
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    allPawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(p =>
                        p != null && !p.Dead && p.RaceProps.Humanlike));
                }
                // 随机洗牌取 1-3 个
                var selected = allPawns.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList();
                var pawnIds = new HashSet<string>(selected.Select(p => p.ThingID));

                var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                if (!string.IsNullOrEmpty(summary) && summary != "[]")
                {
                    sb.AppendLine("## 推荐角色 自由主体");
                    sb.AppendLine(summary);
                    sb.AppendLine();
                }

                AppendRelationshipSummary(sb, pawnIds);
                InjectPreQueriedData(sb, pawnIds);
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
                        sb.AppendLine("## 推荐角色 事件主体");
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

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            AppendSkillPrompts(sb, ws);
            AppendStyleInstruction(sb, pa);
            return sb.ToString();
        }

        private static string BuildImproviserSystemPrompt(NPCLife.Workspace.IWorkspace ws)
        {
            var pa = PromptAdditions;
            var sb = new System.Text.StringBuilder(NPCLife.Driver.PromptConfig.DefaultImproviserPrompt);
            AppendAdditions(sb, pa.ImproviserAdditions, "RimWorld 即兴编剧附加指令");

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            AppendSkillPrompts(sb, ws);
            AppendStyleInstruction(sb, pa);
            return sb.ToString();
        }

        /// <summary>
        /// 构建即兴编剧激活时的动态上下文。当前与 Screenwriter 共享相同实现。
        /// 保留独立方法签名作为扩展点，便于未来可能的分化。
        /// </summary>
        private static string BuildImproviserContext(NPCLife.Workspace.IWorkspace ws)
        {
            return BuildScreenwriterContext(ws);
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

        /// <summary>将已激活技能的 PromptInstruction 注入到 system prompt 中。</summary>
        private static void AppendSkillPrompts(System.Text.StringBuilder sb, NPCLife.Workspace.IWorkspace ws)
        {
            try
            {
                var activeIds = ws?.SkillSlot?.ActiveSkillIds;
                if (activeIds == null || activeIds.Count == 0) return;
                var prompts = McpSkillRegistry.GetActiveSkillPrompts(activeIds);
                if (!string.IsNullOrEmpty(prompts))
                {
                    sb.AppendLine("## 技能使用说明");
                    sb.AppendLine(prompts);
                }
            }
            catch { }
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
                // 如果所有推荐角色都是 Neutral 且无交互，该行无信息量，直接跳过
                bool allNeutral = parts.TrueForAll(p => p.Contains("(Neutral,") || p.Contains("(?,无)"));
                if (allNeutral) return;

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

        /// <summary>
        /// 获取角色的交互历史数量。
        /// TODO: RimWorld 1.6 交互历史需通过 InteractionLog 查询，当前返回 0。
        /// </summary>
        private static int GetInteractionCount(Pawn p)
        {
            return 0;
        }

        // ================================================================
        // Phase 1 预注入：角色卡 + 关系 + 派系词条
        // 以 InjectPreQueriedData 方式注入到 user message 中。
        // ================================================================

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
                    sb.AppendLine("## 角色详情");
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
                    sb.AppendLine("## 角色关系");
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
                        sb.AppendLine("## 派系背景");
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
