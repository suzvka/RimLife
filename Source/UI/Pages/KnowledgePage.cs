using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 知识库页面：观察自动学习结果 + 导入导出（首轮只占位）。
    /// </summary>
    public class KnowledgePage : IConfigPage
    {
        public string Id => "knowledge";
        public string Label => "知识库";
        public string Group => "数据";
        public int Order => 0;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            BeginSection(listing, "自动学习");
            DrawInfoRow(listing, "词条总数", "42");
            DrawInfoRow(listing, "上次更新", "2 小时前");
            listing.Gap(GapTiny);
            Widgets.Label(listing.GetRect(22f), "<size=12>最近学习:</size>");
            Widgets.Label(listing.GetRect(22f), "<color=#AAAAAA>  • 16:32  学会「机械集群」— 来源: Raid 事件</color>");
            Widgets.Label(listing.GetRect(22f), "<color=#AAAAAA>  • 16:30  学会「枯萎病」— 来源: 环境事件</color>");
            Widgets.Label(listing.GetRect(22f), "<color=#AAAAAA>  • 15:58  学会「虚空裂隙」— 来源: GameDef</color>");
            listing.Gap(GapTiny);
            var viewResults = DrawButtonRow(listing, new[] { "查看全部词条" }, new[] { BtnWidthMedium });
            if (viewResults[0])
                Log.Message("[RimLife.UI] View all knowledge entries");
            EndSection(listing);

            BeginSection(listing, "操作");
            var opResults = DrawButtonRow(listing,
                new[] { "从 GameDef 重新扫描" },
                new[] { BtnWidthLarge + 20f });
            if (opResults[0])
                Log.Message("[RimLife.UI] Rescan GameDef clicked");
            listing.Gap(GapTiny);
            var ioResults = DrawButtonRow(listing,
                new[] { "导出知识库", "导入知识库" },
                new[] { BtnWidthMedium, BtnWidthMedium });
            if (ioResults[0])
                Log.Message("[RimLife.UI] Export knowledge clicked");
            if (ioResults[1])
                Log.Message("[RimLife.UI] Import knowledge clicked");
            EndSection(listing);
        }
    }
}
