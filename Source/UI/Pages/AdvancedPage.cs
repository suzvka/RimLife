using NPCLife.Framework;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 高级页面：功能开关、诊断、Agent 状态、运行时度量、配置导入导出。
    /// 所有控件直接读写 FrameworkConfig，保存后通过 Configure() 生效。
    /// </summary>
    public class AdvancedPage : IConfigPage
    {
        public string Id => "advanced";
        public string Label => "高级";
        public string Group => "系统";
        public int Order => 0;

        // 本地编辑缓冲（首次 Draw 从 Config 初始化）
        private bool _initialized;

        // Features
        private bool _enableMetrics;
        private bool _enableDirectorAgent;
        private bool _enableMemoryConsolidation;
        private bool _enableKnowledgeBase;
        private bool _enableImproviserAgent;

        // Diagnostics
        private bool _enableVerboseLogging;
        private bool _enableToolCallTracing;
        private bool _enableEventTracing;

        // 状态消息
        private string _statusMessage;
        private float _statusMessageTime;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            InitializeIfNeeded();

            // ---- 功能开关 ----
            BeginSection(listing, "功能开关");
            listing.CheckboxLabeled("启用运行时度量", ref _enableMetrics, "Token 消耗、工具调用统计");
            listing.CheckboxLabeled("启用导演 Agent", ref _enableDirectorAgent, "导演自动审查事件并分配剧情线");
            listing.CheckboxLabeled("启用编剧 Agent", ref _enableMemoryConsolidation, "记忆巩固（跨轮次上下文）");
            listing.CheckboxLabeled("启用知识库", ref _enableKnowledgeBase, "事件关键词匹配 → 注入背景知识");
            listing.CheckboxLabeled("启用即兴编剧 Agent", ref _enableImproviserAgent, "处理独立临时事件");
            listing.Gap(GapTiny);
            EndSection(listing);

            // ---- 诊断 ----
            BeginSection(listing, "诊断");
            listing.CheckboxLabeled("详细日志", ref _enableVerboseLogging, "输出完整 LLM 请求/响应到日志");
            listing.CheckboxLabeled("工具调用追踪", ref _enableToolCallTracing, "记录每次工具调用的参数和结果");
            listing.CheckboxLabeled("事件总线追踪", ref _enableEventTracing, "记录所有事件发布/订阅轨迹");
            EndSection(listing);

            // ---- 保存 ----
            var saveResults = DrawButtonRow(listing,
                new[] { "保存并应用", "重置默认值" },
                new[] { BtnWidthLarge, BtnWidthMedium });

            if (saveResults[0])
            {
                var config = CloneCurrentConfig();
                if (config != null)
                {
                    config.Features.EnableRuntimeMetrics = _enableMetrics;
                    config.Features.EnableDirectorAgent = _enableDirectorAgent;
                    config.Features.EnableMemoryConsolidation = _enableMemoryConsolidation;
                    config.Features.EnableKnowledgeBase = _enableKnowledgeBase;
                    config.Features.EnableImproviserAgent = _enableImproviserAgent;
                    config.Diagnostics.EnableVerboseLogging = _enableVerboseLogging;
                    config.Diagnostics.EnableToolCallTracing = _enableToolCallTracing;
                    config.Diagnostics.EnableEventTracing = _enableEventTracing;
                    RimLifeCore.Configure(config);
                    _statusMessage = "配置已保存并生效";
                    _statusMessageTime = Time.time;
                    Log.Message("[RimLife.UI] Advanced settings saved");
                }
                else
                {
                    _statusMessage = "保存失败：无法克隆当前配置";
                    _statusMessageTime = Time.time;
                }
            }

            if (saveResults[1])
            {
                InitializeFromDefaults();
                _statusMessage = "已重置（需保存生效）";
                _statusMessageTime = Time.time;
            }

            listing.Gap(GapSmall);

            // ---- Agent 状态 ----
            BeginSection(listing, "Agent 状态");
            var directorAgent = GetAgentStatus("director");
            var screenwriterStatus = GetAgentStatus("screenwriter");
            var improviserStatus = GetAgentStatus("improviser");
            DrawStatusRow(listing, "导演", directorAgent);
            DrawStatusRow(listing, "剧情编剧", screenwriterStatus);
            DrawStatusRow(listing, "即兴编剧", improviserStatus);
            EndSection(listing);

            // ---- 运行时度量 ----
            BeginSection(listing, "运行时度量");
            try
            {
                var snap = RuntimeMetrics.GetSnapshot();
                DrawInfoRow(listing, "会话数", snap.TotalSessions.ToString());
                if (snap.Tokens != null)
                {
                    DrawInfoRow(listing, "  输入 Token", snap.Tokens.TotalInput.ToString());
                    DrawInfoRow(listing, "  输出 Token", snap.Tokens.TotalOutput.ToString());
                    DrawInfoRow(listing, "  缓存命中", snap.Tokens.TotalCacheRead.ToString());
                }
                if (snap.Tools != null)
                    DrawInfoRow(listing, "工具调用类型", snap.Tools.Count.ToString());
                if (snap.Knowledge != null)
                    DrawInfoRow(listing, "知识库查询批次", snap.Knowledge.TotalBatches.ToString());
            }
            catch
            {
                Widgets.Label(listing.GetRect(22f),
                    "<color=#888888><size=12>度量系统未初始化</size></color>");
            }
            EndSection(listing);

            // ---- 危险操作 ----
            BeginSection(listing, "配置管理");

            var dangerResults = DrawButtonRow(listing,
                new[] { "导出配置", "导入配置" },
                new[] { BtnWidthMedium, BtnWidthMedium });

            if (dangerResults[0])
            {
                var json = RimLifeCore.Config.ToJson();
                GUIUtility.systemCopyBuffer = json;
                _statusMessage = "配置已复制到剪贴板";
                _statusMessageTime = Time.time;
                Log.Message($"[RimLife.UI] Config exported: {json.Substring(0, Mathf.Min(json.Length, 100))}...");
            }

            if (dangerResults[1])
            {
                try
                {
                    var json = GUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(json) && json.TrimStart().StartsWith("{"))
                    {
                        var imported = FrameworkConfig.FromJson(json);
                        RimLifeCore.Configure(imported);
                        InitializeFromConfig();
                        _statusMessage = "已从剪贴板导入配置";
                        _statusMessageTime = Time.time;
                    }
                    else
                    {
                        _statusMessage = "剪贴板中没有有效的 JSON 配置";
                        _statusMessageTime = Time.time;
                    }
                }
                catch (System.Exception e)
                {
                    _statusMessage = $"导入失败: {e.Message}";
                    _statusMessageTime = Time.time;
                }
            }

            EndSection(listing);

            // 状态消息（统一淡出效果）
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
        }

        private void InitializeIfNeeded()
        {
            if (_initialized) return;
            _initialized = true;
            InitializeFromConfig();
        }

        private void InitializeFromConfig()
        {
            var f = RimLifeCore.Config.Features;
            var d = RimLifeCore.Config.Diagnostics;
            _enableMetrics = f?.EnableRuntimeMetrics ?? true;
            _enableDirectorAgent = f?.EnableDirectorAgent ?? true;
            _enableMemoryConsolidation = f?.EnableMemoryConsolidation ?? true;
            _enableKnowledgeBase = f?.EnableKnowledgeBase ?? true;
            _enableImproviserAgent = f?.EnableImproviserAgent ?? true;
            _enableVerboseLogging = d?.EnableVerboseLogging ?? false;
            _enableToolCallTracing = d?.EnableToolCallTracing ?? false;
            _enableEventTracing = d?.EnableEventTracing ?? false;
        }

        private void InitializeFromDefaults()
        {
            var def = FrameworkConfig.CreateDefault();
            _enableMetrics = def.Features.EnableRuntimeMetrics;
            _enableDirectorAgent = def.Features.EnableDirectorAgent;
            _enableMemoryConsolidation = def.Features.EnableMemoryConsolidation;
            _enableKnowledgeBase = def.Features.EnableKnowledgeBase;
            _enableImproviserAgent = def.Features.EnableImproviserAgent;
            _enableVerboseLogging = def.Diagnostics.EnableVerboseLogging;
            _enableToolCallTracing = def.Diagnostics.EnableToolCallTracing;
            _enableEventTracing = def.Diagnostics.EnableEventTracing;
        }

        private static FrameworkConfig CloneCurrentConfig()
        {
            try
            {
                var json = RimLifeCore.Config.ToJson();
                return FrameworkConfig.FromJson(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GetAgentStatus(string role)
        {
            switch (role)
            {
                case "director":
                    return RimLifeCore.GetDirectorAgent() != null ? "active" : "idle";
                case "improviser":
                    return RimLifeCore.GetImproviserAgent() != null ? "active" : "idle";
                default:
                    return "idle";
            }
        }
    }
}
