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
using RimLife.Infrastructure.Mcp;
using RimLife.Mappers;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLifeCore 的 Agent 管理部分。
    /// 包含 Agent 创建、销毁、系统提示词构建和重建逻辑。
    /// </summary>
    public static partial class RimLifeCore
    {
        private static AgentLoop _directorAgent;
        private static readonly object _directorAgentLock = new object();
        private static AgentLoop _improviserAgent;
        private static readonly object _improviserAgentLock = new object();
        private static readonly Dictionary<string, AgentLoop> _screenwriters = new Dictionary<string, AgentLoop>();
        private static readonly object _screenwritersLock = new object();

        /// <summary>线程本地：当前上下文中收集的推荐角色 ID，供 preQueriedProvider 读取。</summary>
        [ThreadStatic]
        private static HashSet<string> _currentPawnIds;

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

        /// <summary>
        /// 获取导演所在工作空间。
        /// 按 CreatedByRole == Director &amp;&amp; Status == Active 查找，不存在时自动创建。
        /// </summary>
        public static NPCLife.Workspace.IWorkspace GetDirectorWorkspace()
        {
            if (Workspaces == null) return null;

            var actives = Workspaces.GetActive();
            foreach (var ws in actives)
            {
                if (ws.CreatedByRole == NPCLife.Workspace.WorkspaceRole.Director)
                    return ws;
            }

            return Workspaces.Create("Director", NPCLife.Workspace.WorkspaceRole.Director);
        }

        /// <summary>
        /// 获取即兴编剧所在工作空间。
        /// 按 CreatedByRole == Improviser &amp;&amp; Status == Active 查找，不存在时自动创建。
        /// </summary>
        public static NPCLife.Workspace.IWorkspace GetImproviserWorkspace()
        {
            if (Workspaces == null) return null;

            var actives = Workspaces.GetActive();
            foreach (var ws in actives)
            {
                if (ws.CreatedByRole == NPCLife.Workspace.WorkspaceRole.Improviser)
                    return ws;
            }

            return Workspaces.Create("Improviser", NPCLife.Workspace.WorkspaceRole.Improviser);
        }

        /// <summary>
        /// 获取即兴编剧 AgentLoop 实例。绑定即兴编剧工作空间的 EventPool。
        /// 存档未加载或 LLM 未配置时返回 null。
        /// </summary>
        public static AgentLoop GetImproviserAgent()
        {
            if (_improviserAgent == null)
            {
                lock (_improviserAgentLock)
                {
                    if (_improviserAgent == null && SaveStore != null && LlmAccessor != null)
                    {
                        var improviserWs = GetImproviserWorkspace();
                        if (improviserWs != null)
                        {
                            // 重检：GetImproviserWorkspace 可能通过 onWorkspaceReady
                            // 回调重入本方法并已完成创建（C# lock 可重入）。
                            if (_improviserAgent == null)
                            {
                                _improviserAgent = new AgentLoop(
                                    workspace: improviserWs,
                                    deps: BuildAgentDeps(),
                                    systemPrompt: BuildImproviserSystemPrompt(improviserWs),
                                    contextProvider: () => BuildImproviserContext(improviserWs),
                                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));
                            }
                        }
                    }
                }
            }
            return _improviserAgent;
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
                    if (_directorAgent == null)
                    {
                        if (SaveStore == null)
                        {
                            Logger?.Warning("[RimLife.Core] GetDirectorAgent: SaveStore is null (no save loaded?)");
                            return null;
                        }
                        if (LlmAccessor == null)
                        {
                            Logger?.Warning("[RimLife.Core] GetDirectorAgent: LlmAccessor is null");
                            return null;
                        }

                        var directorWs = GetDirectorWorkspace();
                        if (directorWs != null)
                        {
                            // 重检：GetDirectorWorkspace 可能通过 onWorkspaceReady
                            // 回调重入本方法并已完成创建（C# lock 可重入）。
                            if (_directorAgent == null)
                            {
                                _directorAgent = new AgentLoop(
                                    workspace: directorWs,
                                    deps: BuildAgentDeps(),
                                    systemPrompt: BuildDirectorSystemPrompt(),
                                    contextProvider: () => BuildDirectorWorkspaceSummary(Workspaces),
                                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));
                                Logger?.Message("[RimLife.Core] DirectorAgent created and subscribed to EventPool");
                            }
                        }
                        else
                        {
                            Logger?.Warning("[RimLife.Core] GetDirectorAgent: DirectorWorkspace is null");
                        }
                    }
                }
            }
            return _directorAgent;
        }

        /// <summary>
        /// 根据工作空间角色创建对应类型的 Agent。
        /// Director 工作空间 → Director Agent；Improviser → Improviser Agent；其他 → Screenwriter。
        /// </summary>
        private static void EnsureAgentForWorkspace(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            var ws = Workspaces?.Get(workspaceId);
            if (ws == null) return;

            if (ws.CreatedByRole == NPCLife.Workspace.WorkspaceRole.Director)
                GetDirectorAgent();
            else if (ws.CreatedByRole == NPCLife.Workspace.WorkspaceRole.Improviser)
                GetImproviserAgent();
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

                var agent = new AgentLoop(
                    workspace: ws,
                    deps: BuildAgentDeps(),
                    systemPrompt: BuildScreenwriterSystemPrompt(ws),
                    contextProvider: () => BuildScreenwriterContext(ws),
                    preQueriedProvider: () => BuildPreQueriedMessages(_currentPawnIds));

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

            // 当前角色列表（仅本轮事件相关角色；无事件时回退到全量摘要）
            try
            {
                var directorWs = GetDirectorWorkspace();
                var pawnIds = new HashSet<string>();

                if (directorWs != null)
                {
                    var events = directorWs.EventPool.Query(EventQuery.All);
                    foreach (var evt in events)
                    {
                        if (evt.Actors == null) continue;
                        foreach (var actor in evt.Actors)
                        {
                            if (actor.RefType == "Pawn" && !string.IsNullOrEmpty(actor.ID))
                                pawnIds.Add(actor.ID);
                        }
                    }
                }

                if (pawnIds.Count > 0)
                {
                    var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                    if (!string.IsNullOrEmpty(summary) && summary != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(summary);
                        sb.AppendLine();
                    }
                }
                else
                {
                    var allCondensed = CharacterQueryProvider.GetAllPawnsCondensed(8);
                    if (!string.IsNullOrEmpty(allCondensed) && allCondensed != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(allCondensed);
                        sb.AppendLine();
                    }
                }

                // === Phase 1 预注入：角色卡 + 关系（系统已查询，无需 LLM 再调工具） ===
                _currentPawnIds = pawnIds;
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

            // 当前活跃剧情线（仅编剧工作空间，过滤导演和即兴编剧）
            if (manager == null)
            {
                sb.AppendLine("## 当前活跃剧情线\n（无）");
            }
            else
            {
                try
                {
                    var storylines = manager.GetActive()
                        .Where(ws => ws.CreatedByRole != WorkspaceRole.Director
                                  && ws.CreatedByRole != WorkspaceRole.Improviser)
                        .ToList();

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
                        sb.AppendLine("## 当前活跃剧情线（路由事件时从此处复制 ID）");
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

            // 当前角色列表（仅本轮事件相关角色；无事件时回退到全量摘要）
            try
            {
                var directorWs = GetDirectorWorkspace();
                var pawnIds = new HashSet<string>();

                if (directorWs != null)
                {
                    var events = directorWs.EventPool.Query(EventQuery.All);
                    foreach (var evt in events)
                    {
                        if (evt.Actors == null) continue;
                        foreach (var actor in evt.Actors)
                        {
                            if (actor.RefType == "Pawn" && !string.IsNullOrEmpty(actor.ID))
                                pawnIds.Add(actor.ID);
                        }
                    }
                }

                if (pawnIds.Count > 0)
                {
                    var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                    if (!string.IsNullOrEmpty(summary) && summary != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(summary);
                        sb.AppendLine();
                    }
                }
                else
                {
                    var allCondensed = CharacterQueryProvider.GetAllPawnsCondensed(8);
                    if (!string.IsNullOrEmpty(allCondensed) && allCondensed != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(allCondensed);
                        sb.AppendLine();
                    }
                }

                // === Phase 1 预注入：角色卡 + 关系（系统已查询，无需 LLM 再调工具） ===
                _currentPawnIds = pawnIds;
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

            // 将工作空间上下文（ID、关联角色、格式规范）注入系统提示词。
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"工作空间 ID：{ws.Id}");
            sb.AppendLine($"工作空间：{ws.Label ?? "Unnamed"}");
            var directorWs = GetDirectorWorkspace();
            if (directorWs != null)
                sb.AppendLine($"导演工作空间 ID：{directorWs.Id}");
            if (ws.FocusCharacterIds != null && ws.FocusCharacterIds.Count > 0)
                sb.AppendLine($"关联角色：{string.Join(", ", ws.FocusCharacterIds)}");
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

            // 将工作空间上下文（ID、关联角色、格式规范）注入系统提示词。
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"工作空间 ID：{ws.Id}");
            sb.AppendLine($"工作空间：{ws.Label ?? "Improviser"}");
            var directorWs = GetDirectorWorkspace();
            if (directorWs != null)
                sb.AppendLine($"导演工作空间 ID：{directorWs.Id}");
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

            // 当前角色列表（仅本轮事件相关角色；无事件时回退到全量摘要）
            try
            {
                var directorWs = GetDirectorWorkspace();
                var pawnIds = new HashSet<string>();

                if (directorWs != null)
                {
                    var events = directorWs.EventPool.Query(EventQuery.All);
                    foreach (var evt in events)
                    {
                        if (evt.Actors == null) continue;
                        foreach (var actor in evt.Actors)
                        {
                            if (actor.RefType == "Pawn" && !string.IsNullOrEmpty(actor.ID))
                                pawnIds.Add(actor.ID);
                        }
                    }
                }

                if (pawnIds.Count > 0)
                {
                    var summary = CharacterQueryProvider.GetPawnsByIds(pawnIds);
                    if (!string.IsNullOrEmpty(summary) && summary != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(summary);
                        sb.AppendLine();
                    }
                }
                else
                {
                    var allCondensed = CharacterQueryProvider.GetAllPawnsCondensed(8);
                    if (!string.IsNullOrEmpty(allCondensed) && allCondensed != "[]")
                    {
                        sb.AppendLine("## 推荐本轮叙事主角");
                        sb.AppendLine(allCondensed);
                        sb.AppendLine();
                    }
                }

                // === Phase 1 预注入：角色卡 + 关系（系统已查询，无需 LLM 再调工具） ===
                _currentPawnIds = pawnIds;
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
        /// 销毁现有 Agent 实例，下次事件触发时用新参数自动重建。
        /// </summary>
        public static void RebuildAgents()
        {
            Logger?.Message("[RimLife.Core] Rebuilding agents...");

            lock (_directorAgentLock)
            {
                _directorAgent?.Dispose();
                _directorAgent = null;
            }

            lock (_improviserAgentLock)
            {
                _improviserAgent?.Dispose();
                _improviserAgent = null;
            }

            lock (_screenwritersLock)
            {
                foreach (var kv in _screenwriters)
                    kv.Value?.Dispose();
                _screenwriters.Clear();
            }

            // 如果工作空间管理器已就绪，立即为活跃工作空间重建 Agent
            if (Workspaces != null)
            {
                var actives = Workspaces.GetActive();
                foreach (var ws in actives)
                    EnsureAgentForWorkspace(ws.Id);
            }

            Logger?.Message("[RimLife.Core] Agents rebuilt successfully.");
        }
    }
}
