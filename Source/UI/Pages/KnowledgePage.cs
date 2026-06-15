using UnityEngine;
using Verse;

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
            Widgets.Label(listing.GetRect(24f), "── 自动学习 ──");
            Widgets.Label(listing.GetRect(20f), "词条总数: 42    上次更新: 2小时前");
            listing.Gap(8f);
            Widgets.Label(listing.GetRect(20f), "最近学习:");
            Widgets.Label(listing.GetRect(20f), "  • 16:32  学会「机械集群」— 来源: Raid 事件");
            Widgets.Label(listing.GetRect(20f), "  • 16:30  学会「枯萎病」— 来源: 环境事件");
            Widgets.Label(listing.GetRect(20f), "  • 15:58  学会「虚空裂隙」— 来源: GameDef");
            listing.Gap(4f);
            if (Widgets.ButtonText(listing.GetRect(30f), "查看全部词条"))
                Log.Message("[RimLife.UI] View all knowledge entries");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(24f), "── 操作 ──");
            if (Widgets.ButtonText(listing.GetRect(30f), "从 GameDef 重新扫描"))
                Log.Message("[RimLife.UI] Rescan GameDef clicked");
            listing.Gap(4f);
            if (Widgets.ButtonText(listing.GetRect(30f), "导出知识库"))
                Log.Message("[RimLife.UI] Export knowledge clicked");
            if (Widgets.ButtonText(listing.GetRect(30f), "导入知识库"))
                Log.Message("[RimLife.UI] Import knowledge clicked");
        }
    }
}
