using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NPCLife.Framework.Llm;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// ConnectionPage 的模型列表部分。
    /// 包含可搜索的模型列表渲染和模型发现（ListModelsAsync）。
    /// </summary>
    public partial class ConnectionPage
    {
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
            var currentModel = _selectedModels.TryGetValue(alias, out var cm) ? cm : null;
            var enabledCount = string.IsNullOrEmpty(currentModel) ? 0 : 1;
            var summaryText = $"<color=#888888><size=10>{filtered.Length}/{allModels.Length} 个模型  |  当前: {(string.IsNullOrEmpty(currentModel) ? "无" : currentModel)}</size></color>";
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
                var isSelected = modelName == currentModel;
                var nameColor = isSelected ? "#FFFFFF" : "#AAAAAA";
                Widgets.Label(nameRect, $"<color={nameColor}><size=11>{modelName}</size></color>");

                // 右对齐：单选切换（点击即设为当前模型，再次点击取消）
                var toggleRect = new Rect(entryRect.x + entryRect.width - 28f, entryRect.y + 2f, 24f, 20f);
                var toggleLabel = isSelected ? "<color=#88FF88>●</color>" : "<color=#666666>○</color>";
                if (Widgets.ButtonText(toggleRect, toggleLabel))
                {
                    if (isSelected)
                    {
                        // 取消选择
                        _selectedModels.Remove(alias);
                        Manager.SetModel(alias, null);
                    }
                    else
                    {
                        // 选中此模型
                        _selectedModels[alias] = modelName;
                        Manager.SetModel(alias, modelName);
                    }
                }
            }

            Widgets.EndScrollView();
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
                // 同步到共享字典，供 RunStrategyPage 读取
                RimLifeCore.DiscoveredModels[alias] = new List<string>(models ?? new string[0]);
                // 初始化选中状态：当前凭证的第一个模型自动设为选中
                if (cred.ModelNames != null && cred.ModelNames.Count > 0 && !_selectedModels.ContainsKey(alias))
                    _selectedModels[alias] = cred.ModelNames[0];
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
    }
}
