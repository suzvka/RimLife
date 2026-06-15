using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// UI 绘制辅助工具。封装常用的绘制模式。
    /// </summary>
    public static class UIHelper
    {
        // ================================================================
        // 颜色常量
        // ================================================================

        public static readonly Color ColorSidebarBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        public static readonly Color ColorContentBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color ColorStatusBarBg = new Color(0.12f, 0.12f, 0.12f, 1f);
        public static readonly Color ColorSelectedItem = new Color(0.25f, 0.25f, 0.25f, 1f);
        public static readonly Color ColorHighlight = new Color(0.4f, 0.7f, 1f, 1f);
        public static readonly Color ColorDivider = new Color(0.3f, 0.3f, 0.3f, 1f);

        // ================================================================
        // Section 绘制
        // ================================================================

        /// <summary>
        /// 绘制 Section 标题和内容的容器。
        /// 用法: UIHelper.BeginSection(listing, "标题"); ... UIHelper.EndSection(listing);
        /// </summary>
        public static void BeginSection(Listing_Standard listing, string title)
        {
            Widgets.Label(listing.GetRect(24f), $"<size=14><b>── {title} ──</b></size>");
            listing.Gap(4f);
        }

        public static void EndSection(Listing_Standard listing)
        {
            listing.Gap(12f);
        }

        // ================================================================
        // 带描述的 Label
        // ================================================================

        /// <summary>
        /// 绘制标签 + 描述（灰色小字）。
        /// </summary>
        public static void LabelWithDescription(Listing_Standard listing, string label, string description)
        {
            var labelHeight = Text.CalcHeight(label, listing.ColumnWidth);
            Widgets.Label(listing.GetRect(labelHeight), label);
            if (!string.IsNullOrEmpty(description))
            {
                var descHeight = Text.CalcHeight($"<color=#888888><size=11>{description}</size></color>", listing.ColumnWidth);
                Widgets.Label(listing.GetRect(descHeight), $"<color=#888888><size=11>{description}</size></color>");
            }
        }

        /// <summary>
        /// 绘制自适应高度的 Label。
        /// 根据文本内容自动计算所需高度，避免截断。
        /// </summary>
        public static void AutoHeightLabel(Listing_Standard listing, string text, float minHeight = 20f)
        {
            var height = Text.CalcHeight(text, listing.ColumnWidth);
            if (height < minHeight) height = minHeight;
            Widgets.Label(listing.GetRect(height), text);
        }

        // ================================================================
        // 状态指示器
        // ================================================================

        /// <summary>
        /// 绘制圆形状态指示器。
        /// </summary>
        /// <param name="rect">绘制区域。</param>
        /// <param name="color">指示器颜色。</param>
        public static void DrawStatusIndicator(Rect rect, Color color)
        {
            var indicatorRect = new Rect(rect.x + 4f, rect.y + rect.height / 2 - 4f, 8f, 8f);
            Widgets.DrawBoxSolid(indicatorRect, color);
        }

        /// <summary>
        /// 获取状态文本颜色。
        /// </summary>
        /// <param name="status">状态：connected / disconnected / error。</param>
        /// <returns>对应的颜色。</returns>
        public static Color GetStatusColor(string status)
        {
            switch (status?.ToLowerInvariant())
            {
                case "connected":
                case "running":
                case "active":
                    return new Color(0.3f, 0.8f, 0.3f); // 绿色
                case "disconnected":
                case "idle":
                    return new Color(0.5f, 0.5f, 0.5f); // 灰色
                case "error":
                case "failed":
                    return new Color(0.9f, 0.3f, 0.3f); // 红色
                default:
                    return new Color(0.5f, 0.5f, 0.5f);
            }
        }

        // ================================================================
        // 文本格式化
        // ================================================================

        /// <summary>
        /// 将文本包装为带颜色的 RichText。
        /// </summary>
        public static string WrapColor(string text, Color color)
        {
            return $"<color={ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
        }

        /// <summary>
        /// 将文本包装为指定大小。
        /// </summary>
        public static string WrapSize(string text, int size)
        {
            return $"<size={size}>{text}</size>";
        }

        /// <summary>
        /// 将文本包装为粗体。
        /// </summary>
        public static string WrapBold(string text)
        {
            return $"<b>{text}</b>";
        }
    }
}
