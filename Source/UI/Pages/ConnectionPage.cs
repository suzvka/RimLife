using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// 连接页面：API 配置（首轮只占位，不实现完整功能）。
    /// </summary>
    public class ConnectionPage : IConfigPage
    {
        public string Id => "connection";
        public string Label => "连接";
        public string Group => "核心";
        public int Order => 0;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            Widgets.Label(listing.GetRect(20f), "API 类型");
            if (Widgets.ButtonText(listing.GetRect(30f), "OpenAI 兼容"))
                Log.Message("[RimLife.UI] API Type: OpenAI");
            if (Widgets.ButtonText(listing.GetRect(30f), "Anthropic 兼容"))
                Log.Message("[RimLife.UI] API Type: Anthropic");
            if (Widgets.ButtonText(listing.GetRect(30f), "自定义端点"))
                Log.Message("[RimLife.UI] API Type: Custom");

            listing.Gap(8f);

            Widgets.Label(listing.GetRect(20f), "API 密钥");
            // TextField 暂不实现，首轮仅展示

            listing.Gap(8f);

            if (Widgets.ButtonText(listing.GetRect(30f), "测试连接"))
                Log.Message("[RimLife.UI] Test connection clicked");

            listing.Gap(16f);

            Widgets.Label(listing.GetRect(20f), "模型选择");
            if (Widgets.ButtonText(listing.GetRect(30f), "获取模型列表"))
                Log.Message("[RimLife.UI] List models clicked");
        }
    }
}
