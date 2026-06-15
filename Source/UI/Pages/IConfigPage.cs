using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// 配置页面接口。每个 Tab 对应一个 Page 实现。
    /// 纯绘制逻辑，不持有状态。
    /// </summary>
    public interface IConfigPage
    {
        /// <summary>页面唯一 ID（用于导航路由）。</summary>
        string Id { get; }

        /// <summary>页面显示名（侧栏显示）。</summary>
        string Label { get; }

        /// <summary>分组标签（侧栏分组标题）。</summary>
        string Group { get; }

        /// <summary>排序权重（同组内越小越靠前）。</summary>
        int Order { get; }

        /// <summary>绘制页面内容。</summary>
        /// <param name="rect">可用绘制区域。</param>
        /// <param name="listing">Listing 实例，用于布局。</param>
        void Draw(Rect rect, Listing_Standard listing);
    }
}
