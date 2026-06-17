using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 高级页面：诊断、状态、度量（首轮只占位）。
    /// </summary>
    public class AdvancedPage : IConfigPage
    {
        public string Id => "advanced";
        public string Label => "高级";
        public string Group => "系统";
        public int Order => 0;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            BeginSection(listing, "功能开关");
            listing.CheckboxLabeled("启用运行时度量", ref _enableMetrics, "Token 消耗、工具调用统计");
            listing.CheckboxLabeled("详细日志", ref _enableVerbose, "输出完整 LLM 请求/响应到日志");
            listing.CheckboxLabeled("开发模式", ref _enableDevMode, "显示 Agent 内部状态面板");
            EndSection(listing);

            BeginSection(listing, "Agent 状态");
            DrawStatusRow(listing, "导演 Agent", "idle", "上次激活: 5 分钟前");
            DrawStatusRow(listing, "编剧 Agent", "idle", "工作空间: 海盗袭击后续");
            DrawStatusRow(listing, "Freelancer", "idle");
            EndSection(listing);

            BeginSection(listing, "运行时度量");
            Widgets.Label(listing.GetRect(22f), "<size=12>本次会话 Token 消耗:</size>");
            DrawInfoRow(listing, "  输入", "12,450");
            DrawInfoRow(listing, "  输出", "3,200");
            DrawInfoRow(listing, "工具调用次数", "47");
            DrawInfoRow(listing, "LLM 请求次数", "8");
            EndSection(listing);

            BeginSection(listing, "危险操作");
            var dangerResults = DrawButtonRow(listing,
                new[] { "重置所有配置", "导出配置", "导入配置" },
                new[] { BtnWidthMedium, BtnWidthMedium, BtnWidthMedium });
            if (dangerResults[0])
                Log.Message("[RimLife.UI] Reset all config clicked");
            if (dangerResults[1])
                Log.Message("[RimLife.UI] Export config clicked");
            if (dangerResults[2])
                Log.Message("[RimLife.UI] Import config clicked");
            EndSection(listing);
        }

        // 临时状态变量（首轮占位用）
        private bool _enableMetrics = true;
        private bool _enableVerbose = false;
        private bool _enableDevMode = false;
    }
}
