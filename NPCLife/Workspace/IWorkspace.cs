using NPCLife.Core;
using NPCLife.Framework.Script;
using System.Collections.Generic;

namespace NPCLife.Workspace
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

        /// <summary>编剧给导演的留言。由 FinishRound 写入。</summary>
        string DirectorMessage { get; }

        // --- 内部组件 ---

        /// <summary>工作空间内部事件池。AgentLoop 订阅 OnThresholdReached 被动激活。</summary>
        IEventLog EventPool { get; }

        /// <summary>工作空间内部技能槽。管理 MCP 技能的激活/停用。</summary>
        SkillSlot SkillSlot { get; }

        // --- 叙事操作 ---

        /// <summary>推送单句台词。立即投递到游戏侧显示。</summary>
        bool PushLine(string speakerId, string text, float delay, string type,
            WorkspaceRole callerRole, string callerId = null);

        /// <summary>结束本轮叙事。归档 recap + outcome，给导演留言。</summary>
        bool FinishRound(string recap, string outcome, string directorNote,
            IReadOnlyList<string> triggerEventIds, WorkspaceRole callerRole, string callerId = null);
    }
}
