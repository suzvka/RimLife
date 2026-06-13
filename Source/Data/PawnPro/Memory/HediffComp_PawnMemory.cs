using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimLife.Cards;
using RimLife.Framework;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Pawn 个体记忆 HediffComp。作为隐藏 Hediff 挂载在每个 Pawn 上，
    /// 通过 Scribe 自动持久化四区记忆：STM、LTM、短期回顾、即时心境。
    /// 
    /// 生命周期由 Hediff 管理：
    /// - 添加：Pawn spawn 时自动附加（Harmony patch）
    /// - 序列化：CompExposeData() 由 Scribe 自动调用
    /// - 清理：Pawn 销毁时随 Hediff 移除
    /// </summary>
    public class HediffComp_PawnMemory : HediffComp
    {
        /// <summary>距上次巩固的最小 tick 间隔（24h 强制触发）。</summary>
        public const int ConsolidationIntervalTicks = 60000;

        /// <summary>睡眠触发巩固所需的最小连续睡眠 tick（3h）。</summary>
        public const int SleepConsolidationThresholdTicks = 7500;

        // ---- 序列化字段 ----
        private List<ShortTermMemory> _shortTerm = new List<ShortTermMemory>();
        private List<LongTermMemory> _longTerm = new List<LongTermMemory>();
        private ShortTermReview _review;
        private CurrentMindset _mindset;
        private int _lastConsolidationTick;
        private int _consecutiveSleepTicks;

        // 异步巩固防重入
        private bool _consolidationPending;

        // ---- 公共属性（只读视图） ----
        public IReadOnlyList<ShortTermMemory> ShortTermMemories => _shortTerm;
        public IReadOnlyList<LongTermMemory> LongTermMemories => _longTerm;
        public ShortTermReview Review => _review;
        public CurrentMindset Mindset => _mindset;
        public int LastConsolidationTick => _lastConsolidationTick;

        // ================================================================
        // Scribe 序列化
        // ================================================================

        public override void CompExposeData()
        {
            base.CompExposeData();

            // ---- 短期记忆列表 ----
            ExposeShortTermList();

            // ---- 长期记忆列表 ----
            ExposeLongTermList();

            // ---- 短期回顾 ----
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int reviewTick = _review?.LastUpdateTick ?? 0;
                string reviewContent = _review?.Content ?? "";
                Scribe_Values.Look(ref reviewTick, "reviewTick", 0);
                Scribe_Values.Look(ref reviewContent, "reviewContent", "");
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                int reviewTick = 0;
                string reviewContent = "";
                Scribe_Values.Look(ref reviewTick, "reviewTick", 0);
                Scribe_Values.Look(ref reviewContent, "reviewContent", "");
                if (!string.IsNullOrEmpty(reviewContent))
                    _review = new ShortTermReview(reviewTick, reviewContent);
            }

            // ---- 即时心境 ----
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int mindsetTick = _mindset?.LastUpdateTick ?? 0;
                string mindsetContent = _mindset?.Content ?? "";
                Scribe_Values.Look(ref mindsetTick, "mindsetTick", 0);
                Scribe_Values.Look(ref mindsetContent, "mindsetContent", "");
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                int mindsetTick = 0;
                string mindsetContent = "";
                Scribe_Values.Look(ref mindsetTick, "mindsetTick", 0);
                Scribe_Values.Look(ref mindsetContent, "mindsetContent", "");
                if (!string.IsNullOrEmpty(mindsetContent))
                    _mindset = new CurrentMindset(mindsetTick, mindsetContent);
            }

            Scribe_Values.Look(ref _lastConsolidationTick, "lastConsolidationTick", 0);
            Scribe_Values.Look(ref _consecutiveSleepTicks, "consecutiveSleepTicks", 0);
        }

        private void ExposeShortTermList()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int stmCount = _shortTerm?.Count ?? 0;
                Scribe_Values.Look(ref stmCount, "stmCount", 0);
                if (_shortTerm != null)
                {
                    for (int i = 0; i < _shortTerm.Count; i++)
                    {
                        var stm = _shortTerm[i];
                        Scribe_Values.Look(ref stm.Tick, $"stm_tick_{i}", 0);
                        Scribe_Values.Look(ref stm.Type, $"stm_type_{i}", "Observation");
                        Scribe_Values.Look(ref stm.Summary, $"stm_summary_{i}", "");
                        Scribe_Values.Look(ref stm.RelatedPawnId, $"stm_related_{i}", null);
                    }
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                int stmCount = 0;
                Scribe_Values.Look(ref stmCount, "stmCount", 0);
                _shortTerm = new List<ShortTermMemory>(stmCount);
                for (int i = 0; i < stmCount; i++)
                {
                    var stm = new ShortTermMemory();
                    Scribe_Values.Look(ref stm.Tick, $"stm_tick_{i}", 0);
                    Scribe_Values.Look(ref stm.Type, $"stm_type_{i}", "Observation");
                    Scribe_Values.Look(ref stm.Summary, $"stm_summary_{i}", "");
                    Scribe_Values.Look(ref stm.RelatedPawnId, $"stm_related_{i}", null);
                    _shortTerm.Add(stm);
                }
            }
        }

        private void ExposeLongTermList()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int ltmCount = _longTerm?.Count ?? 0;
                Scribe_Values.Look(ref ltmCount, "ltmCount", 0);
                if (_longTerm != null)
                {
                    for (int i = 0; i < _longTerm.Count; i++)
                    {
                        var ltm = _longTerm[i];
                        Scribe_Values.Look(ref ltm.ConsolidatedTick, $"ltm_tick_{i}", 0);
                        Scribe_Values.Look(ref ltm.Topic, $"ltm_topic_{i}", "");
                        Scribe_Values.Look(ref ltm.Summary, $"ltm_summary_{i}", "");

                        string relStr = ltm.RelatedPawnIds != null ? string.Join("\x1F", ltm.RelatedPawnIds) : "";
                        Scribe_Values.Look(ref relStr, $"ltm_rel_{i}", "");
                    }
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                int ltmCount = 0;
                Scribe_Values.Look(ref ltmCount, "ltmCount", 0);
                _longTerm = new List<LongTermMemory>(ltmCount);
                for (int i = 0; i < ltmCount; i++)
                {
                    var ltm = new LongTermMemory();
                    Scribe_Values.Look(ref ltm.ConsolidatedTick, $"ltm_tick_{i}", 0);
                    Scribe_Values.Look(ref ltm.Topic, $"ltm_topic_{i}", "");
                    Scribe_Values.Look(ref ltm.Summary, $"ltm_summary_{i}", "");

                    string relStr = "";
                    Scribe_Values.Look(ref relStr, $"ltm_rel_{i}", "");
                    ltm.RelatedPawnIds = string.IsNullOrEmpty(relStr)
                        ? new List<string>()
                        : new List<string>(relStr.Split('\x1F'));

                    _longTerm.Add(ltm);
                }
            }
        }

        // ================================================================
        // 短期记忆操作
        // ================================================================

        /// <summary>
        /// 追加一条短期记忆。STM 由巩固清空，无上限风险。
        /// </summary>
        public void AddShortTerm(ShortTermMemory memory)
        {
            if (memory == null) return;
            _shortTerm.Add(memory);
        }

        /// <summary>
        /// 批量追加短期记忆。
        /// </summary>
        public void AddShortTermRange(IEnumerable<ShortTermMemory> memories)
        {
            if (memories == null) return;
            foreach (var m in memories)
                AddShortTerm(m);
        }

        // ================================================================
        // 即时心境写入（由 MCP 工具调用）
        // ================================================================

        /// <summary>
        /// 更新即时心境。这是心境凌驾层的唯一写入入口，
        /// 由 LLM 通过 MCP 工具主动调用。
        /// </summary>
        /// <param name="content">第一人称心境描述（≤200 字）。</param>
        /// <param name="currentTick">当前游戏 tick。</param>
        public void UpdateMindset(string content, int currentTick)
        {
            string truncated = content ?? "";
            if (truncated.Length > 200)
                truncated = truncated.Substring(0, 197) + "…";

            _mindset = new CurrentMindset(currentTick, truncated);
        }

        // ================================================================
        // 记忆巩固
        // ================================================================

        /// <summary>
        /// 尝试触发记忆巩固。
        /// Phase 1（同步）：构建巩固请求。
        /// Phase 2（异步）：调用重写器，完成后在主线程回写结果。
        /// </summary>
        /// <param name="currentTick">当前游戏 tick。</param>
        /// <param name="isFromSleep">是否由睡眠触发。</param>
        /// <returns>是否发起了巩固（异步发起即返回 true）。</returns>
        public bool TryConsolidate(int currentTick, bool isFromSleep)
        {
            if (_shortTerm.Count == 0)
                return false;

            if (!isFromSleep && currentTick - _lastConsolidationTick < ConsolidationIntervalTicks)
                return false;

            if (_consolidationPending)
                return false;

            // Phase 1：构建巩固请求（同步）
            string pawnName = Pawn?.Name?.ToStringShort ?? Pawn?.LabelShort ?? "?";
            var request = MemoryConsolidator.BuildRequest(
                _shortTerm, _longTerm, currentTick, pawnName);

            if (request == null)
                return false;

            // Phase 2：发起异步重写
            _consolidationPending = true;
            int stmCount = _shortTerm.Count;

            // 在 Task 中执行异步重写
            var task = MemoryConsolidator.RewriteAsync(request);
            task.ContinueWith(t =>
            {
                // 通过主线程调度器回写结果
                MainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        if (t.IsFaulted || t.Result == null)
                        {
                            Log.Warning($"[RimLife.PawnMemory] Consolidation rewrite failed for pawn {Pawn?.ThingID}");
                        }
                        else
                        {
                            ApplyRewriteResult(t.Result);
                        }
                    }
                    finally
                    {
                        _consolidationPending = false;
                    }
                });
            }, TaskScheduler.Default);

            Log.Message($"[RimLife.PawnMemory] Consolidation initiated: {stmCount} STM for pawn {Pawn?.ThingID} (sleep={isFromSleep})");
            return true;
        }

        /// <summary>
        /// 将重写结果应用到存储。在主线程上调用。
        /// </summary>
        private void ApplyRewriteResult(MemoryRewriteResult result)
        {
            if (result.UpdatedLtm != null)
                _longTerm = result.UpdatedLtm;

            if (result.Review != null)
                _review = result.Review;

            _shortTerm.Clear();
            _lastConsolidationTick = result.Review?.LastUpdateTick
                ?? (Find.TickManager?.TicksGame ?? 0);
            _consecutiveSleepTicks = 0;

            Log.Message($"[RimLife.PawnMemory] Consolidation applied: LTM={_longTerm.Count} for pawn {Pawn?.ThingID}");
        }

        /// <summary>
        /// 通知 comp 当前 tick 的睡眠状态，用于累积睡眠 tick。
        /// 由外部 GameComponent 每 tick 调用。
        /// </summary>
        public void NotifySleepTick(bool isSleeping, int currentTick)
        {
            if (isSleeping)
            {
                _consecutiveSleepTicks++;

                if (_consecutiveSleepTicks >= SleepConsolidationThresholdTicks)
                {
                    TryConsolidate(currentTick, isFromSleep: true);
                }
            }
            else
            {
                _consecutiveSleepTicks = 0;
            }

            // 24h 强制触发
            if (currentTick - _lastConsolidationTick >= ConsolidationIntervalTicks)
            {
                TryConsolidate(currentTick, isFromSleep: false);
            }
        }

        // ================================================================
        // 长期记忆查询
        // ================================================================

        /// <summary>
        /// 按主题标签筛选长期记忆。
        /// </summary>
        public IReadOnlyList<LongTermMemory> QueryByTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return _longTerm;
            return _longTerm.Where(ltm => ltm.Topic == topic).ToList();
        }

        /// <summary>
        /// 按关联角色筛选长期记忆。
        /// </summary>
        public IReadOnlyList<LongTermMemory> QueryByRelatedPawn(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId)) return _longTerm;
            return _longTerm.Where(ltm => ltm.RelatedPawnIds != null
                && ltm.RelatedPawnIds.Contains(pawnId)).ToList();
        }

        /// <summary>
        /// 获取最近的短期记忆（用于注入 prompt）。
        /// </summary>
        /// <param name="count">最大返回数。</param>
        public IReadOnlyList<ShortTermMemory> GetRecentMemories(int count = 10)
        {
            return _shortTerm
                .OrderByDescending(m => m.Tick)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// 获取长期记忆列表，按 Topic 分组返回（用于注入 prompt）。
        /// </summary>
        /// <param name="count">最大返回数。</param>
        public IReadOnlyList<LongTermMemory> GetKeyMemories(int count)
        {
            return _longTerm
                .OrderByDescending(m => m.ConsolidatedTick)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// 获取记忆概览快照（用于注入 CharacterCard）。
        /// 层级：CurrentMindset（凌驾层）→ ShortTermReview → STM/LTM 摘要。
        /// </summary>
        /// <param name="currentTick">当前游戏 tick。</param>
        public MemorySnapshot CreateSnapshot(int currentTick)
        {
            return new MemorySnapshot
            {
                CurrentMindset = _mindset?.Content,
                ShortTermReview = _review?.Content,
                RecentMemories = GetRecentMemories(10)
                    .Select(m => m.TruncatedSummary(120))
                    .ToList(),
                KeyMemories = GetKeyMemories(5)
                    .Select(m => m.TruncatedSummary(300))
                    .ToList(),
                ShortTermCount = _shortTerm.Count,
                LongTermCount = _longTerm.Count,
                LastConsolidationTick = _lastConsolidationTick
            };
        }

        public override string CompDebugString()
        {
            return $"[PawnMemory] STM={_shortTerm.Count} LTM={_longTerm.Count} " +
                   $"Review={(_review != null ? "yes" : "no")} Mindset={(_mindset != null ? "yes" : "no")} " +
                   $"lastConsolidation={_lastConsolidationTick}";
        }
    }
}
