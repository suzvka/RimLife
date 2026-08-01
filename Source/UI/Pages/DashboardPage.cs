using RimLife.Infrastructure;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// 仪表盘页面 — 会话轮次追踪卡片 + 全文交互详情。
    /// 上半部分：紧凑统计摘要 + RunTrace 卡片列表（可选择）。
    /// 下半部分：选中 Run 的完整交互记录（只读文本框）。
    /// </summary>
    public class DashboardPage : IConfigPage
    {
        public string Id => "dashboard";
        public string Label => "会话追踪";
        public string Group => "高级";
        public int Order => 0;

        // ---- UI 状态 ----
        private string _selectedRunId;
        private Vector2 _detailScrollPosition;
        private string _statusMessage;
        private float _statusMessageTime;

        /// <summary>详情区固定高度。</summary>
        private const float DetailAreaHeight = 450f;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            if (RimLifeCore.SaveStore == null)
            {
                var hintRect = listing.GetRect(40f);
                Widgets.Label(hintRect,
                    "<color=#888888><size=13>会话追踪仅在游戏内可用。<br>请加载或新建存档后查看。</size></color>");
                return;
            }

            // ================================================================
            // 控制栏
            // ================================================================
            DrawControlBar(listing);
            listing.Gap(GapSmall);

            // ================================================================
            // 紧凑统计摘要
            // ================================================================
            DrawStatsSummary(listing);
            listing.Gap(GapSmall);

            // ================================================================
            // 会话卡片列表
            // ================================================================
            DrawTraceCards(listing);
            listing.Gap(GapSmall);

            // ================================================================
            // 详情文本区
            // ================================================================
            DrawDetailArea(listing);

            // 状态消息
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
        }

        // ================================================================
        // 控制栏
        // ================================================================

        private void DrawControlBar(Listing_Standard listing)
        {
            var enabledLabel = SessionTraceStore.Enabled ? "追踪: ON" : "追踪: OFF";
            var btnResults = DrawButtonRow(listing,
                new[] { enabledLabel, "清空追踪", "复制详情" },
                new[] { BtnWidthMedium, BtnWidthMedium, BtnWidthMedium });

            if (btnResults[0])
                SessionTraceStore.Enabled = !SessionTraceStore.Enabled;

            if (btnResults[1])
            {
                SessionTraceStore.Clear();
                _selectedRunId = null;
                _statusMessage = "追踪已清空";
                _statusMessageTime = Time.time;
            }

            if (btnResults[2])
                CopySelectedDetail();
        }

        // ================================================================
        // 紧凑统计摘要
        // ================================================================

        private void DrawStatsSummary(Listing_Standard listing)
        {
            BeginSection(listing, "统计摘要");

            var snap = RimLifeCore.FrameworkFactory.Metrics.GetSnapshot();

            // 一行显示关键指标
            var rowRect = listing.GetRect(22f);
            var sessionsText = $"会话: {snap.TotalSessions}";
            var tokensText = $"Token: {FormatToken(snap.Tokens?.TotalInput ?? 0)}↑ {FormatToken(snap.Tokens?.TotalOutput ?? 0)}↓";
            var tracesText = $"追踪: {SessionTraceStore.Count}/{SessionTraceStore.MaxTraces}";
            var loopsText = snap.AgentLoops != null
                ? $"循环: {snap.AgentLoops.TotalActivations} 次 / {snap.AgentLoops.TotalRounds} 轮"
                : "循环: 0";

            Widgets.Label(rowRect,
                $"<color=#888888><size=12>{sessionsText}  |  {tokensText}  |  {loopsText}  |  {tracesText}</size></color>");

            EndSection(listing);
        }

        // ================================================================
        // 会话卡片列表
        // ================================================================

        private void DrawTraceCards(Listing_Standard listing)
        {
            BeginSection(listing, "会话记录");

            var traces = SessionTraceStore.GetAll();
            if (traces.Count == 0)
            {
                var emptyRect = listing.GetRect(30f);
                Widgets.Label(emptyRect, "<color=#555555><size=12>暂无会话记录。Agent 运行后将自动追踪。</size></color>");
            }
            else
            {
                foreach (var trace in traces)
                {
                    DrawTraceCard(listing, trace);
                }
            }

            EndSection(listing);
        }

        private void DrawTraceCard(Listing_Standard listing, RunTrace trace)
        {
            bool isSelected = trace.RunId == _selectedRunId;

            // 卡片高度：标题 + 两行信息
            var cardHeight = 62f;
            listing.Gap(GapTiny);
            var cardRect = listing.GetRect(cardHeight);

            // 背景 + 边框
            var bgColor = isSelected
                ? new Color(ColorHighlight.r, ColorHighlight.g, ColorHighlight.b, 0.15f)
                : ColorCardBg;
            Widgets.DrawBoxSolid(cardRect, bgColor);
            Widgets.DrawBox(cardRect, 1);

            // 标题行 — 展示角色 + workspace 标签以区分同一 run 内的不同 agent
            var titleRect = new Rect(cardRect.x + GapSmall, cardRect.y + 4f, cardRect.width - GapSmall * 2, 20f);
            var statusIcon = trace.NormalCompletion ? "●" : "✖";
            var statusColor = trace.NormalCompletion ? "#66CC66" : "#FF6666";
            string wsShort = "";
            if (!string.IsNullOrEmpty(trace.WorkspaceLabel))
                wsShort = trace.WorkspaceLabel;
            else if (!string.IsNullOrEmpty(trace.WorkspaceId))
                wsShort = trace.WorkspaceId.Length > 8 ? trace.WorkspaceId.Substring(0, 8) : trace.WorkspaceId;
            Widgets.Label(titleRect,
                $"<color={statusColor}>{statusIcon}</color>  " +
                $"<b>{trace.RunId}</b>  " +
                $"<color=#AAAAAA>[{trace.Role}]</color>  " +
                (!string.IsNullOrEmpty(wsShort) ? $"<color=#888888>{wsShort}</color>  " : "") +
                $"<color=#888888>{trace.StartTime:HH:mm:ss}</color>");

            // 信息行
            var infoRect = new Rect(cardRect.x + GapSmall, cardRect.y + 24f, cardRect.width - GapSmall * 2, 18f);
            Widgets.Label(infoRect,
                $"<color=#888888><size=11>" +
                $"事件: {trace.EventsProcessed}  |  " +
                $"轮次: {trace.TotalRounds}  |  " +
                $"Token: {FormatToken(trace.TotalInputTokens)}↑ {FormatToken(trace.TotalOutputTokens)}↓  |  " +
                $"耗时: {trace.DurationMs}ms" +
                $"</size></color>");

            // 轮次细节行
            var detailRect = new Rect(cardRect.x + GapSmall, cardRect.y + 42f, cardRect.width - GapSmall * 2, 16f);
            var toolCount = 0;
            foreach (var r in trace.Rounds) toolCount += r.ToolCalls.Count;
            Widgets.Label(detailRect,
                $"<color=#666666><size=10>" +
                $"LLM 调用: {trace.Rounds.Count} 次  |  " +
                $"工具调用: {toolCount} 次  |  " +
                $"触发事件: {trace.Events.Count} 个" +
                $"</size></color>");

            // 点击选择
            if (Widgets.ButtonInvisible(cardRect))
            {
                _selectedRunId = trace.RunId;
                _detailScrollPosition = Vector2.zero;
            }

            // Hover
            DrawHoverBackground(cardRect);
        }

        // ================================================================
        // 详情文本区
        // ================================================================

        private void DrawDetailArea(Listing_Standard listing)
        {
            BeginSection(listing, "交互详情");

            var outerRect = LayoutHelper.AllocateSubScrollRegion(listing, DetailAreaHeight, out var innerRect);

            // 终端风格背景
            Widgets.DrawBoxSolid(outerRect, new Color(0.06f, 0.06f, 0.06f, 1f));

            var detailText = BuildDetailText();
            if (string.IsNullOrEmpty(detailText))
                detailText = "<color=#555555>（选择一个会话记录查看详情）</color>";

            var textWidth = innerRect.width - 16f;
            var textHeight = CalcTextHeight(detailText, textWidth);
            var totalHeight = Mathf.Max(innerRect.height, textHeight + 10f);
            var viewRect = new Rect(innerRect.x, innerRect.y, textWidth, totalHeight);

            var contentRect = new Rect(innerRect.x, innerRect.y, innerRect.width, innerRect.height);
            Widgets.BeginScrollView(contentRect, ref _detailScrollPosition, viewRect);

            var textRect = new Rect(viewRect.x, viewRect.y, textWidth, textHeight);
            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, detailText);
            Text.Anchor = originalAnchor;

            Widgets.EndScrollView();
        }

        // ================================================================
        // 详情文本构建
        // ================================================================

        private string BuildDetailText()
        {
            if (_selectedRunId == null) return null;
            var trace = SessionTraceStore.GetByRunId(_selectedRunId);
            if (trace == null) return null;

            var sb = new StringBuilder();

            // 头部 — 按 agent 分界，突出 workspace
            string wsTag = "";
            if (!string.IsNullOrEmpty(trace.WorkspaceId))
            {
                wsTag = string.IsNullOrEmpty(trace.WorkspaceLabel)
                    ? $"ws={trace.WorkspaceId}"
                    : $"ws={trace.WorkspaceLabel}({trace.WorkspaceId})";
            }
            sb.AppendLine($"<color=#CCCCCC><size=13><b>═══ {trace.RunId}  [{trace.Role}]  {wsTag}  {trace.StartTime:HH:mm:ss} ═══</b></size></color>");
            sb.AppendLine($"<color=#888888>事件: {trace.EventsProcessed} | 轮次: {trace.TotalRounds} | Token: {trace.TotalInputTokens}↑ {trace.TotalOutputTokens}↓ | 耗时: {trace.DurationMs}ms | 完成: {(trace.NormalCompletion ? "正常" : "异常")}</color>");
            sb.AppendLine();

            // 触发事件
            if (trace.Events.Count > 0)
            {
                sb.AppendLine("<color=#AAAAFF><b>── 触发事件 ──</b></color>");
                foreach (var evt in trace.Events)
                {
                    sb.AppendLine($"  <color=#888888>[{evt.Type}]</color> {evt.EventId} <color=#666666>(重要度: {evt.Importance:F1})</color>");
                }
                sb.AppendLine();
            }

            // Prompt
            if (!string.IsNullOrEmpty(trace.UserMessage))
            {
                sb.AppendLine("<color=#AAFFAA><b>── 用户消息 (Prompt) ──</b></color>");
                sb.AppendLine($"<color=#CCCCCC>{trace.UserMessage}</color>");
                sb.AppendLine();
            }

            // LLM 交互轮次
            for (int i = 0; i < trace.Rounds.Count; i++)
            {
                var round = trace.Rounds[i];
                sb.AppendLine($"<color=#FFDD88><b>── LLM 轮次 {round.RoundIndex} ──</b></color>");
                sb.AppendLine($"<color=#888888>模型: {round.Model} | 输入: {round.InputTokens} | 输出: {round.OutputTokens}" +
                    (round.CacheReadTokens > 0 ? $" | 缓存: {round.CacheReadTokens}" : "") +
                    $" | 结束: {round.FinishReason}</color>");

                // 请求消息
                if (round.RequestMessages.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  <color=#AAAAAA><b>请求消息:</b></color>");
                    foreach (var msg in round.RequestMessages)
                    {
                        var roleColor = GetRoleColor(msg.Role);
                        sb.AppendLine($"  <color={roleColor}>[{msg.Role}]</color>");
                        if (!string.IsNullOrEmpty(msg.Content))
                            sb.AppendLine($"  <color=#BBBBBB>{msg.Content}</color>");
                        if (!string.IsNullOrEmpty(msg.ToolCallsJson))
                            sb.AppendLine($"  <color=#FF9999>工具调用: {msg.ToolCallsJson}</color>");
                        sb.AppendLine();
                    }
                }

                // 响应
                if (!string.IsNullOrEmpty(round.ResponseContent))
                {
                    sb.AppendLine("  <color=#FFCC44><b>响应:</b></color>");
                    sb.AppendLine($"  <color=#FFFFFF>{round.ResponseContent}</color>");
                    sb.AppendLine();
                }

                // 工具调用
                if (round.ToolCalls.Count > 0)
                {
                    sb.AppendLine("  <color=#88CCFF><b>工具调用:</b></color>");
                    foreach (var tc in round.ToolCalls)
                    {
                        var statusTag = tc.Cancelled ? " <color=#FF6666>[已取消]</color>" : "";
                        sb.AppendLine($"  <color=#88CCFF>▸ {tc.ToolName}</color>{statusTag}");
                        sb.AppendLine($"    <color=#999999>参数: {tc.Arguments}</color>");
                        if (!string.IsNullOrEmpty(tc.Result))
                            sb.AppendLine($"    <color=#AAAAAA>结果: {tc.Result}</color>");
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        private static string GetRoleColor(string role)
        {
            switch (role?.ToLowerInvariant())
            {
                case "system": return "#FF8888";
                case "user": return "#88FF88";
                case "assistant": return "#FFCC44";
                case "tool": return "#88CCFF";
                default: return "#AAAAAA";
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private void CopySelectedDetail()
        {
            var text = BuildDetailText();
            if (string.IsNullOrEmpty(text))
            {
                Messages.Message("没有可复制的内容", MessageTypeDefOf.NeutralEvent);
                return;
            }

            var plainText = System.Text.RegularExpressions.Regex.Replace(
                text, @"<color=[^>]*>|</color>|<size=[^>]*>|</size>|<b>|</b>", "");
            GUIUtility.systemCopyBuffer = plainText;
            _statusMessage = "已复制到剪贴板";
            _statusMessageTime = Time.time;
        }

        private static string FormatToken(int tokens)
        {
            if (tokens >= 1_000_000) return $"{(tokens / 1_000_000.0):F1}M";
            if (tokens >= 1_000) return $"{(tokens / 1000.0):F1}K";
            return tokens.ToString();
        }
    }
}
