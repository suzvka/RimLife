using NPCLife.Driver;
using RimLife.Mappers;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLifeCore 的定时器脉冲驱动部分。
    /// 基于游戏 tick 的现实秒积分算法，驱动导演和即兴编剧的定时器脉冲。
    /// </summary>
    public static partial class RimLifeCore
    {
        // ================================================================
        // 定时器脉冲驱动（基于游戏 tick 的现实秒积分算法）
        // ================================================================
        //
        // 三层解耦模型：
        //   游戏引擎 (TicksGame + CurTimeSpeed)
        //     → 适配层积分算法 (tick × coeff → 现实秒)
        //       → 框架脉冲累加器 (accum >= threshold → 触发)
        //
        // 积分公式: scorePerTick = 1.0 / (60 * speedMultiplier)
        // 确保 1 现实秒 = 1 积分，与游戏速度无关。

        /// <summary>上次帧的游戏 tick 快照，用于计算 deltaTicks。</summary>
        private static int _lastTicksGame;

        /// <summary>导演脉冲积分累加器（现实秒）。每帧累加 deltaTicks 换算的秒数。</summary>
        private static float _directorAccumSec;

        /// <summary>即兴编剧脉冲积分累加器（现实秒）。</summary>
        private static float _improviserAccumSec;

        /// <summary>是否已输出定时器配置摘要（仅首次打印防止日志洪水）。</summary>
        private static bool _timerConfigLogged;

        /// <summary>
        /// 每帧脉冲驱动函数。由 RimWorldAgentDriver.GameComponentUpdate() 调用。
        ///
        /// 算法：
        ///   1. 计算本帧经过的游戏 tick 数（deltaTicks）
        ///   2. 获取当前游戏速度倍率，按积分公式换算为现实秒
        ///   3. 累加到分角色蓄水池，超过阈值则注入 TimerPulse 事件
        ///   4. 使用 while 而非 if：高速下可能一帧跨越多区间，必须全部补上
        ///
        /// 暂停时 TicksGame 不变 → deltaTicks = 0 → 整个 Agent 集群自然休眠。
        /// </summary>
        public static void TickTimerPulses()
        {
            if (Workspaces == null) return;

            int currentTicks = Find.TickManager?.TicksGame ?? 0;
            int deltaTicks = currentTicks - _lastTicksGame;
            _lastTicksGame = currentTicks;

            if (deltaTicks <= 0) return; // 暂停 / 第一帧 / 读档后重置

            var dc = DriverConfig;
            if (dc == null) return;

            // 首次打印定时器配置摘要
            if (!_timerConfigLogged)
            {
                _timerConfigLogged = true;
                Logger?.Message($"[RimLife.Timing] Timer config: directorInterval={dc.DirectorTimerInterval}s, improviserInterval={dc.ImproviserTimerInterval}s, directorCountThreshold={dc.DirectorCountThreshold}, directorImportanceThreshold={dc.DirectorImportanceThreshold:F1}");
            }

            // 事件缓冲 flush 检查（空闲超时 → 批量推送到 EventPool）
            FlushEventBuffer(currentTicks);

            // 核心积分算法：1 现实秒 = 60 × speedMultiplier 个 tick
            float speedMult = GetCurrentSpeedMultiplier();
            float scorePerTick = 1f / (60f * speedMult);
            float addedScore = deltaTicks * scorePerTick;

            // 导演定时器（阈值由 DriverConfig 提供，单位为抽象积分；适配层映射为 1 现实秒 = 1 积分）
            int dirInterval = dc.GetTimerInterval(NPCLife.Workspace.WorkspaceRole.Director);
            if (dirInterval > 0)
            {
                _directorAccumSec += addedScore;
                float dirThreshold = dirInterval;
                float dirImportanceThreshold = dc.GetEffectiveImportanceThreshold(NPCLife.Workspace.WorkspaceRole.Director);
                while (_directorAccumSec >= dirThreshold)
                {
                    _directorAccumSec -= dirThreshold;
                    var directorWs = GetDirectorWorkspace();
                    if (directorWs != null)
                    {
                        var pulseEvt = EventCardMapper.CreateTimerPulse(
                            NPCLife.Workspace.WorkspaceRole.Director, currentTicks, dirImportanceThreshold);
                        directorWs.EventPool.Append(pulseEvt);
                        Logger?.Message($"[RimLife.Timing] TimerPulse injected (role=Director, interval={dirInterval}s, importance={dirImportanceThreshold:F1}, pending={directorWs.EventPool.PendingCount})");
                    }
                }
            }

            // 即兴编剧定时器
            int freeInterval = dc.GetTimerInterval(NPCLife.Workspace.WorkspaceRole.Improviser);
            if (freeInterval > 0)
            {
                _improviserAccumSec += addedScore;
                float freeThreshold = freeInterval;
                float freeImportanceThreshold = dc.GetEffectiveImportanceThreshold(NPCLife.Workspace.WorkspaceRole.Improviser);
                while (_improviserAccumSec >= freeThreshold)
                {
                    _improviserAccumSec -= freeThreshold;
                    var improviserWs = GetImproviserWorkspace();
                    if (improviserWs != null)
                    {
                        var pulseEvt = EventCardMapper.CreateTimerPulse(
                            NPCLife.Workspace.WorkspaceRole.Improviser, currentTicks, freeImportanceThreshold);
                        improviserWs.EventPool.Append(pulseEvt);
                        Logger?.Message($"[RimLife.Timing] TimerPulse injected (role=Improviser, interval={freeInterval}s, importance={freeImportanceThreshold:F1}, pending={improviserWs.EventPool.PendingCount})");
                    }
                }
            }
        }

        /// <summary>
        /// 每帧调用。驱动台词调度器，消费已到达时间的台词行。
        /// 由 RimWorldAgentDriver.GameComponentUpdate 调用。
        /// </summary>
        public static void TickDialogueScheduler()
        {
            _dialogueConsumer?.Tick();
        }

        /// <summary>
        /// 获取当前 RimWorld 游戏速度倍率（相对于 1×）。
        /// Normal=1, Fast=3, Superfast=6, Ultrafast=15, Paused/Unknown=1。
        /// </summary>
        private static float GetCurrentSpeedMultiplier()
        {
            if (Find.TickManager == null) return 1f;
            switch (Find.TickManager.CurTimeSpeed)
            {
                case TimeSpeed.Normal:    return 1f;
                case TimeSpeed.Fast:      return 3f;
                case TimeSpeed.Superfast: return 6f;
                case TimeSpeed.Ultrafast: return 15f;
                default:                  return 1f; // Paused
            }
        }

        /// <summary>
        /// 新游戏 / 读档时重置累加器，避免 deltaTicks 暴增导致脉冲风暴。
        /// 由 RimWorldAgentDriver.StartedNewGame / LoadedGame 调用。
        /// </summary>
        internal static void ResetTimerAccumulators()
        {
            _lastTicksGame = Find.TickManager?.TicksGame ?? 0;
            _directorAccumSec = 0f;
            _improviserAccumSec = 0f;
            _timerConfigLogged = false;
            _eventBuffer?.Reset();
        }

        // ================================================================
        // 事件缓冲 flush（空闲超时 / 强制排空）
        // ================================================================

        /// <summary>
        /// 将事件缓冲中的事件推送到导演工作空间的 EventPool。
        /// <paramref name="force"/> 为 true 时跳过空闲超时检查（存档前强制排空）。
        /// </summary>
        private static void FlushEventBuffer(int currentTick, bool force = false)
        {
            if (_eventBuffer == null || !_eventBuffer.HasEvents) return;
            if (!force && !_eventBuffer.ShouldFlush(currentTick)) return;

            var directorWs = GetDirectorWorkspace();
            if (directorWs == null)
            {
                Logger?.Warning("[RimLife.Core] FlushEventBuffer: DirectorWorkspace is null");
                return;
            }

            // 确保 Director Agent 已创建并订阅 OnThresholdReached，
            // 必须在 Append 之前完成，否则阈值回调会在无订阅者时触发而丢失。
            GetDirectorAgent();

            var events = _eventBuffer.Drain();
            foreach (var evt in events)
                directorWs.EventPool.Append(evt);

            Logger?.Message($"[RimLife.Core] EventBuffer flushed: {events.Count} events → Director workspace (pending={directorWs.EventPool.PendingCount}, importance={directorWs.EventPool.TotalImportance:F1})");
        }

        /// <summary>
        /// 获取导演脉冲积分累加器的当前值（现实秒）。
        /// 由 MCP 工具 / 调试面板暴露给 Agent 查询。
        /// </summary>
        public static float GetTimerPulseAccumulator()
        {
            return _directorAccumSec;
        }

        /// <summary>
        /// 获取指定角色的定时器脉冲间隔（现实秒）。0 表示禁用。
        /// </summary>
        public static int GetTimerPulseInterval(NPCLife.Workspace.WorkspaceRole role)
        {
            return DriverConfig?.GetTimerInterval(role) ?? 0;
        }
    }
}
