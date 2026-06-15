using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Script;
using System;
using System.Collections.Generic;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 台词推送服务。订阅 EventBus 的 script.ready 事件，
    /// 解析占位符后通过 MainThreadDispatcher 投递到游戏侧 IScriptConsumer。
    ///
    /// 职责边界：
    ///   - 从工作空间取出解析好的 ScriptLine 列表
    ///   - 调用 IScriptLineResolver 填充 SpeakerName
    ///   - 通过 MainThreadDispatcher 回调 IScriptConsumer
    ///   - 不关心台词格式（格式是 ScriptFormat 的事）
    ///   - 不关心时间轴（时间是游戏侧的事）
    /// </summary>
    internal class ScriptDeliveryService : IDisposable
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly Func<IScriptConsumer> _getConsumer;
        private readonly IScriptLineResolver _resolver;
        private readonly ILogger _logger;
        private readonly Action _unsubscribe;
        private bool _disposed;

        public ScriptDeliveryService(
            Func<IWorkspaceManager> getWorkspaceManager,
            Func<IScriptConsumer> getConsumer,
            IScriptLineResolver resolver,
            ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager
                ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _getConsumer = getConsumer
                ?? throw new ArgumentNullException(nameof(getConsumer));
            _resolver = resolver
                ?? throw new ArgumentNullException(nameof(resolver));
            _logger = logger;

            // 订阅 EventBus
            _unsubscribe = EventBus.Subscribe(FrameworkEvents.ScriptReady, OnScriptReady, priority: 50);
            _logger?.Message("[RimLife.ScriptDelivery] Initialized and subscribed to script.ready.");
        }

        // ================================================================
        // 事件处理
        // ================================================================

        private void OnScriptReady(EventArg args)
        {
            if (_disposed) return;

            try
            {
                if (args?.Payload == null) return;

                // 提取事件参数
                if (!args.Payload.TryGetValue("workspaceId", out var workspaceId)
                    || string.IsNullOrEmpty(workspaceId))
                {
                    _logger?.Warning("[RimLife.ScriptDelivery] script.ready event missing workspaceId.");
                    return;
                }

                args.Payload.TryGetValue("roundSeq", out var roundSeqStr);
                int.TryParse(roundSeqStr, out int roundSeq);

                // 查找工作空间
                var manager = _getWorkspaceManager();
                if (manager == null)
                {
                    _logger?.Warning("[RimLife.ScriptDelivery] WorkspaceManager unavailable.");
                    return;
                }

                var ws = manager.Get(workspaceId);
                if (ws == null)
                {
                    _logger?.Warning($"[RimLife.ScriptDelivery] Workspace '{workspaceId}' not found.");
                    return;
                }

                // 获取指定轮的 ScriptLines
                var rounds = ws.Rounds;
                Workspace.WorkspaceRound targetRound;
                if (rounds == null || rounds.Count == 0)
                {
                    _logger?.Warning($"[RimLife.ScriptDelivery] Workspace '{workspaceId}' has no rounds.");
                    return;
                }

                // 取最后一条匹配的轮次（seq 应一致，此处做防御）
                int foundIndex = -1;
                for (int i = rounds.Count - 1; i >= 0; i--)
                {
                    if (rounds[i].Seq == roundSeq)
                    {
                        foundIndex = i;
                        break;
                    }
                }
                if (foundIndex < 0)
                {
                    _logger?.Warning($"[RimLife.ScriptDelivery] Round seq={roundSeq} not found in workspace '{workspaceId}'.");
                    return;
                }

                targetRound = rounds[foundIndex];
                var scriptLines = targetRound.ScriptLines;
                if (scriptLines == null || scriptLines.Count == 0)
                {
                    _logger?.Message($"[RimLife.ScriptDelivery] Round seq={roundSeq} has no ScriptLines (narrative may be non-JSON or empty).");
                    return;
                }

                // 解析占位符（pawnId → 显示名）
                _resolver.Resolve(scriptLines);

                // 获取消费者并投递到主线程
                var consumer = _getConsumer();
                if (consumer == null)
                {
                    _logger?.Warning("[RimLife.ScriptDelivery] IScriptConsumer not registered.");
                    return;
                }

                MainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        consumer.OnScriptLinesReady(workspaceId, roundSeq, scriptLines);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"[RimLife.ScriptDelivery] Consumer error: {ex.Message}");
                    }
                });

                _logger?.Message($"[RimLife.ScriptDelivery] Delivered {scriptLines.Count} ScriptLines to consumer (workspace={workspaceId}, round={roundSeq}).");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[RimLife.ScriptDelivery] OnScriptReady error: {ex.Message}");
            }
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _unsubscribe?.Invoke();
            _logger?.Message("[RimLife.ScriptDelivery] Disposed.");
        }
    }
}
