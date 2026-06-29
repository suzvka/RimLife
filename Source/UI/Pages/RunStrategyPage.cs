using NPCLife.Driver;
using NPCLife.Framework;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 运行策略页面：Agent 触发阈值、定时器脉冲间隔与诊断开关。
    /// 合并原叙事页（Agent 驱动参数）和高级页（诊断配置），移除所有功能开关。
    /// 功能开关已永久启用——任何一个关闭都会导致整个系统无法运行。
    /// </summary>
    public class RunStrategyPage : IConfigPage
    {
        public string Id => "run_strategy";
        public string Label => "运行策略";
        public string Group => "核心";
        public int Order => 2;

        // 本地编辑缓冲（首次 Draw 从配置初始化）
        private bool _initialized;

        // ---- Agent 触发阈值（原 NarrativePage） ----

        // 导演专用
        private int _directorCountThreshold;
        private int _directorImportanceThreshold;
        private int _directorTimerInterval;

        // 即兴编剧专用
        private int _improviserCountThreshold;
        private int _improviserImportanceThreshold;
        private int _improviserTimerInterval;

        // 剧情编剧专用
        private int _screenwriterCountThreshold;
        private int _screenwriterImportanceThreshold;

        // 通用
        private int _recentHistoryCapacity;
        private int _maxAgentRounds;

        // ---- 诊断开关（原 AdvancedPage） ----
        private bool _enableVerboseLogging;
        private bool _enableToolCallTracing;
        private bool _enableEventTracing;

        // 保存反馈
        private string _statusMessage;
        private float _statusMessageTime;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            InitializeIfNeeded();

            // ================================================================
            // 导演策略
            // ================================================================
            BeginSection(listing, "导演策略");

            listing.Gap(GapTiny);

            DrawLabeledIntRow(listing, "导演专用事件数阈值:", ref _directorCountThreshold, 1, 999,
                "pending 事件数达到此值时触发导演 Agent");
            DrawLabeledIntRow(listing, "导演专用重要度阈值:", ref _directorImportanceThreshold, 1, 999,
                "pending 事件总重要度达到此值时触发导演 Agent");
            DrawLabeledIntRow(listing, "导演定时器间隔 (秒):", ref _directorTimerInterval, 0, 999999,
                "0 = 禁用定时器；每 N 现实秒注入一个 TimerPulse 事件（与游戏速度无关）");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ================================================================
            // 即兴编剧策略
            // ================================================================
            BeginSection(listing, "即兴编剧策略");

            DrawLabeledIntRow(listing, "即兴编剧专用事件数阈值:", ref _improviserCountThreshold, 1, 999,
                "pending 事件数达到此值时触发即兴编剧 Agent");
            DrawLabeledIntRow(listing, "即兴编剧专用重要度阈值:", ref _improviserImportanceThreshold, 1, 999,
                "pending 事件总重要度达到此值时触发即兴编剧 Agent");
            DrawLabeledIntRow(listing, "即兴编剧定时器间隔 (秒):", ref _improviserTimerInterval, 0, 999999,
                "0 = 禁用定时器；每 N 现实秒注入一个 TimerPulse 事件（与游戏速度无关）");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ================================================================
            // 剧情编剧策略
            // ================================================================
            BeginSection(listing, "剧情编剧策略");

            DrawLabeledIntRow(listing, "剧情编剧专用事件数阈值:", ref _screenwriterCountThreshold, 1, 999,
                "pending 事件数达到此值时触发编剧 Agent；剧情编剧创作叙事内容，无定时器");
            DrawLabeledIntRow(listing, "剧情编剧专用重要度阈值:", ref _screenwriterImportanceThreshold, 1, 999,
                "pending 事件总重要度达到此值时触发编剧 Agent");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ================================================================
            // 通用设置
            // ================================================================
            BeginSection(listing, "通用设置");

            DrawLabeledIntRow(listing, "历史缓冲区容量:", ref _recentHistoryCapacity, 10, 9999,
                "保留在内存中的近期事件数量上限");
            DrawLabeledIntRow(listing, "Agent 最大轮数:", ref _maxAgentRounds, 1, 100,
                "单次激活中工具调用的最大轮数（防死循环）");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ================================================================
            // 诊断
            // ================================================================
            BeginSection(listing, "诊断");
            listing.CheckboxLabeled("详细日志", ref _enableVerboseLogging, "输出完整 LLM 请求/响应到日志");
            listing.CheckboxLabeled("工具调用追踪", ref _enableToolCallTracing, "记录每次工具调用的参数和结果");
            listing.CheckboxLabeled("事件总线追踪", ref _enableEventTracing, "记录所有事件发布/订阅轨迹");
            EndSection(listing);

            // ================================================================
            // 操作按钮
            // ================================================================
            var btnResults = DrawButtonRow(listing,
                new[] { "保存并应用", "重置默认值" },
                new[] { BtnWidthLarge, BtnWidthMedium });

            if (btnResults[0])
            {
                // 构建 DriverConfig
                var dc = new DriverConfig
                {
                    DirectorCountThreshold = _directorCountThreshold,
                    DirectorImportanceThreshold = _directorImportanceThreshold,
                    DirectorTimerInterval = _directorTimerInterval,
                    ImproviserCountThreshold = _improviserCountThreshold,
                    ImproviserImportanceThreshold = _improviserImportanceThreshold,
                    ImproviserTimerInterval = _improviserTimerInterval,
                    ScreenwriterCountThreshold = _screenwriterCountThreshold,
                    ScreenwriterImportanceThreshold = _screenwriterImportanceThreshold,
                    RecentHistoryCapacity = _recentHistoryCapacity,
                    MaxAgentRounds = _maxAgentRounds,
                };

                // 先保存 DriverConfig（独立缓存键，用于单独加载 Driver）
                RimLifeCore.SetDriverConfig(dc);

                // 克隆当前 FrameworkConfig 并注入新 Driver，确保 Configure 不会用旧 Driver 覆盖
                var config = CloneCurrentConfig();
                if (config != null)
                {
                    config.Driver = dc;
                    config.Diagnostics.EnableVerboseLogging = _enableVerboseLogging;
                    config.Diagnostics.EnableToolCallTracing = _enableToolCallTracing;
                    config.Diagnostics.EnableEventTracing = _enableEventTracing;
                    RimLifeCore.Configure(config);
                }

                RimLifeCore.RebuildAgents();
                _statusMessage = "已保存并重建 Agent";
                _statusMessageTime = Time.time;
                Log.Message($"[RimLife.UI] Run strategy saved (timerInterval={_directorTimerInterval}s, countThreshold={_directorCountThreshold})");
            }

            if (btnResults[1])
            {
                var defaultDc = DriverConfig.CreateDefault();
                _directorCountThreshold = defaultDc.DirectorCountThreshold;
                _directorImportanceThreshold = (int)defaultDc.DirectorImportanceThreshold;
                _directorTimerInterval = defaultDc.DirectorTimerInterval;
                _improviserCountThreshold = defaultDc.ImproviserCountThreshold;
                _improviserImportanceThreshold = (int)defaultDc.ImproviserImportanceThreshold;
                _improviserTimerInterval = defaultDc.ImproviserTimerInterval;
                _screenwriterCountThreshold = defaultDc.ScreenwriterCountThreshold;
                _screenwriterImportanceThreshold = (int)defaultDc.ScreenwriterImportanceThreshold;
                _recentHistoryCapacity = defaultDc.RecentHistoryCapacity;
                _maxAgentRounds = defaultDc.MaxAgentRounds;
                _enableVerboseLogging = false;
                _enableToolCallTracing = false;
                _enableEventTracing = false;
                _statusMessage = "已重置（需保存生效）";
                _statusMessageTime = Time.time;
            }

            // 状态消息（统一淡出效果）
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
        }

        private void InitializeIfNeeded()
        {
            if (_initialized) return;
            _initialized = true;

            // DriverConfig
            var dc = RimLifeCore.DriverConfig;
            _directorCountThreshold = dc.DirectorCountThreshold;
            _directorImportanceThreshold = (int)dc.DirectorImportanceThreshold;
            _directorTimerInterval = dc.DirectorTimerInterval;
            _improviserCountThreshold = dc.ImproviserCountThreshold;
            _improviserImportanceThreshold = (int)dc.ImproviserImportanceThreshold;
            _improviserTimerInterval = dc.ImproviserTimerInterval;
            _screenwriterCountThreshold = dc.ScreenwriterCountThreshold;
            _screenwriterImportanceThreshold = (int)dc.ScreenwriterImportanceThreshold;
            _recentHistoryCapacity = dc.RecentHistoryCapacity;
            _maxAgentRounds = dc.MaxAgentRounds;

            // Diagnostics
            var d = RimLifeCore.Config.Diagnostics;
            _enableVerboseLogging = d?.EnableVerboseLogging ?? false;
            _enableToolCallTracing = d?.EnableToolCallTracing ?? false;
            _enableEventTracing = d?.EnableEventTracing ?? false;
        }

        // ================================================================
        // 配置克隆
        // ================================================================

        private static FrameworkConfig CloneCurrentConfig()
        {
            try
            {
                var json = RimLifeCore.Config.ToJson();
                return FrameworkConfig.FromJson(json);
            }
            catch
            {
                return null;
            }
        }

        // ================================================================
        // UI 辅助
        // ================================================================

        /// <summary>
        /// 绘制标签+整数输入+描述的复合行。
        /// </summary>
        private static void DrawLabeledIntRow(Listing_Standard listing, string label, ref int value, int min, int max, string description)
        {
            var labelRect = listing.GetRect(20f);
            Widgets.Label(labelRect, label);

            var inputRect = listing.GetRect(24f);
            var fieldWidth = 100f;
            DrawIntField(new Rect(inputRect.x + 4f, inputRect.y, fieldWidth, inputRect.height), ref value, min, max);

            if (!string.IsNullOrEmpty(description))
            {
                var descRect = new Rect(inputRect.x + 4f + fieldWidth + 8f, inputRect.y, inputRect.width - fieldWidth - 8f - 4f, inputRect.height);
                Widgets.Label(descRect, $"<color=#888888><size=11>{description}</size></color>");
            }

            listing.Gap(2f);
        }

        private static void DrawIntField(Rect rect, ref int value, int min, int max)
        {
            var btnW = 24f;
            var textW = rect.width - btnW * 2 - 4f;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, btnW, rect.height), "-"))
                value = Mathf.Max(min, value - 1);

            var text = value.ToString();
            var newText = Widgets.TextField(new Rect(rect.x + btnW + 2f, rect.y, textW, rect.height), text);
            if (newText != text && int.TryParse(newText, out int parsed))
                value = Mathf.Clamp(parsed, min, max);

            if (Widgets.ButtonText(new Rect(rect.x + btnW + textW + 4f, rect.y, btnW, rect.height), "+"))
                value = Mathf.Min(max, value + 1);
        }
    }
}
