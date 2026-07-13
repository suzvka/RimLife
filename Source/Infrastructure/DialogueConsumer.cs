using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Script;
using NPCLife.Infrastructure;
using NPCLife.Workspace;
using RimLife.Infrastructure.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using ILogger = NPCLife.Framework.ILogger; // 消除与 UnityEngine.ILogger 的歧义

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 台词消费者 + 时间调度器。实现 IScriptConsumer 接收框架推送的台词，
    /// 并按 RelativeTime 延迟消费（基于现实时间而非游戏 tick），将 Dialogue 行写入 RimWorld 的 PlayLog。
    /// 
    /// 职责：
    /// 1. 接收 OnScriptLinesReady 回调，将台词加入调度队列
    /// 2. 每帧 Tick 检查队列，按累积现实秒数到达时间的行立即消费
    /// 3. 为说话者写入闲聊类型 LogEntry_Dialogue，为听众写入 LogEntry_HeardDialogue
    /// 4. 整轮消费完毕后释放 ScriptLines 内存
    /// </summary>
    public class DialogueConsumer : IScriptConsumer, IDisposable
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        // 调度队列：按现实秒排序
        private readonly PriorityQueue<ScheduledLine, float> _queue = new PriorityQueue<ScheduledLine, float>();

        // 每个工作空间的时钟基准
        private readonly Dictionary<string, WorkspaceClock> _clocks = new Dictionary<string, WorkspaceClock>();

        // 已消费的轮次追踪（用于清理）
        private readonly HashSet<string> _consumedRounds = new HashSet<string>();

        // 现实时间累积器：从游戏 tick 换算的现实秒数（暂停时冻结）
        private float _accumulatedSec;
        private int _lastTick;

        private bool _disposed;

        public DialogueConsumer(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger;
        }

        // ================================================================
        // IScriptConsumer 实现
        // ================================================================

        /// <summary>
        /// 框架推送一轮台词。将每行按现实时间（accumulatedSec + RelativeTime）加入调度队列。
        /// 此方法由 ScriptDeliveryService 通过 MainThreadDispatcher 调用，保证主线程安全。
        /// </summary>
        public void OnScriptLinesReady(string workspaceId, int roundSeq, IReadOnlyList<ScriptLine> lines)
        {
            if (_disposed || lines == null || lines.Count == 0) return;

            try
            {
                // 获取或创建工作空间时钟（以当前累积现实秒为基准）
                if (!_clocks.TryGetValue(workspaceId, out var clock))
                {
                    clock = new WorkspaceClock { BaseTime = _accumulatedSec };
                    _clocks[workspaceId] = clock;
                }

                // 按现实秒调度每行：BaseTime + RelativeTime（LLM 输出的 RelativeTime 单位为现实秒）
                foreach (var line in lines)
                {
                    float scheduledTime = clock.BaseTime + line.RelativeTime;

                    var scheduled = new ScheduledLine
                    {
                        Line = line,
                        ScheduledTime = scheduledTime,
                        WorkspaceId = workspaceId,
                        RoundSeq = roundSeq
                    };

                    _queue.Enqueue(scheduled, scheduledTime);
                }

                // 更新时钟基准：以本轮最后一行的时间作为新基准（确保连续轮次不过度重叠）
                float maxRelativeTime = lines.Max(l => l.RelativeTime);
                clock.BaseTime += maxRelativeTime;

                _logger?.Message($"[RimLife.Dialogue] Scheduled {lines.Count} lines for workspace={workspaceId}, round={roundSeq}");
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[RimLife.Dialogue] OnScriptLinesReady error: {ex.Message}");
            }
        }

        // ================================================================
        // 每帧调度
        // ================================================================

        /// <summary>
        /// 每帧调用。计算本帧经过的现实秒数（暂停时冻结），消费已到达时间的台词行。
        /// 现实秒换算公式与 TickTimerPulses 保持一致：deltaSec = deltaTicks / (60f × speedMult)
        /// 由 RimWorldAgentDriver.GameComponentUpdate 调用。
        /// </summary>
        public void Tick()
        {
            if (_disposed || _queue.Count == 0) return;

            try
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                int deltaTicks = currentTick - _lastTick;
                _lastTick = currentTick;

                if (deltaTicks <= 0) return; // 暂停 / 首帧

                // 将游戏 tick 换算为现实秒，累加到全局时钟
                float speedMult = GetCurrentSpeedMultiplier();
                float deltaSec = deltaTicks / (60f * speedMult);
                _accumulatedSec += deltaSec;

                // 消费所有已到达现实时间的行
                while (_queue.Count > 0 && _queue.Peek().ScheduledTime <= _accumulatedSec)
                {
                    var scheduled = _queue.Dequeue();
                    ProcessLine(scheduled);

                    // 检查本轮是否全部消费完毕（队列中没有同轮次的行了）
                    MarkRoundIfConsumed(scheduled.WorkspaceId, scheduled.RoundSeq);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[RimLife.Dialogue] Tick error: {ex.Message}");
            }
        }

        // ================================================================
        // 内部处理
        // ================================================================

        private void ProcessLine(ScheduledLine scheduled)
        {
            var line = scheduled.Line;

            // 仅处理 Dialogue 类型（Pause/Narration/Action 仅推进时间）
            if (line.Type != ScriptLineType.Dialogue) return;

            if (string.IsNullOrEmpty(line.SpeakerId)) return;

            var speaker = PawnQueryHelper.FindPawnById(line.SpeakerId);
            if (speaker == null)
            {
                _logger?.Warning($"[RimLife.Dialogue] Speaker not found: {line.SpeakerId}");
                return;
            }

            // 写入说话者的闲聊类型日志条目（标记 InteractionDef = "Chat"）
            Find.PlayLog?.Add(new LogEntry_Dialogue(speaker, line.Text));

            // 为工作空间中的所有参与角色写入"听见"条目
            var ws = _getWorkspaceManager()?.Get(scheduled.WorkspaceId);
            var participantIds = ws?.FocusCharacterIds;
            if (participantIds != null)
            {
                foreach (var pid in participantIds)
                {
                    if (pid == line.SpeakerId) continue; // 说话者自己不需要"听见"条目

                    var listener = PawnQueryHelper.FindPawnById(pid);
                    if (listener == null) continue;

                    Find.PlayLog?.Add(new LogEntry_HeardDialogue(listener, speaker, line.Text));
                }
            }
        }

        private void MarkRoundIfConsumed(string workspaceId, int roundSeq)
        {
            // 检查队列中是否还有同一轮次的行
            // 由于 PriorityQueue 不支持遍历，我们用一个简单的方法：
            // 当本行是最后一行时（通过检查队列中是否还有相同 workspace+round 的行）
            
            bool hasMore = false;
            foreach (var item in _queue)
            {
                if (item.WorkspaceId == workspaceId && item.RoundSeq == roundSeq)
                {
                    hasMore = true;
                    break;
                }
            }

            if (!hasMore)
            {
                string key = $"{workspaceId}:{roundSeq}";
                if (_consumedRounds.Add(key))
                {
                    // 释放 ScriptLines 内存
                    DiscardRoundScriptLines(workspaceId, roundSeq);
                    _logger?.Message($"[RimLife.Dialogue] Round consumed: workspace={workspaceId}, round={roundSeq}");
                }
            }
        }

        private void DiscardRoundScriptLines(string workspaceId, int roundSeq)
        {
            try
            {
                var ws = _getWorkspaceManager()?.Get(workspaceId);
                ws?.DiscardScriptLines(roundSeq);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[RimLife.Dialogue] DiscardScriptLines failed: {ex.Message}");
            }
        }

        private float GetCurrentSpeedMultiplier()
        {
            if (Find.TickManager == null) return 1f;
            switch (Find.TickManager.CurTimeSpeed)
            {
                case TimeSpeed.Normal: return 1f;
                case TimeSpeed.Fast: return 3f;
                case TimeSpeed.Superfast: return 6f;
                case TimeSpeed.Ultrafast: return 15f;
                default: return 1f;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Clear();
            _clocks.Clear();
            _consumedRounds.Clear();
            _accumulatedSec = 0f;
            _lastTick = 0;
        }

        // ================================================================
        // 内部类型
        // ================================================================

        private struct ScheduledLine
        {
            public ScriptLine Line;
            public float ScheduledTime; // 现实秒
            public string WorkspaceId;
            public int RoundSeq;
        }

        private class WorkspaceClock
        {
            public float BaseTime; // 现实秒基准
        }
    }

    /// <summary>
    /// .NET Framework 4.8 没有内置 PriorityQueue，提供一个简单实现。
    /// 基于 List + 排序，支持重复优先级。
    /// </summary>
    internal class PriorityQueue<T, TPriority> where TPriority : IComparable<TPriority>
    {
        private readonly List<(TPriority priority, int seq, T item)> _list = new List<(TPriority, int, T)>();
        private int _seq; // 序列号，保证相同优先级时 FIFO 顺序

        public int Count => _list.Count;

        public void Enqueue(T item, TPriority priority)
        {
            _list.Add((priority, _seq++, item));
            _list.Sort((a, b) =>
            {
                int cmp = a.priority.CompareTo(b.priority);
                return cmp != 0 ? cmp : a.seq.CompareTo(b.seq);
            });
        }

        public T Peek()
        {
            if (_list.Count == 0) throw new InvalidOperationException("Queue is empty");
            return _list[0].item;
        }

        public T Dequeue()
        {
            if (_list.Count == 0) throw new InvalidOperationException("Queue is empty");
            var item = _list[0].item;
            _list.RemoveAt(0);
            return item;
        }

        public void Clear()
        {
            _list.Clear();
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var entry in _list)
                yield return entry.item;
        }
    }
}
