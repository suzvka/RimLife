using NPCLife.Framework;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// 运行时仪表盘页面。展示框架层使用统计和 Agent 运行状态。
    /// 仅在游戏内加载存档后可用；主菜单时显示提示信息。
    /// </summary>
    /// <remarks>
    /// 所有度量数据来自 NPCLife.Framework.RuntimeMetrics（框架层，与具体 Agent 无关）。
    /// </remarks>
    public class DashboardPage : IConfigPage
    {
        public string Id => "dashboard";
        public string Label => "消耗统计";
        public string Group => "高级";
        public int Order => 0; // 高级分组内第一位

        // 状态消息
        private string _statusMessage;
        private float _statusMessageTime;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            // 无存档时：运行时仪表盘不适用
            if (RimLifeCore.SaveStore == null)
            {
                var hintRect = listing.GetRect(40f);
                Widgets.Label(hintRect, "<color=#888888><size=13>运行时仪表盘仅在游戏内可用。<br>请加载或新建存档后查看。</size></color>");
                return;
            }

            // ================================================================
            // Agent 状态
            // ================================================================
            BeginSection(listing, "Agent 状态");
            DrawStatusRow(listing, "导演", GetDirectorStatus());
            DrawStatusRow(listing, "即兴编剧", GetImproviserStatus());
            EndSection(listing);

            // ================================================================
            // LLM 使用统计（框架层 RuntimeMetrics）
            // ================================================================
            var snap = RimLifeCore.FrameworkFactory.Metrics.GetSnapshot();

            BeginSection(listing, "LLM 使用");
            DrawInfoRow(listing, "总会话", $"{snap.TotalSessions}（活跃 {snap.ActiveSessions}）");
            listing.Gap(GapTiny);

            if (snap.Tokens != null)
            {
                DrawInfoRow(listing, "输入 Token", FormatToken(snap.Tokens.TotalInput));
                DrawInfoRow(listing, "输出 Token", FormatToken(snap.Tokens.TotalOutput));
                if (snap.Tokens.TotalCacheRead > 0)
                    DrawInfoRow(listing, "缓存命中", FormatToken(snap.Tokens.TotalCacheRead));

                // 按角色分
                if (snap.Tokens.LlmCallsByRole != null)
                {
                    listing.Gap(GapTiny);
                    foreach (var kv in snap.Tokens.LlmCallsByRole)
                    {
                        if (kv.Value > 0)
                        {
                            var roleLabel = RoleLabel(kv.Key);
                            var input = snap.Tokens.InputByRole?.TryGetValue(kv.Key, out var iv) == true ? iv : 0;
                            var output = snap.Tokens.OutputByRole?.TryGetValue(kv.Key, out var ov) == true ? ov : 0;
                            DrawInfoRow(listing, $"  {roleLabel}", $"调用 {kv.Value} 次，入 {FormatToken(input)} / 出 {FormatToken(output)}");
                        }
                    }
                }
            }
            EndSection(listing);

            // ================================================================
            // Agent 循环统计
            // ================================================================
            if (snap.AgentLoops != null && snap.AgentLoops.TotalActivations > 0)
            {
                BeginSection(listing, "Agent 循环");
                DrawInfoRow(listing, "总激活", snap.AgentLoops.TotalActivations.ToString());
                DrawInfoRow(listing, "总轮次", snap.AgentLoops.TotalRounds.ToString());
                DrawInfoRow(listing, "均轮次", $"{snap.AgentLoops.AvgRoundsPerActivation:F1}");
                DrawInfoRow(listing, "处理事件", snap.AgentLoops.TotalEventsProcessed.ToString());
                if (snap.AgentLoops.TotalErrors > 0)
                    DrawInfoRow(listing, "错误", $"<color=#FF6666>{snap.AgentLoops.TotalErrors}</color>");

                if (snap.AgentLoops.ActivationsByRole != null)
                {
                    listing.Gap(GapTiny);
                    foreach (var kv in snap.AgentLoops.ActivationsByRole)
                    {
                        if (kv.Value > 0)
                            DrawInfoRow(listing, $"  {RoleLabel(kv.Key)}", $"{kv.Value} 次");
                    }
                }
                EndSection(listing);
            }

            // ================================================================
            // 工具调用
            // ================================================================
            if (snap.Tools != null && snap.Tools.Count > 0)
            {
                BeginSection(listing, "工具调用");
                int shown = 0;
                foreach (var tool in snap.Tools)
                {
                    if (shown >= 10) break; // 最多显示 10 个
                    var errText = tool.Errors > 0 ? $" <color=#FF6666>({tool.Errors} 错)</color>" : "";
                    DrawInfoRow(listing, tool.Name, $"{tool.Calls} 次{errText}");
                    shown++;
                }
                if (snap.Tools.Count > 10)
                {
                    var moreRect = listing.GetRect(18f);
                    Widgets.Label(moreRect, $"<color=#888888><size=11>... 还有 {snap.Tools.Count - 10} 个工具</size></color>");
                }
                EndSection(listing);
            }

            // ================================================================
            // 知识库
            // ================================================================
            if (snap.Knowledge != null && snap.Knowledge.TotalBatches > 0)
            {
                BeginSection(listing, "知识库");
                DrawInfoRow(listing, "查询批次", snap.Knowledge.TotalBatches.ToString());
                if (snap.Knowledge.Terms != null && snap.Knowledge.Terms.Count > 0)
                {
                    listing.Gap(GapTiny);
                    int termShown = 0;
                    foreach (var term in snap.Knowledge.Terms)
                    {
                        if (termShown >= 5) break;
                        var hitInfo = term.HitCount > 0 ? $"命中 {term.HitCount}" : "未命中";
                        DrawInfoRow(listing, $"  {term.Term}", $"{term.TotalAccesses} 次 ({hitInfo})");
                        termShown++;
                    }
                }
                EndSection(listing);
            }

            // ================================================================
            // 操作
            // ================================================================
            var btnResults = DrawButtonRow(listing,
                new[] { "重置度量", "刷新" },
                new[] { BtnWidthMedium, BtnWidthSmall });

            if (btnResults[0])
            {
                RimLifeCore.FrameworkFactory.Metrics.Reset();
                _statusMessage = "运行时度量已重置";
                _statusMessageTime = Time.time;
            }
            // 刷新是 no-op（IMGUI 每帧自动刷新），仅提供视觉反馈
            if (btnResults[1])
            {
                _statusMessage = "已刷新";
                _statusMessageTime = Time.time;
            }

            // 状态消息
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static string GetDirectorStatus()
        {
            return RimLifeCore.GetDirectorAgent() != null ? "active" : "idle";
        }

        private static string GetImproviserStatus()
        {
            return RimLifeCore.GetImproviserAgent() != null ? "active" : "idle";
        }

        private static string RoleLabel(string key)
        {
            switch (key?.ToLowerInvariant())
            {
                case "director": return "导演";
                case "screenwriter": return "编剧";
                case "improviser": return "即兴编剧";
                default: return key ?? "?";
            }
        }

        private static string FormatToken(int tokens)
        {
            if (tokens >= 1_000_000)
                return $"{(tokens / 1_000_000.0):F1}M";
            if (tokens >= 1_000)
                return $"{(tokens / 1000.0):F1}K";
            return tokens.ToString();
        }
    }
}
