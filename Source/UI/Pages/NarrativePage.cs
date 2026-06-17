using RimLife.Driver;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 叙事页面：Agent 驱动参数配置。
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
        private int _countThreshold;
        private int _importanceThreshold;
        private int _recentHistoryCapacity;
        private int _maxAgentRounds;

        // 保存反馈
        private string _statusMessage;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            InitializeIfNeeded();

            // ---- 导演策略 ----
            BeginSection(listing, "导演策略");

            var r1 = listing.GetRect(24f);
            Widgets.Label(new Rect(r1.x, r1.y, r1.width * 0.45f, r1.height), "触发事件数阈值:");
            DrawIntField(new Rect(r1.x + r1.width * 0.45f, r1.y, 120f, r1.height), ref _countThreshold, 1, 999);

            var r2 = listing.GetRect(24f);
            Widgets.Label(new Rect(r2.x, r2.y, r2.width * 0.45f, r2.height), "重要度阈值:");
            DrawIntField(new Rect(r2.x + r2.width * 0.45f, r2.y, 120f, r2.height), ref _importanceThreshold, 1, 999);

            listing.Gap(GapTiny);
            EndSection(listing);

            // ---- 通用设置 ----
            BeginSection(listing, "通用设置");

            var r3 = listing.GetRect(24f);
            Widgets.Label(new Rect(r3.x, r3.y, r3.width * 0.45f, r3.height), "历史缓冲区容量:");
            DrawIntField(new Rect(r3.x + r3.width * 0.45f, r3.y, 120f, r3.height), ref _recentHistoryCapacity, 10, 9999);

            var r4 = listing.GetRect(24f);
            Widgets.Label(new Rect(r4.x, r4.y, r4.width * 0.45f, r4.height), "Agent 最大轮数:");
            DrawIntField(new Rect(r4.x + r4.width * 0.45f, r4.y, 120f, r4.height), ref _maxAgentRounds, 1, 100);

            listing.Gap(GapTiny);
            EndSection(listing);

            // ---- Freelancer 策略 ----
            BeginSection(listing, "Freelancer 策略");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>Freelancer 共享上述触发阈值，处理未归入剧情线的临时事件。</size></color>");
            EndSection(listing);

            // ---- 操作按钮 ----
            var btnResults = DrawButtonRow(listing,
                new[] { "保存并应用", "重置默认值" },
                new[] { BtnWidthLarge, BtnWidthMedium });

            if (btnResults[0])
            {
                var dc = new DriverConfig
                {
                    CountThreshold = _countThreshold,
                    ImportanceThreshold = _importanceThreshold,
                    RecentHistoryCapacity = _recentHistoryCapacity,
                    MaxAgentRounds = _maxAgentRounds,
                    SeverityWeights = RimLifeCore.DriverConfig.SeverityWeights
                };
                RimLifeCore.SetDriverConfig(dc);
                RimLifeCore.RebuildAgents();
                _statusMessage = "已保存并重建 Agent";
                Log.Message("[RimLife.UI] Narrative settings saved");
            }

            if (btnResults[1])
            {
                var defaultDc = DriverConfig.CreateDefault();
                _countThreshold = defaultDc.CountThreshold;
                _importanceThreshold = defaultDc.ImportanceThreshold;
                _recentHistoryCapacity = defaultDc.RecentHistoryCapacity;
                _maxAgentRounds = defaultDc.MaxAgentRounds;
                _statusMessage = "已重置（需保存生效）";
            }

            // 状态消息
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                listing.Gap(GapTiny);
                Widgets.Label(listing.GetRect(22f),
                    $"<color=#88FF88><size=12>{_statusMessage}</size></color>");
            }
        }

        private void InitializeIfNeeded()
        {
            if (_initialized) return;
            _initialized = true;
            var dc = RimLifeCore.DriverConfig;
            _countThreshold = dc.CountThreshold;
            _importanceThreshold = dc.ImportanceThreshold;
            _recentHistoryCapacity = dc.RecentHistoryCapacity;
            _maxAgentRounds = dc.MaxAgentRounds;
        }

        // ================================================================
        // 整数输入辅助
        // ================================================================

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
