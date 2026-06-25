using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using RimLife.Infrastructure;
using NPCLife.Infrastructure.Llm;
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

        // 用户手动输入的模型名称缓存（编辑器内临时输入，尚未持久化到凭证）。
        private Dictionary<string, string> _manualModelInputs = new Dictionary<string, string>();

        private CancellationTokenSource _discoveryCts;

        // 凭证测试状态 (alias -> "untested"|"testing"|"success"|"failed")
        private Dictionary<string, string> _credentialTestStatus = new Dictionary<string, string>();
        private Dictionary<string, string> _credentialTestError = new Dictionary<string, string>();
        private CancellationTokenSource _testCts;

        // API Key 脱敏显示缓冲区（隔离 TextField 返回值，防止覆盖真实密钥）
        private string _maskedKeyBuffer = "";

        // 凭证注册表快捷访问器
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

            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);

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
        // 状态消息（委托 UIHelper 统一绘制）
        // ================================================================

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

            // 统计未测试凭证数
            int untestedCount = 0;
            foreach (var alias in aliases)
            {
                var status = GetCredentialTestStatus(alias);
                if (status == "untested") untestedCount++;
            }

            // 状态栏背景（扩高到 64px）
            var summaryRect = listing.GetRect(64f);
            var bgColor = isReady
                ? new Color(0.12f, 0.20f, 0.12f, 1f)
                : new Color(0.20f, 0.16f, 0.12f, 1f);
            Widgets.DrawBoxSolid(summaryRect, bgColor);
            Widgets.DrawBox(summaryRect, 1);

            var pad = GapMedium;

            // 第一行：大号状态图标 + 状态文本
            var row1Rect = new Rect(summaryRect.x + pad, summaryRect.y + 8f, summaryRect.width - pad * 2, 22f);
            var statusIcon = isReady ? "<color=#88FF88><size=14>●</size></color>" : "<color=#FF8844><size=14>○</size></color>";
            var statusText = isReady ? "<size=14><b>LLM 已就绪</b></size>" : "<size=14>LLM 未就绪</size>";
            string currentAliasInfo = isReady ? $"  —  当前: <b>{activeAliases[0]}</b>" : "";
            Widgets.Label(row1Rect, $"{statusIcon} {statusText}{currentAliasInfo}");

            // 第二行：统计信息
            var row2Rect = new Rect(summaryRect.x + pad, summaryRect.y + 34f, summaryRect.width - pad * 2, 18f);
            var fallbackChain = activeAliases.Count > 0 ? $"  |  链路: {string.Join(" → ", activeAliases)}" : "";
            Widgets.Label(row2Rect, $"<color=#AAAAAA><size=11>已配置 {totalAliases} 个代号  |  激活 {activeAliases.Count} 个{fallbackChain}</size></color>");

            // 第三行：未测试提醒
            if (untestedCount > 0 && isReady)
            {
                var row3Rect = new Rect(summaryRect.x + pad, summaryRect.y + 50f, summaryRect.width - pad * 2, 12f);
                Widgets.Label(row3Rect, $"<color=#FFCC66><size=10>⚠ {untestedCount} 个凭证尚未测试连接</size></color>");
            }

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
                        DrawAliasCard(listing, alias, cred);
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

        private void DrawAliasCard(Listing_Standard listing, string alias, LlmCredential cred)
        {
            var cardHeight = 52f;
            var cardRect = listing.GetRect(cardHeight);

            // 卡片背景
            var activeAliases = Registry.GetActiveAliases();
            var isActive = activeAliases.Contains(alias);
            var bgColor = isActive
                ? new Color(0.16f, 0.20f, 0.16f, 1f)
                : ColorCardBg;
            Widgets.DrawBoxSolid(cardRect, bgColor);
            Widgets.DrawBox(cardRect, 1);

            // 激活态左侧蓝色竖条
            if (isActive)
            {
                var barRect = new Rect(cardRect.x + 1f, cardRect.y + 4f, 3f, cardRect.height - 8f);
                Widgets.DrawBoxSolid(barRect, ColorHighlight);
            }

            var pad = GapSmall;
            var leftX = cardRect.x + pad + (isActive ? 4f : 0f);
            var usableWidth = cardRect.width - pad * 2 - (isActive ? 4f : 0f);

            // ---- 第一行：状态指示 + 代号名 + 操作按钮 ----
            var row1Y = cardRect.y + 4f;
            var row1H = 22f;

            // 测试状态指示器
            var testStatus = GetCredentialTestStatus(alias);
            var indicatorColor = testStatus switch
            {
                "success" => ColorTestSuccess,
                "failed" => ColorTestFailed,
                "testing" => ColorTestRunning,
                _ => ColorTestUntested
            };
            var indicatorRect = new Rect(leftX, row1Y + 5f, 8f, 8f);
            Widgets.DrawBoxSolid(indicatorRect, indicatorColor);

            // 代号名
            var activeMark = isActive ? "<color=#88FF88>●</color> " : "";
            var labelRect = new Rect(leftX + 14f, row1Y, 110f, row1H);
            Widgets.Label(labelRect, $"<size=13>{activeMark}<b>{alias}</b></size>");

            // 右侧操作按钮
            var btnY = row1Y;
            var btnH = 22f;
            var btnRight = cardRect.x + cardRect.width - pad;

            // 测试按钮
            var testW = 56f;
            var testRect = new Rect(btnRight - testW, btnY, testW, btnH);
            var testLabel = testStatus == "testing" ? "测试中…" : "测试";
            var origColor = GUI.color;
            if (testStatus == "testing")
                GUI.color = ColorTestRunning;
            if (Widgets.ButtonText(testRect, testLabel) && testStatus != "testing")
            {
                StartCredentialTest(alias);
            }
            GUI.color = origColor;

            // 编辑按钮
            var editRect = new Rect(testRect.x - BtnWidthSmall - BtnGap, btnY, BtnWidthSmall, btnH);
            if (Widgets.ButtonText(editRect, "编辑"))
            {
                _pendingDeleteAlias = null;
                StartEditAlias(alias, cred);
            }

            // 删除按钮（两步确认）
            var delW = 60f;
            var delRect = new Rect(editRect.x - delW - BtnGap, btnY, delW, btnH);
            var isPendingDelete = _pendingDeleteAlias == alias;
            if (isPendingDelete)
            {
                Widgets.DrawBoxSolid(delRect, ColorDangerBg);
                if (Widgets.ButtonText(delRect, "确认删除"))
                {
                    Registry.RemoveAlias(alias);
                    _credentialTestStatus.Remove(alias);
                    _credentialTestError.Remove(alias);
                    SetStatus($"已删除: {alias}");
                    _pendingDeleteAlias = null;
                }
            }
            else
            {
                if (Widgets.ButtonText(delRect, "删除"))
                {
                    _pendingDeleteAlias = alias;
                }
            }

            // ---- 第二行：详细信息 ----
            var row2Y = cardRect.y + 28f;
            var row2H = 16f;
            var providerLabel = cred.ProviderType == LlmProviderType.Anthropic ? "Anthropic" : "OpenAI 兼容";
            var displayUrl = cred.BaseUrl?.Length > 35 ? cred.BaseUrl.Substring(0, 32) + "..." : (cred.BaseUrl ?? "");
            var modelDisplay = string.IsNullOrEmpty(cred.ModelName) ? "(未设置模型)" : cred.ModelName;

            var infoRect = new Rect(leftX + 14f, row2Y, usableWidth - 14f, row2H);
            Widgets.Label(infoRect, $"<color=#999999><size=11>{providerLabel}  |  {displayUrl}  |  {modelDisplay}</size></color>");

            // 测试失败时显示错误提示
            if (testStatus == "failed" && _credentialTestError.TryGetValue(alias, out var errMsg))
            {
                var errRect = new Rect(leftX + 14f, row2Y + 14f, usableWidth - 14f, 14f);
                Widgets.Label(errRect, $"<color=#FF6666><size=10>✗ {errMsg}</size></color>");
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
                // 隐藏态：使用独立缓冲区避免 TextField 覆盖真实密钥
                if (string.IsNullOrEmpty(_maskedKeyBuffer))
                    _maskedKeyBuffer = MaskApiKey(_editApiKey);
                _maskedKeyBuffer = Widgets.TextField(keyFieldRect, _maskedKeyBuffer);
            }

            if (Widgets.ButtonText(toggleRect, _showApiKey ? "隐藏" : "显示"))
            {
                _showApiKey = !_showApiKey;
                if (_showApiKey)
                    _maskedKeyBuffer = ""; // 切换到显示态时清空掩码缓冲
            }
            listing.Gap(GapTiny);

            // 模型名称（可选，可从下方"模型发现"获取列表后点击设置）
            Widgets.Label(listing.GetRect(20f), "<size=12>模型名称 <color=#888888>(可选)</color></size>");
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
            if (string.IsNullOrWhiteSpace(_editApiKey))
            {
                SetStatus("[错误] API 密钥不能为空");
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
            var activeList = activeAliases.ToList();
            var inactiveList = allAliases.Where(a => !activeList.Contains(a)).ToList();

            BeginSection(listing, "激活顺序（Fallback 链路）");

            if (allAliases.Count == 0)
            {
                Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>请先添加凭证代号。</size></color>");
                EndSection(listing);
                return;
            }

            if (activeList.Count == 0)
            {
                Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>未激活任何代号。运行时按此列表顺序尝试，失败自动切换下一个。</size></color>");
            }
            else
            {
                // 激活列表：每个条目含上下移动按钮 + 移除按钮
                for (int i = 0; i < activeList.Count; i++)
                {
                    var alias = activeList[i];
                    var rowRect = listing.GetRect(24f);
                    var rowW = rowRect.width;
                    var btnSize = 22f;
                    var btnGap = 2f;

                    // 序号
                    var idxRect = new Rect(rowRect.x, rowRect.y + 2f, 20f, 20f);
                    Widgets.Label(idxRect, $"<color=#888888><size=11>{i + 1}.</size></color>");

                    // 名称
                    var nameRect = new Rect(rowRect.x + 22f, rowRect.y + 2f, 120f, 20f);
                    Widgets.Label(nameRect, $"<size=12><b>{alias}</b></size>");

                    // 移除按钮（右侧）
                    var removeRect = new Rect(rowRect.x + rowW - BtnWidthSmall, rowRect.y, BtnWidthSmall, btnSize);
                    if (Widgets.ButtonText(removeRect, "移除"))
                    {
                        activeList.RemoveAt(i);
                        Registry.SetActiveAliases(activeList);
                        SetStatus($"{alias} 已从激活列表移除");
                        break; // 列表已变更，退出循环
                    }

                    // 下移按钮
                    var downRect = new Rect(removeRect.x - btnSize - btnGap, rowRect.y, btnSize, btnSize);
                    if (i < activeList.Count - 1)
                    {
                        if (Widgets.ButtonText(downRect, "↓"))
                        {
                            var tmp = activeList[i];
                            activeList[i] = activeList[i + 1];
                            activeList[i + 1] = tmp;
                            Registry.SetActiveAliases(activeList);
                            SetStatus($"{alias} 下移一位");
                            break;
                        }
                    }

                    // 上移按钮
                    var upRect = new Rect(downRect.x - btnSize - btnGap, rowRect.y, btnSize, btnSize);
                    if (i > 0)
                    {
                        if (Widgets.ButtonText(upRect, "↑"))
                        {
                            var tmp = activeList[i];
                            activeList[i] = activeList[i - 1];
                            activeList[i - 1] = tmp;
                            Registry.SetActiveAliases(activeList);
                            SetStatus($"{alias} 上移一位");
                            break;
                        }
                    }
                }
            }

            // 可添加的未激活代号
            if (inactiveList.Count > 0)
            {
                listing.Gap(GapTiny);
                Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>点击添加至激活列表：</size></color>");
                foreach (var alias in inactiveList)
                {
                    var addRect = listing.GetRect(22f);
                    var btnW = 100f;
                    var labelW = addRect.width - btnW - GapSmall;
                    Widgets.Label(new Rect(addRect.x, addRect.y, labelW, addRect.height),
                        $"<color=#AAAAAA>{alias}</color>");
                    if (Widgets.ButtonText(new Rect(addRect.x + labelW + GapSmall, addRect.y, btnW, 20f), "+ 加入激活"))
                    {
                        var newActive = Registry.GetActiveAliases().ToList();
                        newActive.Add(alias);
                        Registry.SetActiveAliases(newActive);
                        SetStatus($"{alias} 已加入激活列表");
                    }
                }
            }

            // 当前链路顺序只读展示
            if (activeAliases.Count > 0)
            {
                var infoRect = listing.GetRect(20f);
                Widgets.Label(infoRect,
                    $"<color=#888888><size=11>当前 Fallback 链路:  [{string.Join(" → ", activeAliases)}]</size></color>");
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
            _discoveryCts.CancelAfter(TimeSpan.FromSeconds(30)); // 兜底超时保护

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

        // ================================================================
        // 凭证测试
        // ================================================================

        private string GetCredentialTestStatus(string alias)
        {
            if (_credentialTestStatus.TryGetValue(alias, out var status))
                return status;
            return "untested";
        }

        private async void StartCredentialTest(string alias)
        {
            if (!Registry.TryGetCredential(alias, out var cred)) return;

            _credentialTestStatus[alias] = "testing";
            _credentialTestError.Remove(alias);
            _testCts?.Cancel();
            _testCts = new CancellationTokenSource();
            var ct = _testCts.Token;

            try
            {
                // 使用模型发现端点做轻量连通性测试，5 秒超时
                var timeoutTask = Task.Delay(5000, ct);
                var llmService = RimLifeCore.LlmAccessor as ILlmService;

                if (llmService == null)
                {
                    _credentialTestStatus[alias] = "failed";
                    _credentialTestError[alias] = "LLM 服务未初始化";
                    return;
                }

                var discoveryTask = llmService.ListModelsAsync(cred, ct);
                var completedTask = await Task.WhenAny(discoveryTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _credentialTestStatus[alias] = "failed";
                    _credentialTestError[alias] = "连接超时 (5s)";
                }
                else
                {
                    await discoveryTask; // 可能抛异常
                    _credentialTestStatus[alias] = "success";
                    _credentialTestError.Remove(alias);
                    SetStatus($"{alias}: 连接测试通过");
                }
            }
            catch (OperationCanceledException)
            {
                // 用户取消了（例如切换到另一个凭证的测试）
                if (_credentialTestStatus.TryGetValue(alias, out var s) && s == "testing")
                    _credentialTestStatus[alias] = "untested";
            }
            catch (Exception ex)
            {
                _credentialTestStatus[alias] = "failed";
                _credentialTestError[alias] = ex.Message;
            }
            finally
            {
                _testCts?.Dispose();
                _testCts = null;
            }
        }
    }
}

