using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using NPCLife.Infrastructure.Llm;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// LLM 凭证管理页面。
    /// 每个凭证卡片是自包含实体：集成 BaseUrl、ApiKey、ModelName 和提供商类型。
    /// 模型发现内聚到单个卡片内部，不泄露到外部。
    /// </summary>
    public class ConnectionPage : IConfigPage
    {
        public string Id => "connection";
        public string Label => "连接";
        public string Group => "核心";
        public int Order => 0;

        // ================================================================
        // 快捷访问器
        // ================================================================

        private ICredentialManager Manager => RimLifeCore.CredentialManager;
        private ILlmService LlmService => RimLifeCore.LlmAccessor as ILlmService;

        // ================================================================
        // 每卡片 UI 状态
        // ================================================================

        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly HashSet<string> _editing = new HashSet<string>();
        private readonly Dictionary<string, bool> _showApiKey = new Dictionary<string, bool>();
        // 每卡片异步操作状态
        private readonly Dictionary<string, string> _testStatus = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _testError = new Dictionary<string, string>();
        private CancellationTokenSource _testCts;

        // 模型列表管理（每凭证）
        private readonly Dictionary<string, string[]> _availableModels = new Dictionary<string, string[]>();
        private readonly Dictionary<string, HashSet<string>> _selectedModels = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, string> _modelSearchText = new Dictionary<string, string>();
        private readonly Dictionary<string, Vector2> _modelScrollPos = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, bool> _modelsExpanded = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _fetchingModels = new Dictionary<string, bool>();
        private const float ModelListMaxHeight = 200f;

        // 新增凭证表单
        private bool _addingNew;
        private string _newName = "";
        private string _newBaseUrl = "";
        private string _newApiKey = "";
        private string _newModelName = "";
        private LlmProviderType _newProviderType = LlmProviderType.OpenAI;
        private bool _newShowApiKey;

        // 删除确认（一次只能有一个）
        private string _pendingDeleteAlias;

        // 全局操作反馈
        private string _statusMessage;
        private float _statusMessageTime;

        // ================================================================
        // Draw
        // ================================================================

        public void Draw(Rect rect, Listing_Standard listing)
        {
            if (Manager == null)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#FF6666><size=12>凭证管理器未初始化</size></color>");
                return;
            }

            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
            DrawStatusSummary(listing);
            DrawCredentialList(listing);
            DrawActivationOrder(listing);
            DrawAddButton(listing);
        }

        // ================================================================
        // 状态摘要
        // ================================================================

        private void DrawStatusSummary(Listing_Standard listing)
        {
            var aliases = Manager.GetAll().Select(x => x.Name).ToList();
            var activeAliases = Manager.GetActivationOrder();
            var isReady = activeAliases.Count > 0;

            var summaryRect = listing.GetRect(36f);
            var bgColor = isReady
                ? new Color(0.12f, 0.20f, 0.12f, 1f)
                : new Color(0.20f, 0.16f, 0.12f, 1f);
            Widgets.DrawBoxSolid(summaryRect, bgColor);
            Widgets.DrawBox(summaryRect, 1);

            var pad = GapMedium;
            var statusIcon = isReady
                ? "<color=#88FF88><size=14>●</size></color>"
                : "<color=#FF8844><size=14>○</size></color>";
            var statusText = isReady
                ? $"<size=13><b>LLM 已就绪</b></size>  —  当前: <b>{activeAliases[0]}</b>"
                : "<size=13>LLM 未就绪</size>";
            var chain = isReady && activeAliases.Count > 1
                ? $"  链路: {string.Join(" → ", activeAliases)}"
                : "";

            var row1Rect = new Rect(summaryRect.x + pad, summaryRect.y + 8f,
                summaryRect.width - pad * 2, 22f);
            Widgets.Label(row1Rect, $"{statusIcon} {statusText}{chain}");

            listing.Gap(GapMedium);
        }

        // ================================================================
        // 凭证列表
        // ================================================================

        private void DrawCredentialList(Listing_Standard listing)
        {
            BeginSection(listing, "凭证列表");

            var aliases = Manager.GetAll().Select(x => x.Name).ToList();
            if (aliases.Count == 0 && !_addingNew)
            {
                Widgets.Label(listing.GetRect(22f),
                    "<color=#888888><size=12>尚无凭证，点击下方按钮添加。</size></color>");
                listing.Gap(GapSmall);
            }
            else
            {
                // 新增表单（显示在列表顶部）
                if (_addingNew)
                {
                    DrawNewCredentialCard(listing);
                    listing.Gap(GapTiny);
                }

                // 现有凭证卡片
                foreach (var alias in aliases.ToList())
                {
                    var cred = Manager.Get(alias);
                    if (cred != null)
                    {
                        DrawCredentialCard(listing, alias, cred);
                        listing.Gap(GapTiny);
                    }
                }
            }

            EndSection(listing);
        }

        // ================================================================
        // 单个凭证卡片
        // ================================================================

        private void DrawCredentialCard(Listing_Standard listing, string alias, LlmCredential cred)
        {
            var isExpanded = _expanded.Contains(alias);
            var isEditing = _editing.Contains(alias);
            var isActive = Manager.GetActivationOrder().Contains(alias);
            var testSt = GetTestStatus(alias);

            // 计算高度
            float cardHeight;
            if (isEditing)
            {
                cardHeight = CalcEditCardHeight(alias);
            }
            else if (isExpanded)
            {
                cardHeight = CalcExpandedCardHeight(alias, cred, testSt);
            }
            else
            {
                cardHeight = 40f;
            }

            var cardRect = listing.GetRect(cardHeight);

            // 卡片背景
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
            var contentX = cardRect.x + pad + (isActive ? 4f : 0f);
            var contentW = cardRect.width - pad * 2 - (isActive ? 4f : 0f);

            if (isEditing)
            {
                DrawEditCardContent(contentX, cardRect.y, contentW, alias, cred);
            }
            else
            {
                DrawCompactCardContent(contentX, cardRect.y, contentW, alias, cred, testSt);
                if (isExpanded)
                    DrawExpandedCardContent(contentX, cardRect.y, contentW, alias, cred, testSt);
            }
        }

        // ---- 紧凑行 ----

        private void DrawCompactCardContent(
            float x, float y, float w, string alias, LlmCredential cred, string testSt)
        {
            var rowY = y + 6f;
            var rowH = 26f;
            var cursorX = x + 4f;

            // 测试状态指示器
            var indicatorColor = GetTestColor(testSt);
            Widgets.DrawBoxSolid(new Rect(cursorX, rowY + 8f, 8f, 8f), indicatorColor);
            cursorX += 14f;

            // 凭证名
            var nameW = 100f;
            Widgets.Label(new Rect(cursorX, rowY, nameW, rowH),
                $"<size=13><b>{alias}</b></size>");
            cursorX += nameW;

            // 提供商标签
            var providerLabel = cred.ProviderType == LlmProviderType.Anthropic ? "Anthropic" : "OpenAI";
            var providerW = 70f;
            Widgets.Label(new Rect(cursorX, rowY, providerW, rowH),
                $"<color=#888888><size=11>{providerLabel}</size></color>");
            cursorX += providerW;

            // URL 摘要
            var urlDisplay = TruncateUrl(cred.BaseUrl, 30);
            var urlW = w - (cursorX - x) - 180f;
            if (urlW > 40f)
                Widgets.Label(new Rect(cursorX, rowY, urlW, rowH),
                    $"<color=#666666><size=11>{urlDisplay}</size></color>");

            // 右侧按钮（从右到左排列）
            var btnX = x + w;
            const float btnH = 22f;

            // 展开/收起
            var expandLabel = _expanded.Contains(alias) ? "收起" : "展开";
            btnX -= 48f;
            if (Widgets.ButtonText(new Rect(btnX, rowY + 2f, 48f, btnH), expandLabel))
                ToggleExpand(alias);

            // 编辑
            btnX -= BtnWidthSmall + BtnGap;
            if (Widgets.ButtonText(new Rect(btnX, rowY + 2f, BtnWidthSmall, btnH), "编辑"))
                StartEdit(alias, cred);

            // 删除（两步确认）
            btnX -= 56f + BtnGap;
            var delRect = new Rect(btnX, rowY + 2f, 56f, btnH);
            if (_pendingDeleteAlias == alias)
            {
                Widgets.DrawBoxSolid(delRect, ColorDangerBg);
                if (Widgets.ButtonText(delRect, "确认?"))
                {
                    Manager.Delete(alias);
                    CleanupCardState(alias);
                    SetStatus($"已删除: {alias}");
                    _pendingDeleteAlias = null;
                }
            }
            else
            {
                if (Widgets.ButtonText(delRect, "删除"))
                    _pendingDeleteAlias = alias;
            }
        }

        // ---- 展开内容 ----

        private float CalcExpandedCardHeight(string alias, LlmCredential cred, string testSt)
        {
            // 使用 CJK 补偿后的行高，消除 RimWorld 引擎对中文文本高度的低估
            float lineH = 22f * CjkHeightCompensation;
            float keyLineH = 28f * CjkHeightCompensation;

            float h = 36f; // 紧凑行区域
            h += GapSmall;
            h += lineH; // BaseUrl
            h += GapTiny;
            h += keyLineH; // ApiKey 行
            h += GapTiny;
            h += lineH; // 当前模型显示

            // 错误提示
            if (testSt == "failed" && _testError.ContainsKey(alias))
                h += 16f * CjkHeightCompensation;

            h += GapSmall;
            h += 30f; // 操作按钮行

            // 模型列表区（固定高度：搜索框 + 摘要行 + 滚动列表）
            if (_availableModels.ContainsKey(alias))
            {
                h += GapSmall;
                h += 28f; // 搜索框
                h += GapTiny;
                h += 14f; // 摘要统计行（"X/Y 个模型 | 已启用: Z"）
                h += ModelListMaxHeight - 28f - GapTiny - 14f; // 滚动列表区
            }

            return h;
        }

        private void DrawExpandedCardContent(
            float x, float y, float w, string alias, LlmCredential cred, string testSt)
        {
            var cursorY = y + 36f + GapSmall;

            // BaseUrl
            Widgets.Label(new Rect(x + 4f, cursorY, w - 8f, 22f),
                $"<color=#999999><size=11>Base URL:</size></color> {cred.BaseUrl ?? "(未设置)"}");
            cursorY += 22f + GapTiny;

            // ApiKey（带显示/隐藏）
            var keyRowRect = new Rect(x + 4f, cursorY, w - 8f, 28f);
            var showKey = _showApiKey.TryGetValue(alias, out var sk) && sk;
            var keyDisplay = showKey
                ? (cred.ApiKey ?? "(未设置)")
                : MaskApiKey(cred.ApiKey);
            var toggleW = 56f;
            var keyLabelW = keyRowRect.width - toggleW - BtnGap;

            Widgets.Label(new Rect(keyRowRect.x, keyRowRect.y, keyLabelW, keyRowRect.height),
                $"<color=#999999><size=11>API Key:</size></color> {keyDisplay}");
            if (Widgets.ButtonText(
                new Rect(keyRowRect.x + keyLabelW + BtnGap, keyRowRect.y, toggleW, keyRowRect.height),
                showKey ? "隐藏" : "显示"))
            {
                _showApiKey[alias] = !showKey;
            }
            cursorY += 28f + GapTiny;

            // ModelName
            var modelDisplay = string.IsNullOrEmpty(cred.ModelName) ? "(未设置)" : cred.ModelName;
            Widgets.Label(new Rect(x + 4f, cursorY, w - 8f, 22f),
                $"<color=#999999><size=11>Model:</size></color> {modelDisplay}");
            cursorY += 22f;

            // 测试错误提示
            if (testSt == "failed" && _testError.TryGetValue(alias, out var errMsg))
            {
                Widgets.Label(new Rect(x + 4f, cursorY, w - 8f, 16f),
                    $"<color=#FF6666><size=10>✗ {errMsg}</size></color>");
                cursorY += 16f;
            }

            cursorY += GapSmall;

            // 操作按钮行
            var btnRowY = cursorY;
            var btnCursorX = x + 4f;

            // 测试按钮
            var testLabel = testSt == "testing" ? "测试中…" : "测试连接";
            var origColor = GUI.color;
            if (testSt == "testing") GUI.color = ColorTestRunning;
            if (Widgets.ButtonText(new Rect(btnCursorX, btnRowY, 80f, BtnHeight), testLabel)
                && testSt != "testing")
            {
                StartTest(alias, cred);
            }
            GUI.color = origColor;
            btnCursorX += 80f + BtnGap;

            // 获取模型列表
            var fetching = _fetchingModels.TryGetValue(alias, out var f) && f;
            var fetchLabel = fetching ? "获取中…" : "获取模型列表";
            if (Widgets.ButtonText(new Rect(btnCursorX, btnRowY, BtnWidthMedium, BtnHeight), fetchLabel)
                && !fetching)
            {
                StartDiscover(alias, cred);
            }
            btnCursorX += BtnWidthMedium + BtnGap;

            cursorY += BtnHeight + GapTiny;

            // 模型列表区（可搜索 + 可滚动）
            if (_availableModels.TryGetValue(alias, out var allModels))
            {
                cursorY += GapSmall;
                DrawModelListSection(x + 4f, cursorY, w - 8f, alias, allModels, cred);
            }
        }

        // ---- 模型列表区（可搜索 + 可滚动 + 开关） ----

        private void DrawModelListSection(
            float x, float y, float w, string alias, string[] allModels, LlmCredential cred)
        {
            const float entryHeight = 24f;
            const float searchHeight = 26f;

            // 搜索框
            if (!_modelSearchText.ContainsKey(alias))
                _modelSearchText[alias] = "";
            var searchRect = new Rect(x, y, w, searchHeight);
            _modelSearchText[alias] = Widgets.TextField(searchRect, _modelSearchText[alias]);
            y += searchHeight + GapTiny;

            // 应用正则过滤
            var searchText = _modelSearchText[alias];
            string[] filtered;
            if (string.IsNullOrEmpty(searchText))
            {
                filtered = allModels;
            }
            else
            {
                try
                {
                    var regex = new Regex(searchText, RegexOptions.IgnoreCase);
                    filtered = allModels.Where(m => regex.IsMatch(m)).ToArray();
                }
                catch
                {
                    // 无效正则：回退到简单包含匹配
                    filtered = allModels.Where(m =>
                        m.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                }
            }

            // 统计
            var selected = _selectedModels.TryGetValue(alias, out var sel) ? sel : new HashSet<string>();
            var summaryText = $"<color=#888888><size=10>{filtered.Length}/{allModels.Length} 个模型  |  已启用: {selected.Count}</size></color>";
            Widgets.Label(new Rect(x, y, w, 14f), summaryText);
            y += 14f;

            // 滚动列表区
            var listAreaHeight = ModelListMaxHeight - searchHeight - GapTiny - 14f;
            var viewRect = new Rect(x, y, w, listAreaHeight);
            var contentHeight = filtered.Length * entryHeight;
            var scrollRect = new Rect(x, y, w - 16f, contentHeight);

            if (!_modelScrollPos.ContainsKey(alias))
                _modelScrollPos[alias] = Vector2.zero;
            var scrollPos = _modelScrollPos[alias];
            Widgets.BeginScrollView(viewRect, ref scrollPos, scrollRect);
            _modelScrollPos[alias] = scrollPos;

            for (int i = 0; i < filtered.Length; i++)
            {
                var modelName = filtered[i];
                var entryRect = new Rect(x, y + i * entryHeight, w - 16f, entryHeight);

                // 背景交替
                if (i % 2 == 0)
                    Widgets.DrawBoxSolid(entryRect, new Color(0.18f, 0.18f, 0.18f, 0.5f));

                // 左对齐模型名
                var nameRect = new Rect(entryRect.x + 4f, entryRect.y, entryRect.width - 36f, entryHeight);
                var isSelected = selected.Contains(modelName);
                var nameColor = isSelected ? "#FFFFFF" : "#AAAAAA";
                Widgets.Label(nameRect, $"<color={nameColor}><size=11>{modelName}</size></color>");

                // 右对齐开关
                var toggleRect = new Rect(entryRect.x + entryRect.width - 28f, entryRect.y + 2f, 24f, 20f);
                var toggleLabel = isSelected ? "<color=#88FF88>■</color>" : "<color=#666666>□</color>";
                if (Widgets.ButtonText(toggleRect, toggleLabel))
                {
                    if (!_selectedModels.ContainsKey(alias))
                        _selectedModels[alias] = new HashSet<string>();

                    if (isSelected)
                        _selectedModels[alias].Remove(modelName);
                    else
                        _selectedModels[alias].Add(modelName);

                    // 将第一个选中的模型设为当前模型
                    var firstSelected = _selectedModels[alias].FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstSelected))
                        Manager.SetModel(alias, firstSelected);
                }
            }

            Widgets.EndScrollView();
        }

        // ---- 编辑模式 ----

        private float CalcEditCardHeight(string alias)
        {
            // 使用 CJK 补偿后的行高
            float lineH = 22f * CjkHeightCompensation;
            float keyLineH = 28f * CjkHeightCompensation;

            float h = 28f; // 标题
            h += GapTiny;
            h += lineH; // 名称
            h += GapTiny;
            h += lineH; // BaseUrl
            h += GapTiny;
            h += keyLineH; // ApiKey
            h += GapTiny;
            h += lineH; // ModelName
            h += GapTiny;
            h += 32f; // ProviderType
            h += GapMedium;
            h += 30f; // 按钮行
            return h;
        }

        private void DrawEditCardContent(
            float x, float y, float w, string alias, LlmCredential cred)
        {
            var cursorY = y + 6f;
            var isNew = _addingNew && alias == "__new__";
            var title = isNew ? "新增凭证" : $"编辑: {alias}";
            Widgets.Label(new Rect(x + 4f, cursorY, w - 8f, 24f),
                $"<size=14><b>{title}</b></size>");
            cursorY += 28f + GapTiny;

            // 获取编辑字段（从表单状态或凭证数据）
            string editName, editBaseUrl, editApiKey, editModelName;
            LlmProviderType editProviderType;
            bool editShowKey;

            if (isNew)
            {
                editName = _newName;
                editBaseUrl = _newBaseUrl;
                editApiKey = _newApiKey;
                editModelName = _newModelName;
                editProviderType = _newProviderType;
                editShowKey = _newShowApiKey;
            }
            else
            {
                editName = alias;
                // 从编辑缓冲区读取，回退到凭证原始值
                editBaseUrl = GetEditBuffer(alias, "baseUrl", cred.BaseUrl ?? "");
                editApiKey = GetEditBuffer(alias, "apiKey", cred.ApiKey ?? "");
                editModelName = GetEditBuffer(alias, "modelName", cred.ModelName ?? "");
                var ptStr = GetEditBuffer(alias, "providerType", cred.ProviderType.ToString());
                editProviderType = ptStr == "Anthropic"
                    ? LlmProviderType.Anthropic
                    : LlmProviderType.OpenAI;
                editShowKey = _showApiKey.TryGetValue(alias, out var sk) && sk;
            }

            // 名称（编辑时只读，新增时可编辑）
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>名称</size></color>");
            var nameRect = new Rect(x + 64f, cursorY, w - 68f, 22f);
            if (isNew)
            {
                _newName = Widgets.TextField(nameRect, editName);
            }
            else
            {
                Widgets.Label(nameRect, $"<size=12><b>{alias}</b></size>");
            }
            cursorY += 22f + GapTiny;

            // BaseUrl
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>Base URL</size></color>");
            var urlRect = new Rect(x + 64f, cursorY, w - 68f, 22f);
            var newUrlVal = Widgets.TextField(urlRect, editBaseUrl);
            if (isNew) _newBaseUrl = newUrlVal;
            else UpdateEditField(alias, "baseUrl", newUrlVal);
            cursorY += 22f + GapTiny;

            // ApiKey（隐藏时只读显示脱敏文本，显示时可编辑）
            var keyLabelRect = new Rect(x + 4f, cursorY, 60f, 28f);
            Widgets.Label(keyLabelRect, "<color=#999999><size=11>API Key</size></color>");
            var toggleW = 56f;
            var keyFieldW = w - 68f - toggleW - BtnGap;
            var keyFieldRect = new Rect(x + 64f, cursorY, keyFieldW, 28f);
            var toggleRect = new Rect(x + 64f + keyFieldW + BtnGap, cursorY, toggleW, 28f);

            if (editShowKey)
            {
                // 显示模式：可编辑真实值
                var newKeyVal = Widgets.TextField(keyFieldRect, editApiKey);
                if (isNew) _newApiKey = newKeyVal;
                else UpdateEditField(alias, "apiKey", newKeyVal);
            }
            else
            {
                // 隐藏模式：只读显示脱敏文本，不可编辑
                Widgets.Label(keyFieldRect, $"<color=#CCCCCC><size=12>{MaskApiKey(editApiKey)}</size></color>");
            }

            if (Widgets.ButtonText(toggleRect, editShowKey ? "隐藏" : "显示"))
            {
                if (isNew) _newShowApiKey = !_newShowApiKey;
                else _showApiKey[alias] = !editShowKey;
            }
            cursorY += 28f + GapTiny;

            // ModelName
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>Model</size></color>");
            if (isNew)
            {
                // 新增凭证：模型通过获取模型列表自动发现，无需手动填写
                Widgets.Label(new Rect(x + 64f, cursorY, w - 68f, 22f),
                    "<color=#888888><size=11>保存后展开卡片，点击「获取模型列表」选择</size></color>");
            }
            else
            {
                var modelRect = new Rect(x + 64f, cursorY, w - 68f, 22f);
                var newModelVal = Widgets.TextField(modelRect, editModelName);
                UpdateEditField(alias, "modelName", newModelVal);
            }
            cursorY += 22f + GapTiny;

            // ProviderType
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>类型</size></color>");
            var providerIndex = editProviderType == LlmProviderType.Anthropic ? 1 : 0;
            var selectorRect = new Rect(x + 64f, cursorY, w - 68f, 28f);
            // 简单两个按钮
            var halfW = (selectorRect.width - BtnGap) / 2f;
            var openAiRect = new Rect(selectorRect.x, selectorRect.y, halfW, selectorRect.height);
            var anthropicRect = new Rect(selectorRect.x + halfW + BtnGap, selectorRect.y, halfW, selectorRect.height);

            if (providerIndex == 0)
            {
                Widgets.DrawBoxSolid(openAiRect, new Color(ColorHighlight.r, ColorHighlight.g, ColorHighlight.b, 0.3f));
                Widgets.DrawBox(openAiRect, 1);
                Widgets.Label(openAiRect, "<color=#FFFFFF><b>OpenAI 兼容</b></color>");
                if (Widgets.ButtonText(anthropicRect, "Anthropic"))
                {
                    if (isNew) _newProviderType = LlmProviderType.Anthropic;
                    else UpdateEditField(alias, "providerType", "Anthropic");
                }
            }
            else
            {
                if (Widgets.ButtonText(openAiRect, "OpenAI 兼容"))
                {
                    if (isNew) _newProviderType = LlmProviderType.OpenAI;
                    else UpdateEditField(alias, "providerType", "OpenAI");
                }
                Widgets.DrawBoxSolid(anthropicRect, new Color(ColorHighlight.r, ColorHighlight.g, ColorHighlight.b, 0.3f));
                Widgets.DrawBox(anthropicRect, 1);
                Widgets.Label(anthropicRect, "<color=#FFFFFF><b>Anthropic</b></color>");
            }
            cursorY += 32f + GapMedium;

            // 保存/取消按钮
            var saveLabel = isNew ? "添加" : "保存";
            var saveRect = new Rect(x + 4f, cursorY, BtnWidthMedium, BtnHeight);
            var cancelRect = new Rect(x + 4f + BtnWidthMedium + BtnGap, cursorY, BtnWidthSmall, BtnHeight);

            if (Widgets.ButtonText(saveRect, saveLabel))
            {
                if (isNew)
                    SaveNewCredential();
                else
                    SaveEdit(alias);
            }
            if (Widgets.ButtonText(cancelRect, "取消"))
            {
                if (isNew)
                    CancelAdd();
                else
                    CancelEdit(alias);
            }
        }

        // ================================================================
        // 编辑字段暂存（避免直接修改 cred 对象）
        // ================================================================

        private readonly Dictionary<string, Dictionary<string, string>> _editBuffers
            = new Dictionary<string, Dictionary<string, string>>();

        private string GetEditBuffer(string alias, string field, string defaultValue)
        {
            if (_editBuffers.TryGetValue(alias, out var buf) && buf.TryGetValue(field, out var val))
                return val;
            return defaultValue;
        }

        private void UpdateEditField(string alias, string field, string value)
        {
            if (!_editBuffers.ContainsKey(alias))
                _editBuffers[alias] = new Dictionary<string, string>();
            _editBuffers[alias][field] = value;
        }

        // ================================================================
        // 凭证 CRUD
        // ================================================================

        private void StartEdit(string alias, LlmCredential cred)
        {
            _editing.Add(alias);
            _expanded.Add(alias);
            _pendingDeleteAlias = null;
            // 初始化编辑缓冲区
            _editBuffers[alias] = new Dictionary<string, string>
            {
                ["baseUrl"] = cred.BaseUrl ?? "",
                ["apiKey"] = cred.ApiKey ?? "",
                ["modelName"] = cred.ModelName ?? "",
                ["providerType"] = cred.ProviderType.ToString()
            };
        }

        private void CancelEdit(string alias)
        {
            _editing.Remove(alias);
            _editBuffers.Remove(alias);
            _showApiKey.Remove(alias);
        }

        private void SaveEdit(string alias)
        {
            var buf = _editBuffers.TryGetValue(alias, out var b) ? b : null;
            if (buf == null) { CancelEdit(alias); return; }

            var baseUrl = buf.TryGetValue("baseUrl", out var bu) ? bu : "";
            var apiKey = buf.TryGetValue("apiKey", out var ak) ? ak : "";
            var modelName = buf.TryGetValue("modelName", out var mn) ? mn : "";
            var providerStr = buf.TryGetValue("providerType", out var pt) ? pt : "OpenAI";

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                SetStatus("[错误] Base URL 不能为空");
                return;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus("[错误] API Key 不能为空");
                return;
            }

            var providerType = providerStr == "Anthropic"
                ? LlmProviderType.Anthropic
                : LlmProviderType.OpenAI;

            var cred = new LlmCredential
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                ModelName = string.IsNullOrWhiteSpace(modelName) ? null : modelName,
                ProviderType = providerType
            };

            Manager.Update(alias, cred);
            CancelEdit(alias);
            SetStatus($"已更新: {alias}");
        }

        private void SaveNewCredential()
        {
            if (string.IsNullOrWhiteSpace(_newName))
            {
                SetStatus("[错误] 名称不能为空");
                return;
            }
            if (string.IsNullOrWhiteSpace(_newBaseUrl))
            {
                SetStatus("[错误] Base URL 不能为空");
                return;
            }
            if (string.IsNullOrWhiteSpace(_newApiKey))
            {
                SetStatus("[错误] API Key 不能为空");
                return;
            }

            var cred = new LlmCredential
            {
                BaseUrl = _newBaseUrl,
                ApiKey = _newApiKey,
                ModelName = string.IsNullOrWhiteSpace(_newModelName) ? null : _newModelName,
                ProviderType = _newProviderType
            };

            Manager.Create(_newName.Trim(), cred);

            // 自动加入激活列表
            Manager.Activate(_newName.Trim());

            SetStatus($"已添加: {_newName.Trim()}");
            CancelAdd();
        }

        private void CancelAdd()
        {
            _addingNew = false;
            _newName = "";
            _newBaseUrl = "";
            _newApiKey = "";
            _newModelName = "";
            _newProviderType = LlmProviderType.OpenAI;
            _newShowApiKey = false;
        }

        // ================================================================
        // 展开/收起
        // ================================================================

        private void ToggleExpand(string alias)
        {
            if (_expanded.Contains(alias))
                _expanded.Remove(alias);
            else
                _expanded.Add(alias);
        }

        private void CleanupCardState(string alias)
        {
            _expanded.Remove(alias);
            _editing.Remove(alias);
            _showApiKey.Remove(alias);
            _testStatus.Remove(alias);
            _testError.Remove(alias);
            _availableModels.Remove(alias);
            _selectedModels.Remove(alias);
            _modelSearchText.Remove(alias);
            _modelScrollPos.Remove(alias);
            _modelsExpanded.Remove(alias);
            _fetchingModels.Remove(alias);
            _editBuffers.Remove(alias);
        }

        // ================================================================
        // 凭证测试
        // ================================================================

        private string GetTestStatus(string alias)
        {
            return _testStatus.TryGetValue(alias, out var s) ? s : "untested";
        }

        private Color GetTestColor(string status)
        {
            switch (status)
            {
                case "success": return ColorTestSuccess;
                case "failed": return ColorTestFailed;
                case "testing": return ColorTestRunning;
                default: return ColorTestUntested;
            }
        }

        private async void StartTest(string alias, LlmCredential cred)
        {
            var llm = LlmService;
            if (llm == null)
            {
                _testStatus[alias] = "failed";
                _testError[alias] = "LLM 服务未初始化";
                return;
            }

            _testStatus[alias] = "testing";
            _testError.Remove(alias);
            _testCts?.Cancel();
            _testCts = new CancellationTokenSource();
            var ct = _testCts.Token;

            try
            {
                var timeoutTask = Task.Delay(5000, ct);
                var testTask = llm.ListModelsAsync(cred, ct);
                var completed = await Task.WhenAny(testTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    _testStatus[alias] = "failed";
                    _testError[alias] = "连接超时 (5s)";
                }
                else
                {
                    await testTask;
                    _testStatus[alias] = "success";
                    _testError.Remove(alias);
                    SetStatus($"{alias}: 连接测试通过");
                }
            }
            catch (OperationCanceledException)
            {
                if (GetTestStatus(alias) == "testing")
                    _testStatus[alias] = "untested";
            }
            catch (Exception ex)
            {
                _testStatus[alias] = "failed";
                _testError[alias] = ex.Message;
            }
            finally
            {
                _testCts?.Dispose();
                _testCts = null;
            }
        }

        // ================================================================
        // 模型发现（单凭证）
        // ================================================================

        private async void StartDiscover(string alias, LlmCredential cred)
        {
            var llm = LlmService;
            if (llm == null)
            {
                SetStatus("[错误] LLM 服务未初始化");
                return;
            }

            _fetchingModels[alias] = true;

            try
            {
                var models = await llm.ListModelsAsync(cred, CancellationToken.None);
                _availableModels[alias] = models ?? new string[0];
                // 初始化选中状态：当前模型的凭证自动选中
                if (!_selectedModels.ContainsKey(alias))
                    _selectedModels[alias] = new HashSet<string>();
                if (!string.IsNullOrEmpty(cred.ModelName))
                    _selectedModels[alias].Add(cred.ModelName);
                _modelsExpanded[alias] = true;
                SetStatus($"{alias}: 发现 {models?.Length ?? 0} 个模型");
            }
            catch (Exception ex)
            {
                _availableModels[alias] = new string[0];
                SetStatus($"{alias}: 查询失败 - {ex.Message}");
            }
            finally
            {
                _fetchingModels[alias] = false;
            }
        }

        // ================================================================
        // 激活顺序
        // ================================================================

        private void DrawActivationOrder(Listing_Standard listing)
        {
            var allAliases = Manager.GetAll().Select(x => x.Name).ToList();
            if (allAliases.Count == 0) return;

            BeginSection(listing, "激活顺序（Fallback 链路）");

            var activeList = Manager.GetActivationOrder().ToList();
            var inactiveList = allAliases.Where(a => !activeList.Contains(a)).ToList();

            if (activeList.Count == 0)
            {
                Widgets.Label(listing.GetRect(20f),
                    "<color=#888888><size=11>未激活任何凭证。运行时将无法使用 LLM。</size></color>");
            }
            else
            {
                for (int i = 0; i < activeList.Count; i++)
                {
                    var alias = activeList[i];
                    var rowRect = listing.GetRect(28f);

                    // 序号
                    Widgets.Label(new Rect(rowRect.x, rowRect.y + 4f, 24f, 20f),
                        $"<color=#888888><size=11>{i + 1}.</size></color>");

                    // 名称
                    Widgets.Label(new Rect(rowRect.x + 26f, rowRect.y + 4f, 120f, 20f),
                        $"<size=12><b>{alias}</b></size>");

                    // 右侧按钮（从右到左）
                    var btnRight = rowRect.x + rowRect.width;
                    const float smallBtnW = 56f;
                    const float moveBtnW = 32f;

                    // 移除
                    btnRight -= smallBtnW;
                    if (Widgets.ButtonText(new Rect(btnRight, rowRect.y + 2f, smallBtnW, 24f), "移除"))
                    {
                        activeList.RemoveAt(i);
                        Manager.SetActivationOrder(activeList);
                        SetStatus($"{alias} 已移除激活列表");
                        break;
                    }

                    // 下移
                    btnRight -= moveBtnW + BtnGap;
                    if (i < activeList.Count - 1)
                    {
                        if (Widgets.ButtonText(new Rect(btnRight, rowRect.y + 2f, moveBtnW, 24f), "↓"))
                        {
                            var tmp = activeList[i];
                            activeList[i] = activeList[i + 1];
                            activeList[i + 1] = tmp;
                            Manager.SetActivationOrder(activeList);
                            break;
                        }
                    }

                    // 上移
                    btnRight -= moveBtnW + BtnGap;
                    if (i > 0)
                    {
                        if (Widgets.ButtonText(new Rect(btnRight, rowRect.y + 2f, moveBtnW, 24f), "↑"))
                        {
                            var tmp = activeList[i];
                            activeList[i] = activeList[i - 1];
                            activeList[i - 1] = tmp;
                            Manager.SetActivationOrder(activeList);
                            break;
                        }
                    }
                }
            }

            // 未激活的凭证：点击加入
            if (inactiveList.Count > 0)
            {
                listing.Gap(GapTiny);
                Widgets.Label(listing.GetRect(18f),
                    "<color=#888888><size=11>点击加入激活列表:</size></color>");

                foreach (var alias in inactiveList)
                {
                    var addRect = listing.GetRect(24f);
                    if (Widgets.ButtonText(new Rect(addRect.x, addRect.y, 100f, 22f), $"+ {alias}"))
                    {
                        var newActive = Manager.GetActivationOrder().ToList();
                        newActive.Add(alias);
                        Manager.SetActivationOrder(newActive);
                        SetStatus($"{alias} 已加入激活列表");
                    }
                }
            }

            EndSection(listing);
        }

        // ================================================================
        // 新增凭证卡片
        // ================================================================

        private void DrawNewCredentialCard(Listing_Standard listing)
        {
            var cardHeight = CalcEditCardHeight("__new__");
            var cardRect = listing.GetRect(cardHeight);
            Widgets.DrawBoxSolid(cardRect, ColorCardBg);
            Widgets.DrawBox(cardRect, 1);

            var pad = GapSmall;
            DrawEditCardContent(
                cardRect.x + pad, cardRect.y,
                cardRect.width - pad * 2,
                "__new__", null);
        }

        private void DrawAddButton(Listing_Standard listing)
        {
            if (_addingNew) return;
            var results = DrawButtonRow(listing,
                new[] { "+ 添加凭证" },
                new[] { BtnWidthMedium });
            if (results[0])
            {
                _addingNew = true;
                _pendingDeleteAlias = null;
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusMessageTime = Time.time;
        }

        private static string MaskApiKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(未设置)";
            if (key.Length <= 8) return new string('*', key.Length);
            return key.Substring(0, 4) + new string('*', Math.Min(key.Length - 8, 8)) + key.Substring(key.Length - 4);
        }

        private static string TruncateUrl(string url, int maxLen)
        {
            if (string.IsNullOrEmpty(url)) return "(未设置)";
            if (url.Length <= maxLen) return url;
            return url.Substring(0, maxLen - 3) + "...";
        }
    }
}

