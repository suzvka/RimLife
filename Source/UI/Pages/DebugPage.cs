using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// 调试页面 - 命令行窗口风格的日志查看器。
    /// </summary>
    public class DebugPage : IConfigPage
    {
        public string Id => "debug";
        public string Label => "调试";
        public string Group => "系统";
        public int Order => 99; // 最后显示

        private Vector2 _logScrollPosition;
        private bool _autoScroll = true;
        private bool _showInfo = true;
        private bool _showWarning = true;
        private bool _showError = true;
        private bool _showMessage = true;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            // 控制栏
            DrawControlBar(listing);
            listing.Gap(8f);

            // 日志显示区（类似终端窗口）
            DrawLogWindow(listing.GetRect(400f));
        }

        private void DrawControlBar(Listing_Standard listing)
        {
            // 过滤器复选框
            var filterRow = listing.GetRect(24f);
            var checkboxWidth = 100f;
            
            var infoRect = new Rect(filterRow.x, filterRow.y, checkboxWidth, 24f);
            var warnRect = new Rect(filterRow.x + checkboxWidth, filterRow.y, checkboxWidth, 24f);
            var errorRect = new Rect(filterRow.x + checkboxWidth * 2, filterRow.y, checkboxWidth, 24f);
            var msgRect = new Rect(filterRow.x + checkboxWidth * 3, filterRow.y, checkboxWidth, 24f);

            Widgets.CheckboxLabeled(infoRect, "INFO", ref _showInfo);
            Widgets.CheckboxLabeled(warnRect, "WARN", ref _showWarning);
            Widgets.CheckboxLabeled(errorRect, "ERROR", ref _showError);
            Widgets.CheckboxLabeled(msgRect, "MSG", ref _showMessage);

            listing.Gap(4f);

            // 操作按钮行
            var buttonRow = listing.GetRect(30f);
            var btnWidth = 120f;
            var btnGap = 8f;

            // 清空日志
            if (Widgets.ButtonText(new Rect(buttonRow.x, buttonRow.y, btnWidth, 30f), "清空日志"))
            {
                LogBuffer.Clear();
            }

            // 自动滚动开关
            if (Widgets.ButtonText(new Rect(buttonRow.x + btnWidth + btnGap, buttonRow.y, btnWidth, 30f), 
                _autoScroll ? "自动滚动: ON" : "自动滚动: OFF"))
            {
                _autoScroll = !_autoScroll;
            }

            // 导出日志
            if (Widgets.ButtonText(new Rect(buttonRow.x + (btnWidth + btnGap) * 2, buttonRow.y, btnWidth, 30f), "导出日志"))
            {
                ExportLogs();
            }
        }

        private void DrawLogWindow(Rect rect)
        {
            // 终端窗口背景
            Widgets.DrawBoxSolid(rect, new Color(0.05f, 0.05f, 0.05f, 1f));
            
            // 内边距（增加底部空间避免状态行被截断）
            var innerRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 40f);

            // 获取过滤后的日志条目
            var entries = GetFilteredEntries();

            // 计算总高度（每行约 18px，留出更多垂直空间）
            var lineHeight = 18f;
            var totalHeight = entries.Count * lineHeight + 10f;
            var viewRect = new Rect(innerRect.x, innerRect.y, innerRect.width - 16f, totalHeight);

            // 滚动视图
            Widgets.BeginScrollView(innerRect, ref _logScrollPosition, viewRect);

            // 绘制日志条目
            var cursorY = viewRect.y;
            foreach (var entry in entries)
            {
                var color = GetLogColor(entry.Type);
                var timestamp = entry.Timestamp.ToString("HH:mm:ss");
                var text = $"<color={ColorUtility.ToHtmlStringRGB(color)}>[{timestamp}] {entry.Message}</color>";
                
                // 使用 TextAnchor.UpperLeft 确保文本从顶部对齐
                var lineRect = new Rect(viewRect.x, cursorY, viewRect.width, lineHeight);
                var originalAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(lineRect, text);
                Text.Anchor = originalAnchor;
                
                cursorY += lineHeight;
            }

            Widgets.EndScrollView();

            // 如果启用自动滚动，滚动到底部
            if (_autoScroll && entries.Count > 0)
            {
                _logScrollPosition.y = totalHeight - innerRect.height;
            }

            // 底部状态行（放在 scrollview 外面，固定在窗口底部）
            var statusRect = new Rect(rect.x + 8f, rect.y + rect.height - 28f, rect.width - 16f, 20f);
            Widgets.Label(statusRect, $"<color=#888888><size=11>共 {entries.Count} 条日志 | 缓冲区容量: {LogBuffer.Count}/{500}</size></color>");
        }

        private List<LogEntry> GetFilteredEntries()
        {
            var allEntries = LogBuffer.GetEntries();
            var filtered = new List<LogEntry>();

            foreach (var entry in allEntries)
            {
                switch (entry.Type)
                {
                    case LogMessageType.Info when _showInfo:
                    case LogMessageType.Warning when _showWarning:
                    case LogMessageType.Error when _showError:
                    case LogMessageType.Message when _showMessage:
                        filtered.Add(entry);
                        break;
                }
            }

            return filtered;
        }

        private Color GetLogColor(LogMessageType type)
        {
            switch (type)
            {
                case LogMessageType.Info:
                    return new Color(0.7f, 0.7f, 0.7f); // 灰色
                case LogMessageType.Warning:
                    return new Color(1f, 0.8f, 0.2f); // 黄色
                case LogMessageType.Error:
                    return new Color(1f, 0.3f, 0.3f); // 红色
                case LogMessageType.Message:
                    return new Color(0.9f, 0.9f, 0.9f); // 白色
                default:
                    return Color.white;
            }
        }

        private void ExportLogs()
        {
            var entries = LogBuffer.GetEntries();
            if (entries.Count == 0)
            {
                Messages.Message("没有日志可导出", MessageTypeDefOf.NeutralEvent);
                return;
            }

            // 构建导出文本
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== RimLife Debug Log Export ===");
            sb.AppendLine($"Exported at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total entries: {entries.Count}");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            foreach (var entry in entries)
            {
                var prefix = entry.Type switch
                {
                    LogMessageType.Info => "[INFO]",
                    LogMessageType.Warning => "[WARN]",
                    LogMessageType.Error => "[ERROR]",
                    LogMessageType.Message => "[MSG]",
                    _ => "[???]"
                };
                
                sb.AppendLine($"{prefix} [{entry.Timestamp:HH:mm:ss}] {entry.Message}");
            }

            // 复制到剪贴板
            GUIUtility.systemCopyBuffer = sb.ToString();
            Messages.Message($"已复制 {entries.Count} 条日志到剪贴板", MessageTypeDefOf.NeutralEvent);
        }
    }
}
