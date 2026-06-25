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
        private readonly LayoutHelper.ScrollHeightTracker _scrollTracker = new LayoutHelper.ScrollHeightTracker();

        // ================================================================
        // 构造
        // ================================================================

        public ConfigPanelLayout()
        {
            Pages = new List<IConfigPage>
            {
                new ConnectionPage(),
                new NarrativePage(),
                new PromptPage(),
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

            // 统计每组页面数：仅当组内有 ≥2 个页面时才显示分组标题
            var groupCounts = new Dictionary<string, int>();
            foreach (var page in Pages)
            {
                if (!string.IsNullOrEmpty(page.Group))
                {
                    if (groupCounts.ContainsKey(page.Group))
                        groupCounts[page.Group]++;
                    else
                        groupCounts[page.Group] = 1;
                }
            }

            foreach (var page in Pages)
            {
                if (page.Group != lastGroup)
                {
                    if (!string.IsNullOrEmpty(lastGroup))
                        cursorY += GapTiny;

                    lastGroup = page.Group;

                    // 仅当该组有 ≥2 个页面时显示分组标题
                    if (!string.IsNullOrEmpty(page.Group) && groupCounts.TryGetValue(page.Group, out var count) && count >= 2)
                    {
                        var groupRect = new Rect(rect.x + GapSmall, cursorY, rect.width - GapSmall * 2, GroupHeaderHeight);
                        Widgets.Label(groupRect, $"<color=#999999><size=13><b>{page.Group}</b></size></color>");
                        cursorY += GroupHeaderHeight;
                    }
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
                    if (CurrentPage != page)
                        _scrollTracker.Reset(); // 切换页面时重置高度追踪
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

            // 使用 LayoutHelper 统一计算滚动高度（含过估算因子，消除内容截断）
            var scrollHeight = _scrollTracker.GetScrollHeight(innerRect.height);
            var viewRect = new Rect(innerRect.x, innerRect.y, innerRect.width - 16f, scrollHeight);
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

            // 测量实际内容高度，供下一帧使用（IMGUI 双帧收敛）
            // 使用 GetRect(0) 获取当前位置而不推进游标
            var endMarker = listing.GetRect(0f);
            _scrollTracker.UpdateMeasurement(endMarker.y + ContentPadding);

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

            var manager = RimLifeCore.CredentialManager;
            bool configured = manager != null && manager.HasCredentials;
            var activeOrder = configured ? manager.GetActivationOrder() : null;
            string modelInfo = (activeOrder != null && activeOrder.Count > 0) ? activeOrder[0] : "?";
            string statusColor = configured ? "#88FF88" : "#888888";
            string statusIcon = configured ? "●" : "○";
            string statusText = configured ? $"已配置 ({modelInfo})" : "未配置";

            Widgets.Label(leftRect, $"<color=#999999><size=12>LLM 状态: <color={statusColor}>{statusIcon} {statusText}</color>    会话 Token: 0</size></color>");
            Widgets.Label(rightRect, $"<color=#666666><size=11>RimLife v1.6</size></color>");
        }
    }
}
