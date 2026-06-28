using System.Collections.Generic;
using System.Text;
using NPCLife.Agent;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.Script;
using NPCLife.Workspace;

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

            return Workspaces.Create("Director", null, NPCLife.Workspace.WorkspaceRole.Director);
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

            return Workspaces.Create("Improviser", null, NPCLife.Workspace.WorkspaceRole.Improviser);
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
                            _improviserAgent = new AgentLoop(
                                pool: improviserWs.EventPool,
                                llm: LlmAccessor,
                                credentialStore: CredentialManager,
                                systemPrompt: BuildImproviserSystemPrompt(improviserWs),
                                skillIds: new[] { "workspace_improviser", "character_query", "event_query" },
                                maxRounds: DriverConfig.MaxAgentRounds,
                                logger: Logger,
                                serializer: CardSerializer.Default,
                                temperature: PromptAdditions.Temperature,
                                modelRefsJson: improviserWs.ModelRefs,
                                currentModel: improviserWs.CurrentModel);
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
                            Logger?.Warning("[RimLife.Core] GetDirectorAgent: SaveStore is null");
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
                            _directorAgent = new AgentLoop(
                                pool: directorWs.EventPool,
                                llm: LlmAccessor,
                                credentialStore: CredentialManager,
                                systemPrompt: BuildDirectorSystemPrompt(),
                                skillIds: new[] { "workspace_direction", "colony_overview", "character_query", "event_query", "knowledge_management" },
                                maxRounds: DriverConfig.MaxAgentRounds,
                                logger: Logger,
                                serializer: CardSerializer.Default,
                                contextProvider: () => BuildDirectorWorkspaceSummary(Workspaces),
                                temperature: PromptAdditions.Temperature,
                                modelRefsJson: directorWs.ModelRefs,
                                currentModel: directorWs.CurrentModel);
                            Logger?.Message("[RimLife.Core] DirectorAgent created and subscribed to EventPool");
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

                var skillIds = new List<string> { "workspace_writing" };
                if (ws.SkillSlot?.ActiveSkillIds != null)
                    skillIds.AddRange(ws.SkillSlot.ActiveSkillIds);

                var agent = new AgentLoop(
                    pool: ws.EventPool,
                    llm: LlmAccessor,
                    credentialStore: CredentialManager,
                    systemPrompt: BuildScreenwriterSystemPrompt(ws),
                    skillIds: skillIds.ToArray(),
                    maxRounds: DriverConfig.MaxAgentRounds,
                    logger: Logger,
                    serializer: CardSerializer.Default,
                    temperature: PromptAdditions.Temperature,
                    modelRefsJson: ws.ModelRefs,
                    currentModel: ws.CurrentModel);

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
            if (manager == null) return "## 当前活跃工作空间\n（无）";

            var workspaces = manager.GetActive();
            if (workspaces == null || workspaces.Count == 0)
                return "## 当前活跃工作空间\n（无）";

            var sb = new System.Text.StringBuilder("## 当前活跃工作空间");
            foreach (var ws in workspaces)
            {
                sb.AppendLine();
                sb.Append($"- {ws.Label} (id={ws.Id})");
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
            if (ws.ColonistIds != null && ws.ColonistIds.Count > 0)
                sb.AppendLine($"关联角色：{string.Join(", ", ws.ColonistIds)}");
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
