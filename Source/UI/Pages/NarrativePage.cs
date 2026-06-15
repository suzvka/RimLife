using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// 叙事页面：事件策略 + 风格提示词（首轮只占位）。
    /// </summary>
    public class NarrativePage : IConfigPage
    {
        public string Id => "narrative";
        public string Label => "叙事";
        public string Group => "核心";
        public int Order => 1;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            Widgets.Label(listing.GetRect(24f), "── 导演策略 ──");
            Widgets.Label(listing.GetRect(20f), "最小事件数: [5]    冷却时间: [120]秒");
            listing.Gap(8f);
            if (Widgets.ButtonText(listing.GetRect(30f), "重置默认值"))
                Log.Message("[RimLife.UI] Reset director strategy");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── Freelancer 策略 ──");
            Widgets.Label(listing.GetRect(20f), "最小事件数: [1]    事件存活: [300]秒");
            listing.Gap(8f);
            if (Widgets.ButtonText(listing.GetRect(30f), "重置默认值"))
                Log.Message("[RimLife.UI] Reset freelancer strategy");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── 叙事风格 ──");
            Widgets.Label(listing.GetRect(20f), "自由文本提示词（可编辑）:");
            // TextField 暂不实现，首轮仅展示
            listing.Gap(4f);
            Widgets.Label(listing.GetRect(20f), "灵感参考: [黑曜石] [轻小说] [战锤] [自定义]");
            listing.Gap(8f);
            if (Widgets.ButtonText(listing.GetRect(30f), "预览"))
                Log.Message("[RimLife.UI] Preview style clicked");
        }
    }
}
