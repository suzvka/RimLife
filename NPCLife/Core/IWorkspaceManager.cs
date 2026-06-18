using System;
using System.Collections.Generic;
using NPCLife.Cards;
using NPCLife.Workspace;

namespace NPCLife.Core
{
    /// <summary>
    /// 工作空间管理器抽象接口。
    /// 职责：工作空间的 CRUD、分支/合并结构操作、事件路由。
    /// 工作空间内部组件（事件池、技能槽）通过 IWorkspace 接口访问，管理器不关心。
    /// 实现：WorkspaceManager。
    /// </summary>
    public interface IWorkspaceManager
    {
        // --- CRUD（5 方法） ---

        /// <summary>创建新的工作空间。</summary>
        IWorkspace Create(string label, List<string> colonistIds, List<string> tags, WorkspaceRole createdByRole);

        /// <summary>按 ID 查询工作空间。</summary>
        IWorkspace Get(string id);

        /// <summary>列出工作空间（可选状态过滤）。</summary>
        IReadOnlyList<IWorkspace> List(WorkspaceStatus? statusFilter = null);

        /// <summary>获取所有 Active 状态的工作空间。</summary>
        IReadOnlyList<IWorkspace> GetActive();

        /// <summary>更新工作空间状态。</summary>
        bool UpdateStatus(string id, WorkspaceStatus newStatus, string outcome = null);

        // --- 结构操作（2 方法） ---

        /// <summary>从父工作空间分支。</summary>
        IWorkspace Branch(string parentId, string newLabel, string branchRecap, WorkspaceRole callerRole);

        /// <summary>合并工作空间。</summary>
        bool Merge(string sourceId, string targetId, string mergeRecap, WorkspaceRole callerRole);

        // --- 事件路由（1 方法） ---

        /// <summary>
        /// 将事件路由到指定工作空间的事件池。
        /// 管理器只做转发，实际写入由工作空间的 EventPool 组件处理。
        /// </summary>
        bool RouteEvents(string workspaceId, IReadOnlyList<IGameEvent> events);

        // --- 持久化（1 方法） ---

        /// <summary>
        /// 将当前内存中的所有工作空间状态刷入持久化存储。
        /// 由宿主（RimLife）在 RimWorld 存档钩子中调用。
        /// </summary>
        void Persist();
    }
}
