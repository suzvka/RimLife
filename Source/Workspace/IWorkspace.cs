using RimLife.Core;
using System.Collections.Generic;

namespace RimLife.Workspace
{
    /// <summary>
    /// 工作空间门面接口。管理器返回此接口，外部通过它访问组件和操作。
    /// 元数据只读，状态变更由 IWorkspaceManager 控制。
    /// </summary>
    public interface IWorkspace
    {
        // --- 元数据（只读） ---

        string Id { get; }
        string Label { get; }
        WorkspaceStatus Status { get; }
        WorkspaceRole CreatedByRole { get; }
        string ParentId { get; }
        IReadOnlyList<string> MergedFromIds { get; }
        IReadOnlyList<string> ColonistIds { get; }
        IReadOnlyList<string> Tags { get; }
        IReadOnlyList<WorkspaceRound> Rounds { get; }
        string CurrentRecap { get; }
        string CreatedAt { get; }
        string LastActivityAt { get; }
        string Outcome { get; }
        StorylineSignal? LastSignal { get; }

        // --- 内部组件 ---

        /// <summary>工作空间内部事件池。AgentLoop 订阅 OnThresholdReached 被动激活。</summary>
        IEventLog EventPool { get; }

        /// <summary>工作空间内部技能槽。管理 MCP 技能的激活/停用。</summary>
        SkillSlot SkillSlot { get; }

        // --- 叙事操作 ---

        /// <summary>推送一个叙事回合。</summary>
        bool PushRound(string recap, string narrative,
            IReadOnlyList<string> triggerEventIds, WorkspaceRole callerRole, string callerId = null);

        /// <summary>上报剧情信号。</summary>
        bool ReportSignal(SignalType signalType, string note,
            string suggestedTargetId, WorkspaceRole callerRole);
    }
}
