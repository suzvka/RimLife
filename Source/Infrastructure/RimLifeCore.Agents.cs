using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NPCLife.Agent;
using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
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
                                    systemPrompt: BuildImproviserSystemPrompt(improviserWs));
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
                                    contextProvider: () => BuildDirectorWorkspaceSummary(Workspaces));
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
                    contextProvider: () => BuildScreenwriterContext(ws));

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

            // 殖民地快照
            try
            {
                var ctx = ColonyContextMapper.Create();
                if (ctx != null)
                {
                    sb.AppendLine("## 殖民地快照");
                    sb.AppendLine(CardSerializer.Default.SerializeColonyContext(ctx));
                    sb.AppendLine();
                }
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

            // 当前活跃剧情线（复用 DirectionMcpProvider.ListWorkspaces 的完整数据）
            if (manager == null)
            {
                sb.AppendLine("## 当前活跃剧情线\n（无）");
            }
            else
            {
                try
                {
                    var provider = new NPCLife.Workspace.DirectionMcpProvider(() => manager, Logger);
                    var storylinesJson = provider.ListWorkspaces();
                    if (string.IsNullOrEmpty(storylinesJson) || storylinesJson == "[]")
                    {
                        sb.AppendLine("## 当前活跃剧情线\n（无）");
                    }
                    else
                    {
                        sb.AppendLine("## 当前活跃剧情线（路由事件时从此处复制 ID）");
                        sb.AppendLine(storylinesJson);
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

            // 殖民地快照
            try
            {
                var ctx = ColonyContextMapper.Create();
                if (ctx != null)
                {
                    sb.AppendLine("## 殖民地快照");
                    sb.AppendLine(CardSerializer.Default.SerializeColonyContext(ctx));
                    sb.AppendLine();
                }
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
            sb.Append(ScriptFormat.GetFormatSpec());

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
            sb.Append(ScriptFormat.GetFormatSpec());

            AppendStyleInstruction(sb, pa);
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
