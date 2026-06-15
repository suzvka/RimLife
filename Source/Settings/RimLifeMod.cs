using System.Collections.Generic;
using System.Linq;
using RimLife.UI;
using RimLife.UI.Pages;
using UnityEngine;
using Verse;

namespace RimLife.Settings
{
    /// <summary>
    /// RimLife 模组设置。
    /// 直接在 Mod Settings 窗口中显示完整的配置界面。
    /// </summary>
    public class RimLifeModSettings : ModSettings
    {
        // 预留：未来可以添加持久化设置项
        // public bool EnableMetrics = true;
        // public string ApiKey = "";

        public override void ExposeData()
        {
            base.ExposeData();
            // Scribe_Values.Look(ref EnableMetrics, "enableMetrics", true);
            // Scribe_Values.Look(ref ApiKey, "apiKey", "");
        }
    }

    /// <summary>
    /// RimLife Mod 主类。
    /// 直接在 Settings 窗口中绘制配置面板。
    /// </summary>
    public class RimLifeMod : Mod
    {
        private static RimLifeModSettings _settings;
        
        // 配置页面列表
        private List<IConfigPage> _pages;
        private IConfigPage _currentPage;
        private Vector2 _scrollPosition;

        // 常量（与 ConfigPanelWindow 保持一致）
        private const float SidebarWidth = 140f;
        private const float ContentPadding = 16f;
        private const float GroupHeaderHeight = 20f;
        private const float NavItemHeight = 32f;
        private const float StatusBarHeight = 28f; // 增加高度避免截断

        public RimLifeMod(ModContentPack content) : base(content)
        {
            _settings = GetSettings<RimLifeModSettings>();
            
            // 初始化页面
            _pages = new List<IConfigPage>
            {
                new ConnectionPage(),
                new NarrativePage(),
                new KnowledgePage(),
                new AdvancedPage(),
                new DebugPage() // 调试页面
            };
            
            // 默认选中第一个页面
            _currentPage = _pages.FirstOrDefault();
            
            // 排序页面（按 Order）
            _pages = _pages.OrderBy(p => p.Order).ToList();
        }

        public override string SettingsCategory()
        {
            return "RimLife";
        }

        public override void DoSettingsWindowContents(Rect inRect)
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

            // 状态文本（左对齐，减小字号避免截断）
            var statusRect = new Rect(rect.x + 12f, rect.y + 6f, rect.width - 24f, rect.height - 12f);
            Widgets.Label(statusRect, $"<color=#AAAAAA><size=10>LLM 状态: ○ 未配置    会话 Token: 0</size></color>");
        }
    }
}
