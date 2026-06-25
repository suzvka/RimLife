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

        /// <summary>日志区固定高度。独立滚动需要固定尺寸。</summary>
        private const float LogAreaHeight = 420f;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            // 控制栏
            DrawControlBar(listing);
            listing.Gap(GapSmall);

            // 使用 LayoutHelper 从父 listing 分配固定高度子区域，
            // 父 ScrollView 的 viewRect 会正确包含此高度，避免截断。
            var outerRect = LayoutHelper.AllocateSubScrollRegion(listing, LogAreaHeight, out var innerRect);

            // 终端窗口背景
            Widgets.DrawBoxSolid(outerRect, new Color(0.05f, 0.05f, 0.05f, 1f));

            // 日志内容区（减去底部状态行空间）
            var contentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, innerRect.height - 28f);

            // 直接从增量文本缓冲区读取（无过滤、无重建）
            var logText = LogBuffer.GetText();
            if (string.IsNullOrEmpty(logText))
                logText = "<color=#555555>（暂无日志）</color>";

            var count = LogBuffer.Count;

            // 计算换行后的实际文本高度
            var textWidth = contentRect.width - 16f; // 留出滚动条空间
            var textHeight = Text.CalcHeight(logText, textWidth);
            var totalHeight = Mathf.Max(contentRect.height, textHeight + 10f);
            var viewRect = new Rect(contentRect.x, contentRect.y, textWidth, totalHeight);

            // 内层独立 ScrollView（在父 ScrollView 分配的固定高度区域内）
            Widgets.BeginScrollView(contentRect, ref _logScrollPosition, viewRect);

            var textRect = new Rect(viewRect.x, viewRect.y, textWidth, textHeight);
            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, logText);
            Text.Anchor = originalAnchor;

            Widgets.EndScrollView();

            // 智能自动滚动：仅在用户处于底部附近时强制滚动到底部
            if (_autoScroll && count > 0)
            {
                var maxScrollY = totalHeight - contentRect.height;
                var nearBottomThreshold = 30f;
                if (maxScrollY <= 0 || _logScrollPosition.y >= maxScrollY - nearBottomThreshold)
                    _logScrollPosition.y = maxScrollY;
            }

            // 底部状态行
            var statusRect = new Rect(innerRect.x, innerRect.y + innerRect.height - 24f, innerRect.width, 20f);
            Widgets.Label(statusRect, $"<color=#888888><size=11>共 {count} 条日志 | 缓冲区: {count}/{500}</size></color>");
        }

        private void DrawControlBar(Listing_Standard listing)
        {
            // 4 个按钮需要适配可用宽度，使用较小尺寸避免换行
            var btnW = 100f;
            var btnResults = DrawButtonRow(listing,
                new[] { "清空日志", _autoScroll ? "自动滚动: ON" : "自动滚动: OFF", "复制全部", "导出日志" },
                new[] { btnW, btnW + 30f, btnW, btnW });

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
