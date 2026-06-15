using UnityEngine;
using Verse;

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
            Widgets.Label(listing.GetRect(24f), "── 功能开关 ──");
            listing.CheckboxLabeled("启用运行时度量", ref _enableMetrics, "Token 消耗、工具调用统计");
            listing.CheckboxLabeled("详细日志", ref _enableVerbose, "输出完整 LLM 请求/响应到日志");
            listing.CheckboxLabeled("开发模式", ref _enableDevMode, "显示 Agent 内部状态面板");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── Agent 状态 ──");
            Widgets.Label(listing.GetRect(20f), "导演 Agent:  ● 空闲   上次激活: 5分钟前");
            Widgets.Label(listing.GetRect(20f), "编剧 Agent:  ● 空闲   (工作空间: 海盗袭击后续)");
            Widgets.Label(listing.GetRect(20f), "Freelancer:  ● 空闲");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── 运行时度量 ──");
            Widgets.Label(listing.GetRect(20f), "本次会话 Token 消耗:");
            Widgets.Label(listing.GetRect(20f), "  输入: 12,450    输出: 3,200");
            Widgets.Label(listing.GetRect(20f), "工具调用次数: 47    LLM 请求次数: 8");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── 危险操作 ──");
            if (Widgets.ButtonText(listing.GetRect(30f), "重置所有配置"))
                Log.Message("[RimLife.UI] Reset all config clicked");
            if (Widgets.ButtonText(listing.GetRect(30f), "导出配置"))
                Log.Message("[RimLife.UI] Export config clicked");
            if (Widgets.ButtonText(listing.GetRect(30f), "导入配置"))
                Log.Message("[RimLife.UI] Import config clicked");
        }

        // 临时状态变量（首轮占位用）
        private bool _enableMetrics = true;
        private bool _enableVerbose = false;
        private bool _enableDevMode = false;
    }
}
