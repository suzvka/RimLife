using System;
using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// 集中式布局辅助模块。
    /// 解决滚动高度测量、嵌套 ScrollView、卡片自适应高度三大问题。
    /// </summary>
    public static class LayoutHelper
    {
        // ================================================================
        // ScrollHeightTracker - 可靠的滚动高度追踪器
        // ================================================================

        /// <summary>
        /// 追踪 ScrollView 内容高度，使用过估算策略确保内容不被截断。
        /// 替代各页面中散落的 _measuredContentHeight 逻辑。
        /// </summary>
        public class ScrollHeightTracker
        {
            /// <summary>过估算因子：滚动区比实际内容多 20%，消除截断。</summary>
            public const float OvershootFactor = 1.2f;

            /// <summary>滚动高度下限：可见区域的倍数，防止小页面出现无意义短滚动。</summary>
            public const float MinScrollMultiplier = 3f;

            /// <summary>初始高度预估值，足够大以避免首帧截断。</summary>
            private const float InitialHeight = 2400f;

            private float _measuredHeight = InitialHeight;

            /// <summary>
            /// 获取当前帧应使用的滚动高度。
            /// </summary>
            /// <param name="visibleHeight">可见区域高度（用于计算下限）。</param>
            /// <returns>应用于 viewRect 的高度。</returns>
            public float GetScrollHeight(float visibleHeight)
            {
                var minHeight = visibleHeight * MinScrollMultiplier;
                return Math.Max(minHeight, _measuredHeight * OvershootFactor);
            }

            /// <summary>
            /// 在绘制完成后更新测量值。传入实际内容高度（包含 padding）。
            /// </summary>
            /// <param name="actualHeight">实际内容高度。</param>
            public void UpdateMeasurement(float actualHeight)
            {
                if (actualHeight > 100f) // 过滤异常值
                    _measuredHeight = actualHeight;
            }

            /// <summary>
            /// 重置测量值。切换页面时调用，避免旧高度影响新页面。
            /// </summary>
            public void Reset()
            {
                _measuredHeight = InitialHeight;
            }
        }

        // ================================================================
        // SubScrollRegion - 在父 ScrollView 内分配固定高度子区域
        // ================================================================

        /// <summary>
        /// 在父 Listing 中预留固定高度区域，返回可用于独立 ScrollView 的内部 Rect。
        /// 解决“嵌套 ScrollView”布局问题：
        /// 通过从父 listing 明确预留高度，确保父 ScrollView 的 viewRect 包含此区域；
        /// 调用方在返回的 Rect 内创建独立 ScrollView，实现子区域独立滚动。
        /// </summary>
        /// <param name="parentListing">父 Listing（已在父 ScrollView 内）。</param>
        /// <param name="desiredHeight">子区域期望高度（像素）。</param>
        /// <param name="innerRect">返回的内部可用区域（已减去内边距）。</param>
        /// <returns>子区域的外部 Rect（含背景）。</returns>
        public static Rect AllocateSubScrollRegion(
            Listing_Standard parentListing,
            float desiredHeight,
            out Rect innerRect)
        {
            // 从父 listing 预留固定高度（父 ScrollView 会将此高度纳入 viewRect）
            var outerRect = parentListing.GetRect(desiredHeight);

            // 计算内边距后的可用区域
            innerRect = new Rect(
                outerRect.x + UIHelper.GapSmall,
                outerRect.y + UIHelper.GapSmall,
                outerRect.width - UIHelper.GapSmall * 2,
                outerRect.height - UIHelper.GapSmall * 2);

            return outerRect;
        }

        // ================================================================
        // AdaptiveCard - 自适应高度卡片
        // ================================================================

        /// <summary>
        /// 自适应卡片追踪器。
        /// 每帧测量实际内容高度，下一帧使用测量值作为卡片高度。
        /// 比手动估算 contentHeight 可靠得多。
        /// </summary>
        public class AdaptiveCardTracker
        {
            private float _lastMeasuredHeight = 120f; // 初始估计值

            /// <summary>
            /// 绘制自适应卡片。
            /// 使用上一帧测量的高度作为本帧卡片高度，内容绘制后更新测量值。
            /// IMGUI 双帧收敛，第二帧即稳定。
            /// </summary>
            /// <param name="listing">父 Listing。</param>
            /// <param name="title">卡片标题（null 时不绘制标题）。</param>
            /// <param name="drawContent">卡片内容绘制回调。</param>
            public void Draw(Listing_Standard listing, string title, Action<Listing_Standard> drawContent)
            {
                // 使用上一帧测量值 + padding 作为本帧卡片高度
                var titleHeight = title != null ? 28f + UIHelper.GapTiny : 0f;
                var estimatedTotal = titleHeight + _lastMeasuredHeight + UIHelper.GapSmall;

                listing.Gap(UIHelper.GapTiny);
                var cardRect = listing.GetRect(estimatedTotal);

                // 卡片背景 + 边框
                Widgets.DrawBoxSolid(cardRect, UIHelper.ColorCardBg);
                Widgets.DrawBox(cardRect, 1);

                // 标题
                if (title != null)
                {
                    var titleRect = new Rect(
                        cardRect.x + UIHelper.GapSmall,
                        cardRect.y + UIHelper.GapTiny,
                        cardRect.width - UIHelper.GapSmall * 2,
                        24f);
                    Widgets.Label(titleRect, $"<size=14><b>{title}</b></size>");
                }

                // 记录内容绘制前的 Y 位置
                var beforeY = listing.CurHeight;

                // 绘制内容
                drawContent(listing);

                // 测量实际内容高度
                var afterY = listing.CurHeight;
                var actualContentHeight = afterY - beforeY;
                if (actualContentHeight > 10f) // 过滤异常值
                    _lastMeasuredHeight = actualContentHeight;
            }

            /// <summary>重置测量值。</summary>
            public void Reset()
            {
                _lastMeasuredHeight = 120f;
            }
        }
    }
}
