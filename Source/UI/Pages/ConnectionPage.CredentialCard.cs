using System;
using System.Collections.Generic;
using NPCLife.Framework.Llm;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// ConnectionPage 的凭证卡片渲染部分。
    /// 包含紧凑行、展开内容、编辑表单三种状态的绘制与高度计算。
    /// </summary>
    public partial class ConnectionPage
    {
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
            var urlDisplay = Truncate(cred.BaseUrl, 30, "(未设置)");
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
                    RimLifeCore.DiscoveredModels.Remove(alias);
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
            var modelDisplay = (cred.ModelNames != null && cred.ModelNames.Count > 0) 
                ? string.Join(", ", cred.ModelNames) : "(未设置)";
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
            h += lineH; // ModelsEndpoint
            h += GapTiny;
            h += lineH; // ChatEndpoint
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
            string editName, editBaseUrl, editApiKey, editModelName, editModelsEndpoint, editChatEndpoint;
            LlmProviderType editProviderType;
            bool editShowKey;

            if (isNew)
            {
                editName = _newName;
                editBaseUrl = _newBaseUrl;
                editApiKey = _newApiKey;
                editModelName = _newModelName;
                editModelsEndpoint = "/v1/models";
                editChatEndpoint = "/v1/chat/completions";
                editProviderType = _newProviderType;
                editShowKey = _newShowApiKey;
            }
            else
            {
                editName = alias;
                // 从编辑缓冲区读取，回退到凭证原始值
                editBaseUrl = GetEditBuffer(alias, "baseUrl", cred.BaseUrl ?? "");
                editApiKey = GetEditBuffer(alias, "apiKey", cred.ApiKey ?? "");
                editModelName = GetEditBuffer(alias, "modelName", (cred.ModelNames != null && cred.ModelNames.Count > 0) ? cred.ModelNames[0] : "");
                editModelsEndpoint = GetEditBuffer(alias, "modelsEndpoint", cred.ModelsEndpoint ?? "/v1/models");
                editChatEndpoint = GetEditBuffer(alias, "chatEndpoint", cred.ChatEndpoint ?? "/v1/chat/completions");
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

            // ModelsEndpoint
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>模型端点</size></color>");
            var endpointRect = new Rect(x + 64f, cursorY, w - 68f, 22f);
            var newEndpointVal = Widgets.TextField(endpointRect, editModelsEndpoint);
            UpdateEditField(alias, "modelsEndpoint", newEndpointVal);
            cursorY += 22f + GapTiny;

            // ChatEndpoint
            Widgets.Label(new Rect(x + 4f, cursorY, 60f, 22f),
                "<color=#999999><size=11>对话端点</size></color>");
            var chatEndpointRect = new Rect(x + 64f, cursorY, w - 68f, 22f);
            var newChatEndpointVal = Widgets.TextField(chatEndpointRect, editChatEndpoint);
            UpdateEditField(alias, "chatEndpoint", newChatEndpointVal);
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
    }
}
