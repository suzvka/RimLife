using System;
using System.Collections.Generic;
using RimLife.Workspace;

namespace RimLife.Core
{
    /// <summary>
    /// 工作空间管理器抽象接口。
    /// 负责剧情线工作空间的创建、查询、状态管理、分支、合并和回合推送。
    /// 实现：WorkspaceManager。
    /// </summary>
    public interface IWorkspaceManager
    {
        /// <summary>创建新的工作空间。</summary>
        WorkspaceState Create(string label, List<string> colonistIds, List<string> tags, WorkspaceRole createdByRole);

        /// <summary>按 ID 查询工作空间。</summary>
        WorkspaceState Get(string id);

        /// <summary>列出工作空间（可选状态过滤）。</summary>
        IReadOnlyList<WorkspaceState> List(WorkspaceStatus? statusFilter = null);

        /// <summary>获取所有 Active 状态的工作空间。</summary>
        IReadOnlyList<WorkspaceState> GetActive();

        /// <summary>更新工作空间状态。</summary>
        bool UpdateStatus(string id, WorkspaceStatus newStatus, string outcome = null);

        /// <summary>推送一个叙事回合。</summary>
        bool PushRound(string workspaceId, string recap, string narrative,
            List<string> triggerEventIds, WorkspaceRole callerRole, string callerId = null);

        /// <summary>从父工作空间分支。</summary>
        WorkspaceState Branch(string parentId, string newLabel, string branchRecap, WorkspaceRole callerRole);

        /// <summary>合并工作空间。</summary>
        bool Merge(string sourceId, string targetId, string mergeRecap, WorkspaceRole callerRole);

        /// <summary>上报剧情信号。</summary>
        bool ReportSignal(string workspaceId, SignalType signalType, string note,
            string suggestedTargetId, WorkspaceRole callerRole);

        /// <summary>激活技能。</summary>
        string ActivateSkill(string workspaceId, string skillId);

        /// <summary>停用技能。</summary>
        string DeactivateSkill(string workspaceId, string skillId);

        /// <summary>获取指定工作空间的已激活 Skill ID 列表。</summary>
        IReadOnlyList<string> GetActiveSkillIds(string workspaceId);
    }
}
