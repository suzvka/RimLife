using UnityEngine;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// RimLife 配置面板浮动窗口。
    /// 委托 ConfigPanelLayout 绘制界面，可在游戏内通过自检工具打开。
    /// </summary>
    public class ConfigPanelWindow : Window
    {
        private readonly ConfigPanelLayout _layout;

        public ConfigPanelWindow()
        {
            doCloseButton = true;
            closeOnClickedOutside = true;
            draggable = true;
            resizeable = false;

            _layout = new ConfigPanelLayout();
        }

        public override Vector2 InitialSize => new Vector2(720f, 520f);

        public override void DoWindowContents(Rect inRect) => _layout.Draw(inRect);
    }
}
