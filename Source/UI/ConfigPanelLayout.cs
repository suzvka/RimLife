using System.Collections.Generic;
using System.Linq;
using RimLife.Infrastructure;
using RimLife.UI.Pages;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 配置面板共享布局组件。
    /// 封装三区布局（侧栏 + 内容区 + 状态栏），供 Mod Settings 和浮动窗口共用。
    /// </summary>
    public class ConfigPanelLayout
    {
        // ================================================================
        // 布局常量
        // ================================================================

        public const float SidebarWidth = 150f;
        public const float ContentPadding = 16f;
        public const float GroupHeaderHeight = 22f;
        public const float NavItemHeight = 36f;
        public const float StatusBarHeight = 30f;

        // ================================================================
        // 状态
        // ================================================================

        public readonly List<IConfigPage> Pages;
        public IConfigPage CurrentPage { get; private set; }
        private Vector2 _scrollPosition;

        // ================================================================
        // 构造
        // ================================================================

        public ConfigPanelLayout()
        {
            Pages = new List<IConfigPage>
            {
                new ConnectionPage(),
                new NarrativePage(),
                new KnowledgePage(),
                new AdvancedPage(),
                new DebugPage()
            };

            Pages.Sort((a, b) => a.Order.CompareTo(b.Order));
            CurrentPage = Pages.FirstOrDefault();
        }

        // ================================================================
        // 入口：在给定 Rect 内绘制完整三区布局
        // ================================================================

        public void Draw(Rect inRect)
        {
            var sidebarRect = new Rect(inRect.x, inRect.y, SidebarWidth, inRect.height - StatusBarHeight);
            var contentRect = new Rect(inRect.x + SidebarWidth, inRect.y, inRect.width - SidebarWidth, inRect.height - StatusBarHeight);
            var statusBarRect = new Rect(inRect.x, inRect.y + inRect.height - StatusBarHeight, inRect.width, StatusBarHeight);

            DrawSidebar(sidebarRect);
            DrawContent(contentRect);
            DrawStatusBar(statusBarRect);
        }

        // ================================================================
        // 侧栏
        // ================================================================

        private void DrawSidebar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ColorSidebarBg);

            var cursorY = rect.y + GapSmall;
            var lastGroup = "";

            foreach (var page in Pages)
            {
                if (page.Group != lastGroup)
                {
                    if (!string.IsNullOrEmpty(lastGroup))
                        cursorY += GapTiny;

                    lastGroup = page.Group;
                    var groupRect = new Rect(rect.x + GapSmall, cursorY, rect.width - GapSmall * 2, GroupHeaderHeight);
                    Widgets.Label(groupRect, $"<color=#999999><size=13><b>{page.Group}</b></size></color>");
                    cursorY += GroupHeaderHeight;
                }

                var itemRect = new Rect(rect.x + GapTiny, cursorY, rect.width - GapTiny * 2, NavItemHeight);
                var isSelected = page == CurrentPage;

                if (isSelected)
                {
                    Widgets.DrawBoxSolid(itemRect, ColorSelectedItem);
                    var highlightRect = new Rect(rect.x + GapTiny, cursorY + 4f, 4f, NavItemHeight - 8f);
                    Widgets.DrawBoxSolid(highlightRect, ColorHighlight);
                }
                else
                {
                    // Hover 效果：未选中时鼠标悬停显示半透明高亮
                    DrawHoverBackground(itemRect);
                }

                var labelRect = new Rect(rect.x + 16f, cursorY + 6f, rect.width - 36f, NavItemHeight - 12f);
                if (isSelected)
                    Widgets.Label(labelRect, $"<color=#FFFFFF><size=13>{page.Label}</size></color>");
                else
                    Widgets.Label(labelRect, $"<color=#B0B0B0><size=12>{page.Label}</size></color>");

                if (Widgets.ButtonInvisible(itemRect))
                {
                    CurrentPage = page;
                    _scrollPosition = Vector2.zero;
                }

                cursorY += NavItemHeight + 1f;
            }
        }

        // ================================================================
        // 内容区
        // ================================================================

        private void DrawContent(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ColorContentBg);

            if (CurrentPage == null) return;

            var innerRect = new Rect(
                rect.x + ContentPadding,
                rect.y + ContentPadding,
                rect.width - ContentPadding * 2,
                rect.height - ContentPadding * 2
            );

            // 动态计算虚拟高度：取内容区可见高度的 3 倍与 800f 的较大值，
            // 确保有足够滚动空间，同时避免固定 1200f 导致的空白过多
            var estimatedContentHeight = Mathf.Max(innerRect.height * 3f, 800f);
            var viewRect = new Rect(innerRect.x, innerRect.y, innerRect.width - 16f, estimatedContentHeight);
            Widgets.BeginScrollView(innerRect, ref _scrollPosition, viewRect);

            var listing = new Listing_Standard();
            listing.maxOneColumn = true;
            listing.Begin(viewRect);

            var titleRect = listing.GetRect(28f);
            Widgets.Label(titleRect, $"<size=18><b>{CurrentPage.Label}</b></size>");
            var titleLineRect = new Rect(titleRect.x, titleRect.y + titleRect.height, titleRect.width, 1f);
            Widgets.DrawBoxSolid(titleLineRect, ColorDivider);
            listing.Gap(GapMedium);

            CurrentPage.Draw(viewRect, listing);

            listing.End();
            Widgets.EndScrollView();
        }

        // ================================================================
        // 状态栏
        // ================================================================

        private void DrawStatusBar(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ColorStatusBarBg);

            var lineRect = new Rect(rect.x, rect.y, rect.width, 1f);
            Widgets.DrawBoxSolid(lineRect, ColorDivider);

            var padding = GapSmall;
            var leftRect = new Rect(rect.x + padding, rect.y + 4f, rect.width * 0.65f, rect.height - 8f);
            var rightRect = new Rect(rect.x + rect.width * 0.65f, rect.y + 4f, rect.width * 0.35f - padding, rect.height - 8f);

            var accessor = RimLifeCore.LlmAccessor;
            bool configured = accessor != null && accessor.IsConfigured;
            string modelInfo = configured ? accessor.Config?.ModelName ?? "?" : "";
            string statusColor = configured ? "#88FF88" : "#888888";
            string statusIcon = configured ? "●" : "○";
            string statusText = configured ? $"已配置 ({modelInfo})" : "未配置";

            Widgets.Label(leftRect, $"<color=#999999><size=12>LLM 状态: <color={statusColor}>{statusIcon} {statusText}</color>    会话 Token: 0</size></color>");
            Widgets.Label(rightRect, $"<color=#666666><size=11>RimLife v1.6</size></color>");
        }
    }
}
