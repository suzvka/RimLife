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
        // 按钮尺寸常量
        // ================================================================

        /// <summary>按钮统一高度。</summary>
        public const float BtnHeight = 28f;

        /// <summary>小按钮宽度（如"取消""全选"）。</summary>
        public const float BtnWidthSmall = 80f;

        /// <summary>中按钮宽度（如"保存""编辑"）。</summary>
        public const float BtnWidthMedium = 120f;

        /// <summary>大按钮宽度（如"添加卡片""获取模型"）。</summary>
        public const float BtnWidthLarge = 160f;

        /// <summary>按钮之间的间距。</summary>
        public const float BtnGap = 8f;

        // ================================================================
        // CJK 文本高度补偿
        // ================================================================

        /// <summary>
        /// CJK 文本高度补偿因子。
        /// RimWorld (Unity) 的 Text.CalcHeight 对中文字符的高度估算偏低，
        /// 导致自动换行后实际渲染高度超出分配区域，产生截断。
        /// 此因子用于补偿该误差。
        /// </summary>
        public const float CjkHeightCompensation = 1.25f;

        /// <summary>
        /// 检测文本是否包含 CJK（中日韩）字符。
        /// </summary>
        public static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                // CJK Unified Ideographs + Extensions + CJK Radicals + Fullwidth Forms
                if ((c >= '\u4E00' && c <= '\u9FFF') ||
                    (c >= '\u3400' && c <= '\u4DBF') ||
                    (c >= '\uF900' && c <= '\uFAFF') ||
                    (c >= '\uFF00' && c <= '\uFFEF') ||
                    (c >= '\u3000' && c <= '\u303F') ||
                    (c >= '\u2E80' && c <= '\u2EFF'))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 补偿版 Text.CalcHeight。
        /// 对包含 CJK 字符的文本应用高度补偿因子，消除 RimWorld 引擎的中文高度低估问题。
        /// </summary>
        /// <param name="text">要测量的文本。</param>
        /// <param name="width">可用宽度。</param>
        /// <returns>补偿后的文本高度。</returns>
        public static float CalcTextHeight(string text, float width)
        {
            var h = Text.CalcHeight(text, width);
            if (ContainsCjk(text))
                h *= CjkHeightCompensation;
            return h;
        }

        // ================================================================
        // 颜色常量
        // ================================================================

        public static readonly Color ColorSidebarBg = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        public static readonly Color ColorContentBg = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color ColorCardBg = new Color(0.22f, 0.22f, 0.22f, 1f);
        public static readonly Color ColorStatusBarBg = new Color(0.12f, 0.12f, 0.12f, 1f);
        public static readonly Color ColorSelectedItem = new Color(0.25f, 0.25f, 0.25f, 1f);
        public static readonly Color ColorHoverItem = new Color(0.22f, 0.22f, 0.22f, 1f);
        public static readonly Color ColorHighlight = new Color(0.35f, 0.65f, 1f, 1f);
        public static readonly Color ColorDivider = new Color(0.3f, 0.3f, 0.3f, 1f);
        public static readonly Color ColorCardBorder = new Color(0.35f, 0.35f, 0.35f, 1f);
        public static readonly Color ColorDanger = new Color(0.9f, 0.3f, 0.3f, 1f);
        public static readonly Color ColorDangerBg = new Color(0.35f, 0.12f, 0.12f, 1f);

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
        /// 注意：对于需要自适应高度的卡片，请使用 LayoutHelper.AdaptiveCardTracker。
        /// </summary>
        /// <param name="listing">Listing 实例。</param>
        /// <param name="title">卡片标题（可为 null，null 时不绘制标题）。</param>
        /// <param name="contentHeight">卡片内容区估算高度（用于绘制背景）。</param>
        /// <returns>卡片背景 Rect，供调用方参考。</returns>
        /// <remarks>此方法绘制背景和标题，调用方需在返回后继续绘制内容。</remarks>
        public static Rect BeginCard(Listing_Standard listing, string title, float contentHeight)
        {
            listing.Gap(GapTiny);
            var totalHeight = (title != null ? 28f + GapTiny : 0f) + contentHeight + GapSmall;
            var cardRect = listing.GetRect(totalHeight);

            // 卡片背景
            Widgets.DrawBoxSolid(cardRect, ColorCardBg);
            // 四边完整边框
            Widgets.DrawBox(cardRect, 1);

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
            var labelText = $"{label}:";
            var labelSize = Text.CalcSize(labelText);
            var labelWidth = Mathf.Min(rowRect.width * 0.45f, labelSize.x + 8f);
            var labelRect = new Rect(rowRect.x, rowRect.y, labelWidth, rowRect.height);
            var valueRect = new Rect(rowRect.x + labelWidth, rowRect.y, rowRect.width - labelWidth, rowRect.height);

            Widgets.Label(labelRect, $"<color=#888888>{labelText}</color>");
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
            var labelHeight = CalcTextHeight(label, listing.ColumnWidth);
            Widgets.Label(listing.GetRect(labelHeight), label);
            if (!string.IsNullOrEmpty(description))
            {
                var descText = $"<color=#888888><size=12>{description}</size></color>";
                var descHeight = CalcTextHeight(descText, listing.ColumnWidth);
                Widgets.Label(listing.GetRect(descHeight), $"<color=#888888><size=12>{description}</size></color>");
            }
        }

        /// <summary>
        /// 绘制自适应高度的 Label。
        /// 根据文本内容自动计算所需高度，避免截断。
        /// </summary>
        public static void AutoHeightLabel(Listing_Standard listing, string text, float minHeight = 20f)
        {
            var height = CalcTextHeight(text, listing.ColumnWidth);
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

        // ================================================================
        // 按钮行布局
        // ================================================================

        /// <summary>
        /// 在 listing 中绘制一行按钮，自动处理间距和布局。
        /// 返回每个按钮是否被点击的数组。
        /// </summary>
        /// <param name="listing">Listing 实例。</param>
        /// <param name="labels">按钮标签数组。</param>
        /// <param name="widths">按钮宽度数组（与 labels 一一对应）。若为 null 则使用 BtnWidthMedium。</param>
        /// <returns>每个按钮是否被点击。</returns>
        public static bool[] DrawButtonRow(Listing_Standard listing, string[] labels, float[] widths = null)
        {
            var rowRect = listing.GetRect(BtnHeight + 2f);
            var results = new bool[labels.Length];
            var cursorX = rowRect.x;

            for (int i = 0; i < labels.Length; i++)
            {
                var w = widths != null && i < widths.Length ? widths[i] : BtnWidthMedium;
                var btnRect = new Rect(cursorX, rowRect.y, w, BtnHeight);
                results[i] = Widgets.ButtonText(btnRect, labels[i]);
                cursorX += w + BtnGap;
            }

            return results;
        }

        /// <summary>
        /// 在指定 Rect 内绘制一行按钮（不使用 listing），适用于卡片内部等场景。
        /// </summary>
        public static bool[] DrawButtonRowInRect(Rect rowRect, string[] labels, float[] widths = null)
        {
            var results = new bool[labels.Length];
            var cursorX = rowRect.x;

            for (int i = 0; i < labels.Length; i++)
            {
                var w = widths != null && i < widths.Length ? widths[i] : BtnWidthMedium;
                var btnRect = new Rect(cursorX, rowRect.y, w, BtnHeight);
                results[i] = Widgets.ButtonText(btnRect, labels[i]);
                cursorX += w + BtnGap;
            }

            return results;
        }

        // ================================================================
        // 分段选择器（替代并排按钮模拟单选）
        // ================================================================

        /// <summary>
        /// 绘制分段选择器。选中项有高亮背景，未选中项为普通按钮样式。
        /// </summary>
        /// <param name="listing">Listing 实例。</param>
        /// <param name="options">选项标签数组。</param>
        /// <param name="selectedIndex">当前选中索引。</param>
        /// <returns>新的选中索引。</returns>
        public static int DrawSegmentedSelector(Listing_Standard listing, string[] options, int selectedIndex)
        {
            var rowRect = listing.GetRect(BtnHeight + 2f);
            var totalWidth = rowRect.width;
            var segWidth = (totalWidth - BtnGap * (options.Length - 1)) / options.Length;
            var cursorX = rowRect.x;

            for (int i = 0; i < options.Length; i++)
            {
                var segRect = new Rect(cursorX, rowRect.y, segWidth, BtnHeight);

                if (i == selectedIndex)
                {
                    // 选中态：高亮背景 + 白色文字
                    Widgets.DrawBoxSolid(segRect, new Color(ColorHighlight.r, ColorHighlight.g, ColorHighlight.b, 0.3f));
                    Widgets.DrawBox(segRect, 1);
                    Widgets.Label(segRect, $"<color=#FFFFFF><b>{options[i]}</b></color>");
                }
                else
                {
                    // 未选中态：普通按钮
                    if (Widgets.ButtonText(segRect, options[i]))
                    {
                        selectedIndex = i;
                    }
                }

                cursorX += segWidth + BtnGap;
            }

            return selectedIndex;
        }

        // ================================================================
        // Hover 检测辅助
        // ================================================================

        /// <summary>
        /// 如果鼠标悬停在指定区域内，绘制半透明高亮背景。
        /// </summary>
        public static bool DrawHoverBackground(Rect rect, Color? hoverColor = null)
        {
            var isHover = Mouse.IsOver(rect);
            if (isHover)
            {
                Widgets.DrawBoxSolid(rect, hoverColor ?? ColorHoverItem);
            }
            return isHover;
        }

        // ================================================================
        // 状态消息统一绘制（带淡出效果）
        // ================================================================

        /// <summary>
        /// 绘制状态消息。持续显示 5 秒，最后 1 秒淡出。
        /// 所有配置页面复用此方法，确保一致的反馈体验。
        /// </summary>
        /// <param name="listing">Listing 实例。</param>
        /// <param name="message">消息文本（以 "[错误]" 开头时显示红色）。</param>
        /// <param name="messageTime">消息的 Time.time 时间戳。传 0 表示无消息。</param>
        /// <returns>消息是否仍在显示中。</returns>
        public static bool DrawStatusMessage(Listing_Standard listing, string message, float messageTime)
        {
            if (string.IsNullOrEmpty(message) || messageTime <= 0f)
                return false;

            var elapsed = Time.time - messageTime;
            if (elapsed >= 5f)
                return false;

            var isError = message.StartsWith("[错误]");
            var baseColor = isError ? "#FF6666" : "#88FF88";

            string colorTag;
            if (elapsed > 4f)
            {
                var alpha = Mathf.Clamp01(5f - elapsed);
                var alphaHex = ((int)(alpha * 255)).ToString("X2");
                colorTag = $"<color={baseColor}{alphaHex}>";
            }
            else
            {
                colorTag = $"<color={baseColor}>";
            }

            Widgets.Label(listing.GetRect(22f), $"{colorTag}<size=12>{message}</size></color>");
            listing.Gap(GapSmall);
            return true;
        }

        // ================================================================
        // 凭证测试状态颜色
        // ================================================================

        public static readonly Color ColorTestSuccess = new Color(0.3f, 0.8f, 0.3f);
        public static readonly Color ColorTestFailed = new Color(0.9f, 0.3f, 0.3f);
        public static readonly Color ColorTestUntested = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color ColorTestRunning = new Color(0.35f, 0.65f, 1f);

        // ================================================================
        // 工具方法
        // ================================================================

        /// <summary>
        /// 截断字符串至指定长度，超出部分替换为 "..."。
        /// </summary>
        /// <param name="value">原始字符串。</param>
        /// <param name="maxLength">最大长度（不含省略号）。</param>
        /// <param name="defaultValue">输入为 null 或空时返回的默认值。</param>
        public static string Truncate(string value, int maxLength, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(value)) return defaultValue;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
