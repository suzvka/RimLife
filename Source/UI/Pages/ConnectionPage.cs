using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <remarks>
    /// 部分实现拆分：
    /// <list type="bullet">
    /// <item><c>ConnectionPage.CredentialCard.cs</c> — 卡片三态绘制（紧凑/展开/编辑）与编辑缓冲区</item>
    /// <item><c>ConnectionPage.ModelList.cs</c> — 模型列表渲染与模型发现</item>
    /// </list>
    /// </remarks>
    public partial class ConnectionPage : IConfigPage
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
        private readonly Dictionary<string, string> _selectedModels = new Dictionary<string, string>();
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
        private string _newChatEndpoint = "/v1/chat/completions";
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
        // 单个凭证卡片（调度：紧凑/展开/编辑）
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
                ["modelName"] = (cred.ModelNames != null && cred.ModelNames.Count > 0) ? cred.ModelNames[0] : "",
                ["providerType"] = cred.ProviderType.ToString(),
                ["modelsEndpoint"] = cred.ModelsEndpoint ?? "/v1/models"
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
            var chatEndpoint = buf.TryGetValue("chatEndpoint", out var ce) ? ce : "/v1/chat/completions";
            var providerStr = buf.TryGetValue("providerType", out var pt) ? pt : "OpenAI";
            var modelsEndpoint = buf.TryGetValue("modelsEndpoint", out var me) ? me : "/v1/models";

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
                ModelNames = string.IsNullOrWhiteSpace(modelName) ? new List<string>() : new List<string> { modelName },
                ProviderType = providerType,
                ChatEndpoint = string.IsNullOrWhiteSpace(chatEndpoint) ? "/v1/chat/completions" : chatEndpoint,
                ModelsEndpoint = string.IsNullOrWhiteSpace(modelsEndpoint) ? "/v1/models" : modelsEndpoint
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
                ModelNames = string.IsNullOrWhiteSpace(_newModelName) ? new List<string>() : new List<string> { _newModelName },
                ProviderType = _newProviderType,
                ChatEndpoint = string.IsNullOrWhiteSpace(_newChatEndpoint) ? "/v1/chat/completions" : _newChatEndpoint
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
            _newChatEndpoint = "/v1/chat/completions";
            _newProviderType = LlmProviderType.OpenAI;
            _newShowApiKey = false;
        }

        // ================================================================
        // 展开/收起与状态清理
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
    }
}
