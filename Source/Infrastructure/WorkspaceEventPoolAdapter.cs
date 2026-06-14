using RimLife.Cards;
using RimLife.Core;
using RimLife.Driver;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using RimLife.Workspace;
using System;
using System.Collections.Generic;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 工作空间事件池适配器。将 WorkspaceState 内部的 KV 事件缓存包装为 IEventLog，
    /// 使 AgentLoop 可以无感知地消费工作空间事件。
    ///
    /// 适配原理：
    /// - Append 委托给 IWorkspaceManager.PushEvent（序列化 + KV 写入）
    /// - DrainPending 委托给 IWorkspaceManager.DrainPendingEvents（KV 读取 + 反序列化）
    /// - 阈值检测由 WorkspaceManager.EvaluatePendingThreshold 完成，
    ///   但 OnThresholdReached 事件由外部手动触发（PushEvent 已处理）
    ///
    /// 零持久化：事件已存储在 WorkspaceState 中，随 workspace 序列化。
    /// </summary>
    internal class WorkspaceEventPoolAdapter : IEventLog
    {
        private readonly WorkspaceState _ws;
        private readonly DriverConfig _config;
        private readonly Func<IWorkspaceManager> _getManager;
        private readonly ILogger _logger;
        private readonly ICardSerializer _serializer;

        public event Action OnThresholdReached;

        public WorkspaceEventPoolAdapter(
            WorkspaceState ws,
            DriverConfig config,
            Func<IWorkspaceManager> getManager,
            ILogger logger,
            ICardSerializer serializer)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
            _config = config ?? DriverConfig.CreateDefault();
            _getManager = getManager ?? throw new ArgumentNullException(nameof(getManager));
            _logger = logger;
            _serializer = serializer ?? CardSerializer.Default;
        }

        // --- IEventLog 实现 ---

        public void Append(IGameEvent evt)
        {
            var manager = _getManager();
            if (manager != null)
                manager.PushEvent(_ws.Id, evt);
        }

        public IReadOnlyList<IGameEvent> Query(EventQuery query)
        {
            // 简单实现：返回所有 pending 事件
            var manager = _getManager();
            if (manager == null) return new List<IGameEvent>();

            var pendingIds = _ws.PendingEventIds;
            if (pendingIds == null || pendingIds.Count == 0) return new List<IGameEvent>();

            var result = new List<IGameEvent>();
            foreach (var id in pendingIds)
            {
                if (_ws.EventCache != null && _ws.EventCache.TryGetValue(id, out var json))
                {
                    var evt = _serializer.DeserializeEvent(json);
                    if (evt != null) result.Add(evt);
                }
            }
            return result;
        }

        public int Count(EventQuery query)
        {
            return _ws.PendingEventIds?.Count ?? 0;
        }

        public IGameEvent Latest
        {
            get
            {
                var pendingIds = _ws.PendingEventIds;
                if (pendingIds == null || pendingIds.Count == 0) return null;
                var lastId = pendingIds[pendingIds.Count - 1];
                if (_ws.EventCache != null && _ws.EventCache.TryGetValue(lastId, out var json))
                    return _serializer.DeserializeEvent(json);
                return null;
            }
        }

        public int TotalAppended { get; private set; }

        public IGameEvent GetById(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            if (_ws.EventCache != null && _ws.EventCache.TryGetValue(eventId, out var json))
                return _serializer.DeserializeEvent(json);
            return null;
        }

        public int PendingCount => _ws.PendingEventIds?.Count ?? 0;

        public int TotalImportance => _ws.PendingImportance;

        public IReadOnlyList<IGameEvent> DrainPending()
        {
            var manager = _getManager();
            if (manager != null)
                return manager.DrainPendingEvents(_ws.Id);
            return new List<IGameEvent>();
        }
    }
}
