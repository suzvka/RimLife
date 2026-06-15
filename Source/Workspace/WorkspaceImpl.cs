using RimLife.Cards;
using RimLife.Core;
using RimLife.Driver;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace RimLife.Workspace
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
        public StorylineSignal? LastSignal => _state.LastSignal;

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

        public bool PushRound(string recap, string narrative,
            IReadOnlyList<string> triggerEventIds, WorkspaceRole callerRole, string callerId = null)
        {
            if (string.IsNullOrEmpty(recap) && string.IsNullOrEmpty(narrative)) return false;

            if (callerRole != WorkspaceRole.Screenwriter)
            {
                _logger?.Warning($"[RimLife.Workspace] PushRound rejected: caller is {callerRole}, only Screenwriter can push rounds.");
                return false;
            }

            if (_state.Status != WorkspaceStatus.Active)
            {
                _logger?.Warning($"[RimLife.Workspace] PushRound failed: workspace '{_state.Label}' is not Active (status={_state.Status}).");
                return false;
            }

            string now = _timeProvider();
            int nextSeq = _state.Rounds?.Count ?? 0;

            var round = new WorkspaceRound
            {
                Seq = nextSeq,
                Type = RoundType.Normal,
                Recap = recap ?? "",
                Narrative = narrative ?? "",
                CreatedAt = now,
                TriggerEventIds = triggerEventIds != null ? new List<string>(triggerEventIds) : new List<string>(),
                AuthorRole = callerRole,
                AuthorId = callerId
            };

            if (_state.Rounds == null)
                _state.Rounds = new List<WorkspaceRound>();
            _state.Rounds.Add(round);

            _state.CurrentRecap = recap ?? "";
            _state.LastActivityAt = now;

            _publishUpdated?.Invoke(_state.Id);
            return true;
        }

        public bool ReportSignal(SignalType signalType, string note,
            string suggestedTargetId, WorkspaceRole callerRole)
        {
            if (callerRole != WorkspaceRole.Screenwriter)
            {
                _logger?.Warning($"[RimLife.Workspace] ReportSignal rejected: caller is {callerRole}, only Screenwriter can report signals.");
                return false;
            }

            string now = _timeProvider();
            _state.LastSignal = new StorylineSignal
            {
                Type = signalType,
                ReportedAt = now,
                Note = note ?? "",
                SuggestedTargetId = suggestedTargetId
            };
            _state.LastActivityAt = now;

            _publishUpdated?.Invoke(_state.Id);
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
