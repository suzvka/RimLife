using System.Collections.Generic;
using System.Linq;
using RimLife.UI.Pages;
using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// RimLife 配置面板主窗口。
    /// 采用侧栏导航 + 内容区的布局模式。
    /// 
    /// 布局结构:
    /// ┌──────────────┬──────────────────────────────────┐
    /// │              │                                  │
    /// │   侧边导航    │              内容区               │
    /// │   (固定宽度)  │          (弹性宽度，可滚动)       │
    /// │              │                                  │
    /// │  ● 连接      │                                  │
    /// │    叙事      │                                  │
    /// │              │                                  │
    /// │    知识库    │                                  │
    /// │              │                                  │
    /// │    高级      │                                  │
    /// │              │                                  │
    /// │  ─────────── │                                  │
    /// │  底部状态区   │                                  │
    /// └──────────────┴──────────────────────────────────┘
    /// </summary>
    public class ConfigPanelWindow : Window
    {
        // ================================================================
        // 常量
        // ================================================================

        /// <summary>侧栏宽度（像素）。</summary>
        private const float SidebarWidth = 140f;

        /// <summary>内容区内边距。</summary>
        private const float ContentPadding = 16f;

        /// <summary>分组标题高度。</summary>
        private const float GroupHeaderHeight = 20f;

        /// <summary>导航项高度。</summary>
        private const float NavItemHeight = 32f;

        /// <summary>底部状态栏高度。</summary>
        private const float StatusBarHeight = 24f;

        // ================================================================
        // 状态
        // ================================================================

        private List<IConfigPage> _pages;
        private IConfigPage _currentPage;
        private Vector2 _scrollPosition;

        // ================================================================
        // Window 配置
        // ================================================================

        public ConfigPanelWindow()
        {
            // 窗口属性
            doCloseButton = true;
            closeOnClickedOutside = true;
            draggable = true;
            resizeable = false;

            // 初始化页面
            _pages = new List<IConfigPage>
            {
                new ConnectionPage(),
                new NarrativePage(),
                new KnowledgePage(),
                new AdvancedPage()
            };

            // 默认选中第一个页面
            _currentPage = _pages.FirstOrDefault();

            // 排序页面（按 Order）
            _pages = _pages.OrderBy(p => p.Order).ToList();
        }

        public override Vector2 InitialSize => new Vector2(720f, 520f);

        // ================================================================
        // 绘制
        // ================================================================

        public override void DoWindowContents(Rect inRect)
        {
            // 分割区域
            var sidebarRect = new Rect(inRect.x, inRect.y, SidebarWidth, inRect.height - StatusBarHeight);
            var contentRect = new Rect(inRect.x + SidebarWidth, inRect.y, inRect.width - SidebarWidth, inRect.height - StatusBarHeight);
            var statusBarRect = new Rect(inRect.x, inRect.y + inRect.height - StatusBarHeight, inRect.width, StatusBarHeight);

            // 绘制各区域
            DrawSidebar(sidebarRect);
            DrawContent(contentRect);
            DrawStatusBar(statusBarRect);
        }

        // ----------------------------------------------------------------
        // 侧栏绘制
        // ----------------------------------------------------------------

        private void DrawSidebar(Rect rect)
        {
            // 侧栏背景（深色）
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.95f));

            // 绘制导航项
            var cursorY = rect.y + 8f;
            var lastGroup = "";

            foreach (var page in _pages)
            {
                // 分组标题
                if (page.Group != lastGroup)
                {
                    lastGroup = page.Group;
                    var groupRect = new Rect(rect.x + 8f, cursorY, rect.width - 16f, GroupHeaderHeight);
                    Widgets.Label(groupRect, $"<color=#666666><size=11>{page.Group}</size></color>");
                    cursorY += GroupHeaderHeight;
                }

                // 导航项
                var itemRect = new Rect(rect.x + 4f, cursorY, rect.width - 8f, NavItemHeight);
                var isSelected = page == _currentPage;

                // 选中态背景
                if (isSelected)
                {
                    Widgets.DrawBoxSolid(itemRect, new Color(0.25f, 0.25f, 0.25f, 1f));
                    // 左侧高亮条
                    var highlightRect = new Rect(rect.x + 4f, cursorY, 3f, NavItemHeight);
                    Widgets.DrawBoxSolid(highlightRect, new Color(0.4f, 0.7f, 1f, 1f));
                }

                // 导航项文本
                var labelRect = new Rect(rect.x + 16f, cursorY + 4f, rect.width - 32f, NavItemHeight - 8f);
                if (isSelected)
                {
                    Widgets.Label(labelRect, $"<color=#FFFFFF>{page.Label}</color>");
                }
                else
                {
                    Widgets.Label(labelRect, $"<color=#B0B0B0>{page.Label}</color>");
                }

                // 点击检测
                if (Widgets.ButtonInvisible(itemRect))
                {
                    _currentPage = page;
                    _scrollPosition = Vector2.zero; // 切换页面时重置滚动位置
                }

                cursorY += NavItemHeight;
            }
        }

        // ----------------------------------------------------------------
        // 内容区绘制
        // ----------------------------------------------------------------

        private void DrawContent(Rect rect)
        {
            // 内容区背景（浅色）
            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 1f));

            if (_currentPage == null) return;

            // 内边距
            var innerRect = new Rect(
                rect.x + ContentPadding,
                rect.y + ContentPadding,
                rect.width - ContentPadding * 2,
                rect.height - ContentPadding * 2
            );

            // 滚动视图
            var viewRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 1000f); // 预估高度
            Widgets.BeginScrollView(innerRect, ref _scrollPosition, viewRect);

            // 使用 Listing_Standard 布局
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // 页面标题
            Widgets.Label(listing.GetRect(24f), $"<size=18><b>{_currentPage.Label}</b></size>");
            listing.Gap(12f);

            // 绘制页面内容
            _currentPage.Draw(viewRect, listing);

            listing.End();
            Widgets.EndScrollView();
        }

        // ----------------------------------------------------------------
        // 底部状态栏
        // ----------------------------------------------------------------

        private void DrawStatusBar(Rect rect)
        {
            // 状态栏背景
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 1f));

            // 分隔线
            var lineRect = new Rect(rect.x, rect.y, rect.width, 1f);
            Widgets.DrawBoxSolid(lineRect, new Color(0.3f, 0.3f, 0.3f, 1f));

            // 状态文本（左对齐）
            var statusRect = new Rect(rect.x + 12f, rect.y + 4f, rect.width - 24f, rect.height - 8f);
            Widgets.Label(statusRect, $"<color=#888888><size=11>LLM 状态: ○ 未配置    会话 Token: 0</size></color>");
        }
    }
}
