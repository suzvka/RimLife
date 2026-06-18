using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.Script;
using System;
using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// IWorkspace 实现。包装 WorkspaceState，暴露组件和操作。
    /// 由 WorkspaceManager 创建和管理，不直接对外暴露。
    /// </summary>
    internal class WorkspaceImpl : IWorkspace
    {
        private readonly WorkspaceState _state;
        private readonly WorkspaceEventPool _eventPool;
        private readonly SkillSlot _skillSlot;
        private readonly Func<string> _timeProvider;
        private readonly Action<string> _publishUpdated;
        private readonly ILogger _logger;

        public WorkspaceImpl(
            WorkspaceState state,
            DriverConfig config,
            ICardSerializer serializer,
            Func<string> timeProvider,
            Action<string> publishUpdated,
            ILogger logger)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _timeProvider = timeProvider ?? (() => "");
            _publishUpdated = publishUpdated;
            _logger = logger;

            _eventPool = new WorkspaceEventPool(state, config, serializer);
            _skillSlot = new SkillSlot(
                state.ActiveSkillIds ?? new List<string>(),
                () =>
                {
                    state.ActiveSkillIds = _skillSlot.ActiveSkillIds is List<string> list ? list : new List<string>(_skillSlot.ActiveSkillIds);
                    publishUpdated?.Invoke(state.Id);
                });
        }

        // ================================================================
        // 元数据（只读代理到 WorkspaceState）
        // ================================================================

        public string Id => _state.Id;
        public string Label => _state.Label;
        public WorkspaceStatus Status => _state.Status;
        public WorkspaceRole CreatedByRole => _state.CreatedByRole;
        public string ParentId => _state.ParentId;
        public IReadOnlyList<string> MergedFromIds => _state.MergedFromIds;
        public IReadOnlyList<string> ColonistIds => _state.ColonistIds;
        public IReadOnlyList<string> Tags => _state.Tags;
        public IReadOnlyList<WorkspaceRound> Rounds => _state.Rounds;
        public string CurrentRecap => _state.CurrentRecap;
        public string CreatedAt => _state.CreatedAt;
        public string LastActivityAt => _state.LastActivityAt;
        public string Outcome => _state.Outcome;
        public string DirectorMessage => _state.DirectorMessage;

        // ================================================================
        // 组件
        // ================================================================

        public IEventLog EventPool => _eventPool;
        public SkillSlot SkillSlot => _skillSlot;

        /// <summary>获取内部 WorkspaceState（仅供 WorkspaceManager 持久化和结构操作使用）。</summary>
        internal WorkspaceState State => _state;

        // ================================================================
        // 叙事操作
        // ================================================================

        private readonly List<ScriptLine> _pendingScriptLines = new List<ScriptLine>();

        public bool PushLine(string speakerId, string text, float delay, string type,
            WorkspaceRole callerRole, string callerId = null)
        {
            if (string.IsNullOrEmpty(text) && type != "pause") return false;

            if (callerRole != WorkspaceRole.Screenwriter && callerRole != WorkspaceRole.Freelancer)
            {
                _logger?.Warning($"[NPCLife.Workspace] PushLine rejected: caller is {callerRole}, only Screenwriter/Freelancer can push lines.");
                return false;
            }

            if (_state.Status != WorkspaceStatus.Active)
            {
                _logger?.Warning($"[NPCLife.Workspace] PushLine failed: workspace '{_state.Label}' is not Active (status={_state.Status}).");
                return false;
            }

            var lineType = ScriptFormat.ParseLineType(type);
            var line = new ScriptLine
            {
                SpeakerId = string.IsNullOrEmpty(speakerId) ? null : speakerId,
                Text = text ?? "",
                RelativeTime = delay,
                Type = lineType
            };

            _pendingScriptLines.Add(line);
            _state.LastActivityAt = _timeProvider();
            _publishUpdated?.Invoke(_state.Id);

            // 立即发布单行就绪事件 → ScriptDeliveryService 逐行投递
            EventBus.Publish(FrameworkEvents.ScriptLineReady, EventArg.WithPayload(
                ("workspaceId", _state.Id),
                ("speakerId", line.SpeakerId ?? ""),
                ("text", line.Text),
                ("delay", line.RelativeTime.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("type", line.Type.ToString())
            ));

            return true;
        }

        public bool FinishRound(string recap, string outcome, string directorNote,
            IReadOnlyList<string> triggerEventIds, WorkspaceRole callerRole, string callerId = null)
        {
            if (callerRole != WorkspaceRole.Screenwriter && callerRole != WorkspaceRole.Freelancer)
            {
                _logger?.Warning($"[NPCLife.Workspace] FinishRound rejected: caller is {callerRole}.");
                return false;
            }

            if (_state.Status != WorkspaceStatus.Active)
            {
                _logger?.Warning($"[NPCLife.Workspace] FinishRound failed: workspace '{_state.Label}' is not Active (status={_state.Status}).");
                return false;
            }

            string now = _timeProvider();
            int nextSeq = _state.Rounds?.Count ?? 0;

            var scriptLines = new List<ScriptLine>(_pendingScriptLines);
            _pendingScriptLines.Clear();

            var round = new WorkspaceRound
            {
                Seq = nextSeq,
                Type = RoundType.Normal,
                Recap = recap ?? "",
                Narrative = "",
                CreatedAt = now,
                TriggerEventIds = triggerEventIds != null ? new List<string>(triggerEventIds) : new List<string>(),
                AuthorRole = callerRole,
                AuthorId = callerId,
                ScriptLines = scriptLines
            };

            if (_state.Rounds == null)
                _state.Rounds = new List<WorkspaceRound>();
            _state.Rounds.Add(round);

            _state.CurrentRecap = recap ?? "";
            _state.LastActivityAt = now;

            if (!string.IsNullOrEmpty(outcome))
                _state.Outcome = outcome;

            if (!string.IsNullOrEmpty(directorNote))
                _state.DirectorMessage = directorNote;

            _publishUpdated?.Invoke(_state.Id);

            // 发布轮次完成事件
            EventBus.Publish(FrameworkEvents.ScriptReady, EventArg.WithPayload(
                ("workspaceId", _state.Id),
                ("roundSeq", nextSeq.ToString()),
                ("callerRole", callerRole.ToString())
            ));

            return true;
        }

        // ================================================================
        // 内部状态变更（由 WorkspaceManager 调用）
        // ================================================================

        internal void SetStatus(WorkspaceStatus newStatus, string outcome = null)
        {
            _state.Status = newStatus;
            _state.LastActivityAt = _timeProvider();
            if (outcome != null)
                _state.Outcome = outcome;
        }
    }
}
