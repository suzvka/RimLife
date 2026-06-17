using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// 调试页面 - 标准命令行终端风格的日志查看器。
    /// 日志直接增量追加到文本缓冲区，无过滤、无重建，纯追加式显示。
    /// </summary>
    public class DebugPage : IConfigPage
    {
        public string Id => "debug";
        public string Label => "调试";
        public string Group => "系统";
        public int Order => 99; // 最后显示

        private Vector2 _logScrollPosition;
        private bool _autoScroll = true;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            // 控制栏
            DrawControlBar(listing);
            listing.Gap(GapSmall);

            // 日志显示区
            DrawLogWindow(listing.GetRect(420f));
        }

        private void DrawControlBar(Listing_Standard listing)
        {
            var btnResults = DrawButtonRow(listing,
                new[] { "清空日志", _autoScroll ? "自动滚动: ON" : "自动滚动: OFF", "复制全部", "导出日志" },
                new[] { BtnWidthMedium, BtnWidthMedium + 20f, BtnWidthMedium, BtnWidthMedium });

            if (btnResults[0])
            {
                LogBuffer.Clear();
            }
            if (btnResults[1])
            {
                _autoScroll = !_autoScroll;
            }
            if (btnResults[2])
            {
                CopyAllLogs();
            }
            if (btnResults[3])
            {
                ExportLogs();
            }
        }

        private void DrawLogWindow(Rect rect)
        {
            // 终端窗口背景
            Widgets.DrawBoxSolid(rect, new Color(0.05f, 0.05f, 0.05f, 1f));

            // 内边距
            var innerRect = new Rect(rect.x + GapSmall, rect.y + GapSmall, rect.width - GapSmall * 2, rect.height - 40f);

            // 直接从增量文本缓冲区读取（无过滤、无重建）
            var logText = LogBuffer.GetText();
            if (string.IsNullOrEmpty(logText))
                logText = "<color=#555555>（暂无日志）</color>";

            var count = LogBuffer.Count;

            // 计算换行后的实际文本高度
            var textWidth = innerRect.width - 16f; // 留出滚动条空间
            var textHeight = Text.CalcHeight(logText, textWidth);
            var totalHeight = Mathf.Max(innerRect.height, textHeight + 10f);
            var viewRect = new Rect(innerRect.x, innerRect.y, textWidth, totalHeight);

            // 滚动视图
            Widgets.BeginScrollView(innerRect, ref _logScrollPosition, viewRect);

            // 以单个 Label 绘制整个文本块，Unity GUI 自动处理换行
            var textRect = new Rect(viewRect.x, viewRect.y, textWidth, textHeight);
            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, logText);
            Text.Anchor = originalAnchor;

            Widgets.EndScrollView();

            // 智能自动滚动：仅在用户处于底部附近时强制滚动到底部
            // 如果用户手动向上翻阅（距底部超过阈值），不干扰其阅读位置
            if (_autoScroll && count > 0)
            {
                var maxScrollY = totalHeight - innerRect.height;
                var nearBottomThreshold = 30f;

                if (maxScrollY <= 0 || _logScrollPosition.y >= maxScrollY - nearBottomThreshold)
                {
                    _logScrollPosition.y = maxScrollY;
                }
            }

            // 底部状态行
            var statusRect = new Rect(rect.x + GapSmall, rect.y + rect.height - 28f, rect.width - GapSmall * 2, 20f);
            Widgets.Label(statusRect, $"<color=#888888><size=11>共 {count} 条日志 | 缓冲区: {count}/{500}</size></color>");
        }

        private void CopyAllLogs()
        {
            var logText = LogBuffer.GetText();
            if (string.IsNullOrEmpty(logText))
            {
                Messages.Message("没有日志可复制", MessageTypeDefOf.NeutralEvent);
                return;
            }

            var plainText = StripRichText(logText);
            GUIUtility.systemCopyBuffer = plainText;
            Messages.Message($"已复制 {LogBuffer.Count} 条日志到剪贴板", MessageTypeDefOf.NeutralEvent);
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

        /// <summary>
        /// 移除 Rich Text 标签，返回纯文本。
        /// </summary>
        private static string StripRichText(string richText)
        {
            if (string.IsNullOrEmpty(richText)) return "";
            
            // 移除所有 <color=...> 和 </color> 标签
            string plain = System.Text.RegularExpressions.Regex.Replace(
                richText, 
                @"<color=[^>]*>|</color>", 
                ""
            );
            
            return plain;
        }
    }
}
