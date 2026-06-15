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
        // 间距常量
        // ================================================================

        /// <summary>微间距：标签与输入框之间、列表项之间。</summary>
        public const float GapTiny = 4f;

        /// <summary>小间距：同组内元素之间。</summary>
        public const float GapSmall = 8f;

        /// <summary>中间距：Section 之间、卡片之间。</summary>
        public const float GapMedium = 12f;

        /// <summary>大间距：页面级区块之间。</summary>
        public const float GapLarge = 16f;

        // ================================================================
        // 颜色常量
        // ================================================================

        public static readonly Color ColorSidebarBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        public static readonly Color ColorContentBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color ColorCardBg = new Color(0.22f, 0.22f, 0.22f, 1f);
        public static readonly Color ColorStatusBarBg = new Color(0.12f, 0.12f, 0.12f, 1f);
        public static readonly Color ColorSelectedItem = new Color(0.25f, 0.25f, 0.25f, 1f);
        public static readonly Color ColorHighlight = new Color(0.35f, 0.65f, 1f, 1f);
        public static readonly Color ColorDivider = new Color(0.3f, 0.3f, 0.3f, 1f);
        public static readonly Color ColorCardBorder = new Color(0.28f, 0.28f, 0.28f, 1f);

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
            listing.Gap(GapMedium);
        }

        /// <summary>
        /// 绘制 Section 底部分隔线。
        /// 在 Section 内容下方绘制一条细线增强视觉分隔。
        /// </summary>
        public static void DrawSectionDivider(Listing_Standard listing)
        {
            listing.Gap(GapTiny);
            var lineRect = listing.GetRect(1f);
            Widgets.DrawBoxSolid(lineRect, ColorDivider);
        }

        // ================================================================
        // 卡片式 Section
        // ================================================================

        /// <summary>
        /// 绘制一个卡片式容器。为逻辑分组提供视觉边界。
        /// 返回卡片内部可用区域的起始 Y（调用方自行管理 listing 游标）。
        /// 注意：此方法绘制背景和标题，调用方需在返回后继续绘制内容。
        /// </summary>
        /// <param name="listing">Listing 实例。</param>
        /// <param name="title">卡片标题（可为 null，null 时不绘制标题）。</param>
        /// <param name="contentHeight">卡片内容区估算高度（用于绘制背景）。</param>
        /// <returns>卡片背景 Rect，供调用方参考。</returns>
        public static Rect BeginCard(Listing_Standard listing, string title, float contentHeight)
        {
            listing.Gap(GapTiny);
            var totalHeight = (title != null ? 28f + GapTiny : 0f) + contentHeight + GapSmall;
            var cardRect = listing.GetRect(totalHeight);

            // 卡片背景
            Widgets.DrawBoxSolid(cardRect, ColorCardBg);
            // 顶部边框线
            var topBorder = new Rect(cardRect.x, cardRect.y, cardRect.width, 1f);
            Widgets.DrawBoxSolid(topBorder, ColorCardBorder);
            // 底部边框线
            var bottomBorder = new Rect(cardRect.x, cardRect.y + cardRect.height - 1f, cardRect.width, 1f);
            Widgets.DrawBoxSolid(bottomBorder, ColorCardBorder);

            // 标题
            if (title != null)
            {
                var titleRect = new Rect(cardRect.x + GapSmall, cardRect.y + GapTiny, cardRect.width - GapSmall * 2, 24f);
                Widgets.Label(titleRect, $"<size=14><b>{title}</b></size>");
            }

            return cardRect;
        }

        // ================================================================
        // Key-Value 信息行
        // ================================================================

        /// <summary>
        /// 绘制 Label: Value 格式的信息行。
        /// Label 为浅灰色，Value 为白色，适合展示只读状态数据。
        /// </summary>
        public static void DrawInfoRow(Listing_Standard listing, string label, string value)
        {
            var rowRect = listing.GetRect(22f);
            var labelWidth = Text.CalcHeight(label, listing.ColumnWidth) * 4f; // 粗略估算 label 宽度
            var labelRect = new Rect(rowRect.x, rowRect.y, Mathf.Min(140f, labelWidth + 8f), rowRect.height);
            var valueRect = new Rect(rowRect.x + labelRect.width, rowRect.y, rowRect.width - labelRect.width, rowRect.height);

            Widgets.Label(labelRect, $"<color=#888888>{label}:</color>");
            Widgets.Label(valueRect, value);
        }

        /// <summary>
        /// 绘制带状态指示器的信息行。
        /// </summary>
        public static void DrawStatusRow(Listing_Standard listing, string label, string status, string value = null)
        {
            var rowRect = listing.GetRect(22f);
            var color = GetStatusColor(status);
            var indicator = WrapColor("● ", color);
            var text = value != null ? $"{indicator}{label}: {value}" : $"{indicator}{label}";
            Widgets.Label(rowRect, text);
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
                var descHeight = Text.CalcHeight($"<color=#888888><size=12>{description}</size></color>", listing.ColumnWidth);
                Widgets.Label(listing.GetRect(descHeight), $"<color=#888888><size=12>{description}</size></color>");
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
