using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimLife.Core;
using RimLife.Framework.Llm;
using RimLife.Infrastructure;
using RimLife.Infrastructure.Llm;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// LLM 连接配置页面。
    /// 信息架构：状态摘要 → 代号管理 → 模型发现。
    /// 基于 ICredentialRegistry，管理"模型代号 → 凭证三元组"映射。
    /// </summary>
    public class ConnectionPage : IConfigPage
    {
        public string Id => "connection";
        public string Label => "连接";
        public string Group => "核心";
        public int Order => 0;

        // ================================================================
        // UI 状态
        // ================================================================

        private enum PageMode { ViewAliases, EditAlias, AddAlias }
        private PageMode _mode = PageMode.ViewAliases;

        // 编辑/新增表单字段
        private string _editAliasName = "";
        private string _editBaseUrl = "";
        private string _editApiKey = "";
        private string _editModelName = "";
        private LlmProviderType _editProviderType = LlmProviderType.OpenAI;

        // 模型发现状态
        private bool _isDiscovering;
        private string _discoveryStatus = "";
        private string _discoveryError;
        private Dictionary<string, string[]> _discoveredModels;

        // 操作反馈
        private string _statusMessage;
        private float _statusMessageTime;

        // 删除确认（两步确认）
        private string _pendingDeleteAlias;

        // API Key 显示切换
        private bool _showApiKey;

        // 手动添加模型输入
        private Dictionary<string, string> _manualModelInputs = new Dictionary<string, string>();

        private CancellationTokenSource _discoveryCts;

        // 缓存
        private ICredentialRegistry Registry => RimLifeCore.CredentialRegistry;

        // ================================================================
        // Draw
        // ================================================================

        public void Draw(Rect rect, Listing_Standard listing)
        {
            if (Registry == null)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#FF6666><size=12>凭证注册表未初始化</size></color>");
                return;
            }

            DrawStatusMessage(listing);

            switch (_mode)
            {
                case PageMode.ViewAliases:
                    DrawStatusSummary(listing);
                    DrawAliasSection(listing);
                    DrawActiveOrderSection(listing);
                    DrawModelSection(listing);
                    break;
                case PageMode.EditAlias:
                    DrawAliasEditForm(listing, isNew: false);
                    break;
                case PageMode.AddAlias:
                    DrawAliasEditForm(listing, isNew: true);
                    break;
            }
        }

        // ================================================================
        // 状态消息
        // ================================================================

        private void DrawStatusMessage(Listing_Standard listing)
        {
            if (!string.IsNullOrEmpty(_statusMessage) && Time.time - _statusMessageTime < 5f)
            {
                var elapsed = Time.time - _statusMessageTime;
                var baseColor = _statusMessage.StartsWith("[错误]") ? "#FF6666" : "#88FF88";

                string colorTag;
                if (elapsed > 4f)
                {
                    var alpha = Mathf.Clamp01(5f - elapsed);
                    var alphaHex = ((int)(alpha * 255)).ToString("X2");
                    colorTag = $"<color={baseColor}{alphaHex}>";
                }
                else
                {
                    colorTag = $"<color={baseColor}>";
                }

                Widgets.Label(listing.GetRect(22f), $"{colorTag}<size=12>{_statusMessage}</size></color>");
                listing.Gap(GapSmall);
            }
        }

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusMessageTime = Time.time;
        }

        // ================================================================
        // 连接状态摘要（顶部）
        // ================================================================

        private void DrawStatusSummary(Listing_Standard listing)
        {
            var aliases = Registry.GetAllAliases();
            var activeAliases = Registry.GetActiveAliases();
            var isReady = activeAliases.Count > 0;
            var totalAliases = aliases.Count;

            // 状态栏背景
            var summaryRect = listing.GetRect(52f);
            var bgColor = isReady
                ? new Color(0.15f, 0.22f, 0.15f, 1f)
                : new Color(0.22f, 0.18f, 0.15f, 1f);
            Widgets.DrawBoxSolid(summaryRect, bgColor);
            Widgets.DrawBox(summaryRect, 1);

            // 第一行：状态指示 + 当前代号
            var row1Rect = new Rect(summaryRect.x + GapMedium, summaryRect.y + 6f, summaryRect.width - GapMedium * 2, 20f);
            var statusIcon = isReady ? "<color=#88FF88>●</color>" : "<color=#FF8844>○</color>";
            var statusText = isReady ? "已就绪" : "未就绪";
            string currentAliasInfo = isReady ? $"  当前: <b>{activeAliases[0]}</b>" : "";
            Widgets.Label(row1Rect, $"<size=13>{statusIcon} {statusText}{currentAliasInfo}</size>");

            // 第二行：统计信息
            var row2Rect = new Rect(summaryRect.x + GapMedium, summaryRect.y + 28f, summaryRect.width - GapMedium * 2, 18f);
            Widgets.Label(row2Rect, $"<color=#AAAAAA><size=11>代号总数: {totalAliases}  |  激活: {activeAliases.Count} 个  |  顺序: [{string.Join(", ", activeAliases)}]</size></color>");

            listing.Gap(GapMedium);
        }

        // ================================================================
        // 代号管理区
        // ================================================================

        private void DrawAliasSection(Listing_Standard listing)
        {
            BeginSection(listing, "凭证代号");

            var aliases = Registry.GetAllAliases();
            if (aliases.Count == 0)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#888888><size=12>尚无凭证代号，点击下方按钮添加。</size></color>");
                listing.Gap(GapSmall);
            }
            else
            {
                foreach (var alias in aliases.ToList())
                {
                    if (Registry.TryGetCredential(alias, out var cred))
                    {
                        DrawAliasRow(listing, alias, cred);
                    }
                }
                listing.Gap(GapSmall);
            }

            // 添加按钮
            var addResults = DrawButtonRow(listing,
                new[] { "+ 添加代号" },
                new[] { BtnWidthMedium });
            if (addResults[0])
            {
                _pendingDeleteAlias = null;
                StartAddAlias();
            }

            EndSection(listing);
        }

        private void DrawAliasRow(Listing_Standard listing, string alias, LlmCredential cred)
        {
            var rowHeight = 32f;
            var rowRect = listing.GetRect(rowHeight);

            // 背景
            var activeAliases = Registry.GetActiveAliases();
            var isActive = activeAliases.Contains(alias);
            if (isActive)
            {
                Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.22f, 0.18f, 1f));
            }

            DrawHoverBackground(rowRect, new Color(0.25f, 0.25f, 0.25f, 0.5f));

            var pad = GapSmall;
            var leftX = rowRect.x + pad;
            var contentWidth = rowRect.width - pad * 2;

            // 代号名（左侧）
            var labelWidth = Mathf.Min(120f, contentWidth * 0.22f);
            var labelRect = new Rect(leftX, rowRect.y + 4f, labelWidth, 24f);
            var activeMark = isActive ? "● " : "○ ";
            Widgets.Label(labelRect, $"<size=12>{activeMark}<b>{alias}</b></size>");

            // 凭证摘要（中间）
            var infoX = leftX + labelWidth + GapSmall;
            var infoWidth = contentWidth - labelWidth - BtnWidthSmall * 2 - BtnGap * 2 - GapSmall;
            var infoRect = new Rect(infoX, rowRect.y + 7f, infoWidth, 18f);

            var displayUrl = cred.BaseUrl?.Length > 25 ? cred.BaseUrl.Substring(0, 22) + "..." : (cred.BaseUrl ?? "");
            var maskedKey = MaskApiKey(cred.ApiKey);
            Widgets.Label(infoRect, $"<color=#AAAAAA><size=11>{cred.ModelName}  {displayUrl}  {maskedKey}  {cred.ProviderType}</size></color>");

            // 操作按钮（右侧）
            var btnY = rowRect.y + 3f;
            var btnRight = rowRect.x + rowRect.width - pad;

            // 删除按钮（两步确认）
            var delWidth = BtnWidthSmall;
            var delRect = new Rect(btnRight - delWidth, btnY, delWidth, 26f);
            var isPendingDelete = _pendingDeleteAlias == alias;
            if (isPendingDelete)
            {
                Widgets.DrawBoxSolid(delRect, ColorDangerBg);
                if (Widgets.ButtonText(delRect, "确认删除"))
                {
                    Registry.RemoveAlias(alias);
                    SetStatus($"已删除: {alias}");
                    _pendingDeleteAlias = null;
                }
            }
            else
            {
                if (Widgets.ButtonText(delRect, "×"))
                {
                    _pendingDeleteAlias = alias;
                }
            }

            // 编辑按钮
            var editRect = new Rect(delRect.x - BtnWidthSmall - BtnGap, btnY, BtnWidthSmall, 26f);
            if (Widgets.ButtonText(editRect, "编辑"))
            {
                _pendingDeleteAlias = null;
                StartEditAlias(alias, cred);
            }

            listing.Gap(GapTiny);
        }

        // ================================================================
        // 代号编辑/新增表单
        // ================================================================

        private void StartEditAlias(string alias, LlmCredential cred)
        {
            _editAliasName = alias;
            _editBaseUrl = cred.BaseUrl ?? "";
            _editApiKey = cred.ApiKey ?? "";
            _editModelName = cred.ModelName ?? "";
            _editProviderType = cred.ProviderType;
            _mode = PageMode.EditAlias;
        }

        private void StartAddAlias()
        {
            _editAliasName = "";
            _editBaseUrl = "";
            _editApiKey = "";
            _editModelName = "";
            _editProviderType = LlmProviderType.OpenAI;
            _mode = PageMode.AddAlias;
        }

        private void DrawAliasEditForm(Listing_Standard listing, bool isNew)
        {
            var title = isNew ? "新增凭证代号" : "编辑凭证代号";
            BeginSection(listing, title);

            // 代号
            Widgets.Label(listing.GetRect(20f), "<size=12>模型代号</size>");
            var aliasRect = listing.GetRect(28f);
            if (isNew)
            {
                _editAliasName = Widgets.TextField(aliasRect, _editAliasName);
            }
            else
            {
                Widgets.Label(aliasRect, $"<size=12><b>{_editAliasName}</b></size>");
            }
            listing.Gap(GapTiny);

            // Base URL
            Widgets.Label(listing.GetRect(20f), "<size=12>API 基础 URL</size>");
            var urlRect = listing.GetRect(28f);
            _editBaseUrl = Widgets.TextField(urlRect, _editBaseUrl);
            listing.Gap(GapTiny);

            // API Key
            Widgets.Label(listing.GetRect(20f), "<size=12>API 密钥</size>");
            var keyRowRect = listing.GetRect(28f);
            var keyFieldWidth = keyRowRect.width - BtnWidthSmall - BtnGap;
            var keyFieldRect = new Rect(keyRowRect.x, keyRowRect.y, keyFieldWidth, keyRowRect.height);
            var toggleRect = new Rect(keyRowRect.x + keyFieldWidth + BtnGap, keyRowRect.y, BtnWidthSmall, keyRowRect.height);

            if (_showApiKey)
            {
                _editApiKey = Widgets.TextField(keyFieldRect, _editApiKey);
            }
            else
            {
                var maskedDisplay = MaskApiKey(_editApiKey);
                Widgets.TextField(keyFieldRect, maskedDisplay);
            }

            if (Widgets.ButtonText(toggleRect, _showApiKey ? "隐藏" : "显示"))
            {
                _showApiKey = !_showApiKey;
            }
            listing.Gap(GapTiny);

            // 模型名称
            Widgets.Label(listing.GetRect(20f), "<size=12>模型名称</size>");
            var modelRect = listing.GetRect(28f);
            _editModelName = Widgets.TextField(modelRect, _editModelName);
            listing.Gap(GapTiny);

            // Provider Type
            Widgets.Label(listing.GetRect(20f), "<size=12>提供商类型</size>");
            var providerIndex = _editProviderType == LlmProviderType.Anthropic ? 1 : 0;
            var newProviderIndex = DrawSegmentedSelector(listing,
                new[] { "OpenAI 兼容", "Anthropic" }, providerIndex);
            if (newProviderIndex != providerIndex)
            {
                _editProviderType = newProviderIndex == 1
                    ? LlmProviderType.Anthropic
                    : LlmProviderType.OpenAI;
            }

            listing.Gap(GapMedium);

            // 按钮行
            var saveLabel = isNew ? "添加" : "保存";
            var btnResults = DrawButtonRow(listing,
                new[] { saveLabel, "取消" },
                new[] { BtnWidthMedium, BtnWidthSmall });
            if (btnResults[0])
            {
                SaveAliasEdit(isNew);
            }
            if (btnResults[1])
            {
                _showApiKey = false;
                _mode = PageMode.ViewAliases;
            }

            EndSection(listing);
        }

        private void SaveAliasEdit(bool isNew)
        {
            if (string.IsNullOrWhiteSpace(_editAliasName))
            {
                SetStatus("[错误] 代号不能为空");
                return;
            }
            if (string.IsNullOrWhiteSpace(_editBaseUrl))
            {
                SetStatus("[错误] Base URL 不能为空");
                return;
            }

            var cred = new LlmCredential
            {
                BaseUrl = _editBaseUrl,
                ApiKey = _editApiKey,
                ModelName = _editModelName,
                ProviderType = _editProviderType
            };

            Registry.SetAlias(_editAliasName, cred);

            // 如果是新增，自动加入激活列表
            if (isNew)
            {
                var currentActive = Registry.GetActiveAliases();
                if (!currentActive.Contains(_editAliasName))
                {
                    var newActive = currentActive.ToList();
                    newActive.Add(_editAliasName);
                    Registry.SetActiveAliases(newActive);
                }
            }

            SetStatus(isNew ? $"已添加: {_editAliasName}" : $"已更新: {_editAliasName}");
            _mode = PageMode.ViewAliases;
        }

        // ================================================================
        // 激活顺序管理
        // ================================================================

        private void DrawActiveOrderSection(Listing_Standard listing)
        {
            var activeAliases = Registry.GetActiveAliases();
            var allAliases = Registry.GetAllAliases();

            BeginSection(listing, "激活顺序（Fallback 链路）");

            if (allAliases.Count == 0)
            {
                Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>请先添加凭证代号。</size></color>");
                EndSection(listing);
                return;
            }

            // 每个代号显示复选框
            var newActive = activeAliases.ToList();
            foreach (var alias in allAliases)
            {
                var rowRect = listing.GetRect(22f);
                bool isActive = newActive.Contains(alias);
                Widgets.CheckboxLabeled(rowRect, $" {alias}", ref isActive);

                if (isActive && !newActive.Contains(alias))
                {
                    newActive.Add(alias);
                }
                else if (!isActive && newActive.Contains(alias))
                {
                    newActive.Remove(alias);
                }
            }

            listing.Gap(GapSmall);

            // 应用按钮
            var applyResults = DrawButtonRow(listing,
                new[] { $"应用激活 ({newActive.Count})" },
                new[] { BtnWidthMedium });
            if (applyResults[0])
            {
                Registry.SetActiveAliases(newActive);
                SetStatus($"已应用: {newActive.Count} 个代号激活");
            }

            // 说明
            if (activeAliases.Count > 0)
            {
                var infoRect = listing.GetRect(22f);
                Widgets.Label(infoRect,
                    $"<color=#888888><size=11>运行时按顺序尝试，失败自动切换下一个 | 顺序: [{string.Join(" → ", activeAliases)}]</size></color>");
            }

            EndSection(listing);
        }

        // ================================================================
        // 模型发现区
        // ================================================================

        private void DrawModelSection(Listing_Standard listing)
        {
            var aliases = Registry.GetAllAliases();
            if (aliases.Count == 0) return;

            BeginSection(listing, "模型发现");

            // 获取模型列表按钮
            var btnLabel = _isDiscovering ? "正在获取..." : "获取所有模型列表";
            var btnResults = DrawButtonRow(listing,
                new[] { btnLabel },
                new[] { BtnWidthLarge });

            if (btnResults[0] && !_isDiscovering)
            {
                StartModelDiscovery();
            }

            // 进度/结果
            if (_isDiscovering && !string.IsNullOrEmpty(_discoveryStatus))
            {
                Widgets.Label(listing.GetRect(22f), $"<color=#88AAFF><size=12>{_discoveryStatus}</size></color>");
            }

            if (!string.IsNullOrEmpty(_discoveryError))
            {
                Widgets.Label(listing.GetRect(22f), $"<color=#FF6666><size=12>{_discoveryError}</size></color>");
            }

            // 发现结果列表
            if (_discoveredModels != null && _discoveredModels.Count > 0)
            {
                listing.Gap(GapSmall);
                foreach (var kv in _discoveredModels)
                {
                    var alias = kv.Key;
                    var models = kv.Value;

                    var headerRect = listing.GetRect(22f);
                    Widgets.Label(headerRect, $"<size=12><b>{alias}</b></size>");

                    if (models.Length == 0)
                    {
                        var emptyRect = listing.GetRect(20f);
                        Widgets.Label(emptyRect, "<color=#888888><size=11>未发现模型（或不支持列表查询）</size></color>");
                    }
                    else
                    {
                        // 紧凑排列，每行多个
                        var modelsPerRow = 2;
                        var modelWidth = (listing.ColumnWidth - GapSmall) / modelsPerRow;

                        for (int i = 0; i < models.Length; i += modelsPerRow)
                        {
                            var rowRect = listing.GetRect(26f);
                            for (int j = 0; j < modelsPerRow && i + j < models.Length; j++)
                            {
                                var modelName = models[i + j];
                                var modelRect = new Rect(rowRect.x + j * (modelWidth + GapSmall), rowRect.y, modelWidth, 26f);

                                if (Widgets.ButtonText(modelRect, modelName))
                                {
                                    // 点击模型名：设置为该代号的模型
                                    if (Registry.TryGetCredential(alias, out var cred))
                                    {
                                        cred.ModelName = modelName;
                                        Registry.SetAlias(alias, cred);
                                        SetStatus($"{alias} → {modelName}");
                                    }
                                }
                            }
                        }
                    }

                    listing.Gap(GapTiny);
                }
            }

            // 手动添加模型
            DrawManualModelEntry(listing);

            EndSection(listing);
        }

        private void DrawManualModelEntry(Listing_Standard listing)
        {
            var aliases = Registry.GetAllAliases();
            if (aliases.Count == 0) return;

            listing.Gap(GapSmall);
            Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>手动设置模型（用于不支持列表查询的 API）:</size></color>");
            listing.Gap(GapTiny);

            foreach (var alias in aliases)
            {
                var rowRect = listing.GetRect(28f);
                var inputWidth = rowRect.width - BtnWidthMedium - BtnGap;
                var inputRect = new Rect(rowRect.x, rowRect.y, inputWidth, 28f);
                var btnRect = new Rect(rowRect.x + inputWidth + BtnGap, rowRect.y, BtnWidthMedium, 28f);

                if (!_manualModelInputs.ContainsKey(alias))
                    _manualModelInputs[alias] = "";

                _manualModelInputs[alias] = Widgets.TextField(inputRect, _manualModelInputs[alias]);

                if (Widgets.ButtonText(btnRect, $"设置 {alias}"))
                {
                    var modelName = _manualModelInputs[alias]?.Trim();
                    if (!string.IsNullOrEmpty(modelName))
                    {
                        if (Registry.TryGetCredential(alias, out var cred))
                        {
                            cred.ModelName = modelName;
                            Registry.SetAlias(alias, cred);
                            SetStatus($"{alias} → {modelName}");
                            _manualModelInputs[alias] = "";
                        }
                    }
                    else
                    {
                        SetStatus("[错误] 请输入模型名称");
                    }
                }
            }
        }

        // ================================================================
        // 模型发现
        // ================================================================

        private async void StartModelDiscovery()
        {
            var llmService = RimLifeCore.LlmAccessor as ILlmService;
            if (llmService == null)
            {
                SetStatus("[错误] LLM 服务未初始化");
                return;
            }

            _isDiscovering = true;
            _discoveryError = null;
            _discoveryStatus = "正在查询模型列表...";
            _discoveryCts = new CancellationTokenSource();

            try
            {
                var models = await Registry.DiscoverModelsAsync(
                    llmService,
                    (current, total, alias, modelCount) =>
                    {
                        if (modelCount >= 0)
                            _discoveryStatus = $"[{current}/{total}] {alias}: 发现 {modelCount} 个模型";
                        else
                            _discoveryStatus = $"[{current}/{total}] {alias}: 查询失败";
                    },
                    _discoveryCts.Token);

                _discoveredModels = models.ToDictionary(kv => kv.Key, kv => kv.Value);

                int totalModels = models.Values.Sum(v => v.Length);
                SetStatus($"共发现 {totalModels} 个模型（来自 {models.Count} 个代号）");
            }
            catch (OperationCanceledException)
            {
                _discoveryError = "查询已取消";
            }
            catch (Exception ex)
            {
                _discoveryError = $"查询出错: {ex.Message}";
            }
            finally
            {
                _isDiscovering = false;
                _discoveryCts?.Dispose();
                _discoveryCts = null;
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static string MaskApiKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(未设置)";
            if (key.Length <= 8) return new string('*', key.Length);
            return key.Substring(0, 4) + new string('*', Math.Min(key.Length - 8, 8)) + key.Substring(key.Length - 4);
        }
    }
}

