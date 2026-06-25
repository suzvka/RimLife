using NPCLife.Driver;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 叙事页面：Agent 驱动参数配置。
    /// 支持分角色（导演/剧情编剧/临时编剧）触发阈值和定时器脉冲间隔。
    /// 所有控件直接读写真实配置，保存后重建 Agent 生效。
    /// </summary>
    public class NarrativePage : IConfigPage
    {
        public string Id => "narrative";
        public string Label => "叙事";
        public string Group => "核心";
        public int Order => 1;

        // 本地编辑缓冲（首次 Draw 从配置初始化）
        private bool _initialized;

        // 导演专用
        private int _directorCountThreshold;
        private int _directorImportanceThreshold;
        private int _directorTimerInterval;

        // Freelancer 专用
        private int _freelancerCountThreshold;
        private int _freelancerImportanceThreshold;
        private int _freelancerTimerInterval;

        // 剧情编剧专用
        private int _screenwriterCountThreshold;
        private int _screenwriterImportanceThreshold;

        // 通用
        private int _recentHistoryCapacity;
        private int _maxAgentRounds;

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

            // 导演专用覆盖
            DrawLabeledIntRow(listing, "导演专用事件数阈值:", ref _directorCountThreshold, 1, 999,
                "pending 事件数达到此值时触发导演 Agent");
            DrawLabeledIntRow(listing, "导演专用重要度阈值:", ref _directorImportanceThreshold, 1, 999,
                "pending 事件总重要度达到此值时触发导演 Agent");
            DrawLabeledIntRow(listing, "导演定时器间隔 (ticks):", ref _directorTimerInterval, 0, 999999,
                "0 = 禁用定时器；每 N ticks 注入一个 TimerPulse 事件");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ================================================================
            // Freelancer 策略
            // ================================================================
            BeginSection(listing, "Freelancer 策略");

            DrawLabeledIntRow(listing, "Freelancer 专用事件数阈值:", ref _freelancerCountThreshold, 1, 999,
                "pending 事件数达到此值时触发 Freelancer Agent");
            DrawLabeledIntRow(listing, "Freelancer 专用重要度阈值:", ref _freelancerImportanceThreshold, 1, 999,
                "pending 事件总重要度达到此值时触发 Freelancer Agent");
            DrawLabeledIntRow(listing, "Freelancer 定时器间隔 (ticks):", ref _freelancerTimerInterval, 0, 999999,
                "0 = 禁用定时器；每 N ticks 注入一个 TimerPulse 事件");

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
            // 操作按钮
            // ================================================================
            var btnResults = DrawButtonRow(listing,
                new[] { "保存并应用", "重置默认值" },
                new[] { BtnWidthLarge, BtnWidthMedium });

            if (btnResults[0])
            {
                var dc = new DriverConfig
                {
                    DirectorCountThreshold = _directorCountThreshold,
                    DirectorImportanceThreshold = _directorImportanceThreshold,
                    DirectorTimerInterval = _directorTimerInterval,
                    FreelancerCountThreshold = _freelancerCountThreshold,
                    FreelancerImportanceThreshold = _freelancerImportanceThreshold,
                    FreelancerTimerInterval = _freelancerTimerInterval,
                    ScreenwriterCountThreshold = _screenwriterCountThreshold,
                    ScreenwriterImportanceThreshold = _screenwriterImportanceThreshold,
                    RecentHistoryCapacity = _recentHistoryCapacity,
                    MaxAgentRounds = _maxAgentRounds,
                };
                RimLifeCore.SetDriverConfig(dc);
                RimLifeCore.RebuildAgents();
                _statusMessage = "已保存并重建 Agent";
                _statusMessageTime = Time.time;
                Log.Message("[RimLife.UI] Narrative settings saved");
            }

            if (btnResults[1])
            {
                var defaultDc = DriverConfig.CreateDefault();
                _directorCountThreshold = defaultDc.DirectorCountThreshold;
                _directorImportanceThreshold = (int)defaultDc.DirectorImportanceThreshold;
                _directorTimerInterval = defaultDc.DirectorTimerInterval;
                _freelancerCountThreshold = defaultDc.FreelancerCountThreshold;
                _freelancerImportanceThreshold = (int)defaultDc.FreelancerImportanceThreshold;
                _freelancerTimerInterval = defaultDc.FreelancerTimerInterval;
                _screenwriterCountThreshold = defaultDc.ScreenwriterCountThreshold;
                _screenwriterImportanceThreshold = (int)defaultDc.ScreenwriterImportanceThreshold;
                _recentHistoryCapacity = defaultDc.RecentHistoryCapacity;
                _maxAgentRounds = defaultDc.MaxAgentRounds;
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
            var dc = RimLifeCore.DriverConfig;
            _directorCountThreshold = dc.DirectorCountThreshold;
            _directorImportanceThreshold = (int)dc.DirectorImportanceThreshold;
            _directorTimerInterval = dc.DirectorTimerInterval;
            _freelancerCountThreshold = dc.FreelancerCountThreshold;
            _freelancerImportanceThreshold = (int)dc.FreelancerImportanceThreshold;
            _freelancerTimerInterval = dc.FreelancerTimerInterval;
            _screenwriterCountThreshold = dc.ScreenwriterCountThreshold;
            _screenwriterImportanceThreshold = (int)dc.ScreenwriterImportanceThreshold;
            _recentHistoryCapacity = dc.RecentHistoryCapacity;
            _maxAgentRounds = dc.MaxAgentRounds;
        }

        // ================================================================
        // UI 辅助
        // ================================================================

        /// <summary>
        /// 绘制标签+整数输入+描述的复合行。
        /// </summary>
        private static void DrawLabeledIntRow(Listing_Standard listing, string label, ref int value, int min, int max, string description)
        {
            // 标签行
            var labelRect = listing.GetRect(20f);
            Widgets.Label(labelRect, label);

            // 输入行
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
