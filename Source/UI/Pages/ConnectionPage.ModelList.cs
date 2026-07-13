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
            var selectedSet = _selectedModels.TryGetValue(alias, out var ss) ? ss : null;
            var enabledCount = selectedSet != null
                ? filtered.Count(m => selectedSet.Contains(m))
                : 0;
            var summaryText = $"<color=#888888><size=10>{filtered.Length}/{allModels.Length} 个模型  |  已激活: {enabledCount}</size></color>";
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
                var isSelected = selectedSet != null && selectedSet.Contains(modelName);
                var nameColor = isSelected ? "#FFFFFF" : "#AAAAAA";
                Widgets.Label(nameRect, $"<color={nameColor}><size=11>{modelName}</size></color>");

                // 右对齐：多选勾选（点击切换激活状态，并立即持久化）
                var toggleRect = new Rect(entryRect.x + entryRect.width - 28f, entryRect.y + 2f, 24f, 20f);
                var toggleLabel = isSelected ? "<color=#88FF88>☑</color>" : "<color=#666666>☐</color>";
                if (Widgets.ButtonText(toggleRect, toggleLabel))
                {
                    ToggleModelSelection(alias, modelName, cred);
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
                var modelArray = models ?? new string[0];
                _availableModels[alias] = modelArray;

                // 初始化多选集合：首次发现时自动激活所有模型
                if (!_selectedModels.ContainsKey(alias))
                    _selectedModels[alias] = new HashSet<string>();
                var selectedSet = _selectedModels[alias];
                foreach (var m in modelArray)
                    selectedSet.Add(m);

                // 将发现的模型同步到凭证并持久化
                var updated = cred.Clone();
                updated.ModelNames = new List<string>(modelArray);
                Manager.Update(alias, updated);

                _modelsExpanded[alias] = true;
                SetStatus($"{alias}: 发现 {modelArray.Length} 个模型（已自动激活）");
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
        // 模型多选切换（立即持久化）
        // ================================================================

        /// <summary>
        /// 切换单个模型的激活状态：勾选 → 添加到凭证 ModelNames；取消 → 移除。
        /// 立即通过 Manager.Update 持久化到 ModSettings。
        /// </summary>
        private void ToggleModelSelection(string alias, string modelName, LlmCredential cred)
        {
            if (!_selectedModels.TryGetValue(alias, out var selectedSet))
            {
                selectedSet = new HashSet<string>();
                _selectedModels[alias] = selectedSet;
            }

            if (selectedSet.Contains(modelName))
            {
                selectedSet.Remove(modelName);
            }
            else
            {
                selectedSet.Add(modelName);
            }

            // 同步到凭证并持久化
            var updated = cred.Clone();
            updated.ModelNames = new List<string>(selectedSet);
            Manager.Update(alias, updated);
        }
    }
}
