using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

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
            BeginSection(listing, "导演策略");
            DrawInfoRow(listing, "最小事件数", "5");
            DrawInfoRow(listing, "冷却时间", "120 秒");
            listing.Gap(GapTiny);
            if (Widgets.ButtonText(listing.GetRect(24f), "重置默认值"))
                Log.Message("[RimLife.UI] Reset director strategy");
            EndSection(listing);

            BeginSection(listing, "Freelancer 策略");
            DrawInfoRow(listing, "最小事件数", "1");
            DrawInfoRow(listing, "事件存活", "300 秒");
            listing.Gap(GapTiny);
            if (Widgets.ButtonText(listing.GetRect(24f), "重置默认值"))
                Log.Message("[RimLife.UI] Reset freelancer strategy");
            EndSection(listing);

            BeginSection(listing, "叙事风格");
            Widgets.Label(listing.GetRect(22f), "<size=12>自由文本提示词（可编辑）:</size>");
            listing.Gap(GapTiny);
            Widgets.Label(listing.GetRect(22f), "<color=#888888>灵感参考: 黑曜石 | 轻小说 | 战锤 | 自定义</color>");
            listing.Gap(GapTiny);
            if (Widgets.ButtonText(listing.GetRect(26f), "预览"))
                Log.Message("[RimLife.UI] Preview style clicked");
            EndSection(listing);
        }
    }
}
