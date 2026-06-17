using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimLife.Core;
using RimLife.Framework.Llm;
using RimLife.Infrastructure;
using RimLife.UI.Models;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI.Pages
{
    /// <summary>
    /// LLM 连接配置页面。
    /// 信息架构：状态摘要 → 凭证管理 → 模型选择。
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

        private enum PageMode { ViewCards, EditCard, AddCard }
        private PageMode _mode = PageMode.ViewCards;

        // 编辑/新增表单字段
        private string _editCardId;
        private string _editLabel = "";
        private string _editBaseUrl = "";
        private string _editApiKey = "";
        private LlmProviderType _editProviderType = LlmProviderType.OpenAI;

        // 模型发现状态
        private bool _isDiscovering;
        private string _discoveryStatus = "";
        private bool _discoveryComplete;
        private string _discoveryError;

        // 操作反馈
        private string _statusMessage;
        private float _statusMessageTime;

        // 删除确认（两步确认）
        private string _pendingDeleteCardId;

        // API Key 显示切换
        private bool _showApiKey;

        // 手动添加模型输入
        private Dictionary<string, string> _manualModelInputs = new Dictionary<string, string>();

        private CancellationTokenSource _discoveryCts;

        // 缓存
        private LlmCredentialManager Mgr => LlmCredentialManager.Instance;

        // ================================================================
        // Draw
        // ================================================================

        public void Draw(Rect rect, Listing_Standard listing)
        {
            DrawStatusMessage(listing);

            switch (_mode)
            {
                case PageMode.ViewCards:
                    DrawStatusSummary(listing);
                    DrawCredentialSection(listing);
                    DrawModelSection(listing);
                    break;
                case PageMode.EditCard:
                    DrawCardEditForm(listing, isNew: false);
                    break;
                case PageMode.AddCard:
                    DrawCardEditForm(listing, isNew: true);
                    break;
            }
        }

        // ================================================================
        // 状态消息（临时反馈，5 秒淡出）
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
            var cards = Mgr.Cards;
            var models = Mgr.DiscoveredModels;
            var order = Mgr.ActiveModelOrder;

            var activeCards = cards.Count(c => c.IsActive && c.IsValid());
            var selectedModels = models.Count(m => m.IsSelected);
            var isReady = activeCards > 0 && order.Count > 0;

            // 状态栏背景
            var summaryRect = listing.GetRect(52f);
            var bgColor = isReady
                ? new Color(0.15f, 0.22f, 0.15f, 1f)
                : new Color(0.22f, 0.18f, 0.15f, 1f);
            Widgets.DrawBoxSolid(summaryRect, bgColor);
            Widgets.DrawBox(summaryRect, 1);

            // 第一行：状态指示 + 当前模型
            var row1Rect = new Rect(summaryRect.x + GapMedium, summaryRect.y + 6f, summaryRect.width - GapMedium * 2, 20f);
            var statusIcon = isReady ? "<color=#88FF88>●</color>" : "<color=#FF8844>○</color>";
            var statusText = isReady ? "已就绪" : "未就绪";

            string currentModelInfo = "";
            if (order.Count > 0)
            {
                var currentModel = order[Mgr.CurrentModelIndex % order.Count];
                var sourceCard = models.FirstOrDefault(m => m.ModelName == currentModel);
                var cardLabel = cards.FirstOrDefault(c => c.Id == sourceCard?.SourceCardId)?.Label ?? "";
                currentModelInfo = $"  当前模型: <b>{currentModel}</b>" +
                    (string.IsNullOrEmpty(cardLabel) ? "" : $"  (来源: {cardLabel})");
            }

            Widgets.Label(row1Rect, $"<size=13>{statusIcon} {statusText}{currentModelInfo}</size>");

            // 第二行：统计信息
            var row2Rect = new Rect(summaryRect.x + GapMedium, summaryRect.y + 28f, summaryRect.width - GapMedium * 2, 18f);
            Widgets.Label(row2Rect, $"<color=#AAAAAA><size=11>活跃凭证: {activeCards} 张  |  已选模型: {selectedModels} 个  |  运行时队列: {order.Count} 个</size></color>");

            listing.Gap(GapMedium);
        }

        // ================================================================
        // 凭证管理区
        // ================================================================

        private void DrawCredentialSection(Listing_Standard listing)
        {
            BeginSection(listing, "凭证");

            var cards = Mgr.Cards;
            if (cards.Count == 0)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#888888><size=12>尚无凭证，点击下方按钮添加。</size></color>");
                listing.Gap(GapSmall);
            }
            else
            {
                foreach (var card in cards.ToList())
                {
                    DrawCredentialRow(listing, card);
                }
                listing.Gap(GapSmall);
            }

            // 添加凭证按钮
            var addResults = DrawButtonRow(listing,
                new[] { "+ 添加凭证" },
                new[] { BtnWidthMedium });
            if (addResults[0])
            {
                _pendingDeleteCardId = null;
                StartAddCard();
            }

            EndSection(listing);
        }

        private void DrawCredentialRow(Listing_Standard listing, ApiCredentialCard card)
        {
            var rowHeight = 32f;
            var rowRect = listing.GetRect(rowHeight);

            // 激活态背景
            if (card.IsActive)
            {
                Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.22f, 0.18f, 1f));
            }

            // hover 效果
            DrawHoverBackground(rowRect, new Color(0.25f, 0.25f, 0.25f, 0.5f));

            var pad = GapSmall;
            var leftX = rowRect.x + pad;
            var contentWidth = rowRect.width - pad * 2;

            // 复选框 + 标签（左侧）
            var checkWidth = Mathf.Min(140f, contentWidth * 0.25f);
            var checkRect = new Rect(leftX, rowRect.y + 3f, checkWidth, 26f);
            bool isActive = card.IsActive;
            Widgets.CheckboxLabeled(checkRect, $"<size=12>{card.Label}</size>", ref isActive);
            if (isActive != card.IsActive)
            {
                Mgr.SetCardActive(card.Id, isActive);
            }

            // URL + Key 摘要（中间）
            var infoX = leftX + checkWidth + GapSmall;
            var infoWidth = contentWidth - checkWidth - BtnWidthSmall * 2 - BtnGap * 2 - GapSmall;
            var infoRect = new Rect(infoX, rowRect.y + 7f, infoWidth, 18f);

            var displayUrl = card.BaseUrl.Length > 30 ? card.BaseUrl.Substring(0, 27) + "..." : card.BaseUrl;
            var maskedKey = MaskApiKey(card.ApiKey);
            Widgets.Label(infoRect, $"<color=#AAAAAA><size=11>{displayUrl}  {maskedKey}  {card.ProviderType}</size></color>");

            // 操作按钮（右侧）
            var btnY = rowRect.y + 3f;
            var btnRight = rowRect.x + rowRect.width - pad;

            // 删除按钮（两步确认）
            var delWidth = BtnWidthSmall;
            var delRect = new Rect(btnRight - delWidth, btnY, delWidth, 26f);
            var isPendingDelete = _pendingDeleteCardId == card.Id;
            if (isPendingDelete)
            {
                Widgets.DrawBoxSolid(delRect, ColorDangerBg);
                if (Widgets.ButtonText(delRect, "确认删除"))
                {
                    Mgr.RemoveCard(card.Id);
                    SetStatus($"已删除: {card.Label}");
                    _pendingDeleteCardId = null;
                }
            }
            else
            {
                if (Widgets.ButtonText(delRect, "×"))
                {
                    _pendingDeleteCardId = card.Id;
                }
            }

            // 编辑按钮
            var editRect = new Rect(delRect.x - BtnWidthSmall - BtnGap, btnY, BtnWidthSmall, 26f);
            if (Widgets.ButtonText(editRect, "编辑"))
            {
                _pendingDeleteCardId = null;
                StartEditCard(card);
            }

            listing.Gap(GapTiny);
        }

        // ================================================================
        // 卡片编辑/新增表单
        // ================================================================

        private void StartEditCard(ApiCredentialCard card)
        {
            _editCardId = card.Id;
            _editLabel = card.Label;
            _editBaseUrl = card.BaseUrl;
            _editApiKey = card.ApiKey;
            _editProviderType = card.ProviderType;
            _mode = PageMode.EditCard;
        }

        private void StartAddCard()
        {
            _editCardId = null;
            _editLabel = "";
            _editBaseUrl = "";
            _editApiKey = "";
            _editProviderType = LlmProviderType.OpenAI;
            _mode = PageMode.AddCard;
        }

        private void DrawCardEditForm(Listing_Standard listing, bool isNew)
        {
            var title = isNew ? "新增凭证" : "编辑凭证";
            BeginSection(listing, title);

            // 标签
            Widgets.Label(listing.GetRect(20f), "<size=12>卡片标签</size>");
            var labelRect = listing.GetRect(28f);
            _editLabel = Widgets.TextField(labelRect, _editLabel);
            listing.Gap(GapTiny);

            // Base URL
            Widgets.Label(listing.GetRect(20f), "<size=12>API 基础 URL</size>");
            var urlRect = listing.GetRect(28f);
            _editBaseUrl = Widgets.TextField(urlRect, _editBaseUrl);
            listing.Gap(GapTiny);

            // API Key（带显示/隐藏切换）
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
                SaveCardEdit(isNew);
            }
            if (btnResults[1])
            {
                _showApiKey = false;
                _mode = PageMode.ViewCards;
            }

            EndSection(listing);
        }

        private void SaveCardEdit(bool isNew)
        {
            if (string.IsNullOrWhiteSpace(_editBaseUrl))
            {
                SetStatus("[错误] Base URL 不能为空");
                return;
            }

            if (isNew)
            {
                var card = Mgr.AddCard(_editLabel, _editBaseUrl, _editApiKey, _editProviderType);
                SetStatus($"已添加: {card.Label}");
            }
            else
            {
                var cards = Mgr.Cards;
                var existing = cards.FirstOrDefault(c => c.Id == _editCardId);
                if (existing != null)
                {
                    existing.Label = _editLabel;
                    existing.BaseUrl = _editBaseUrl;
                    existing.ApiKey = _editApiKey;
                    existing.ProviderType = _editProviderType;
                    Mgr.UpdateCard(existing);
                    SetStatus($"已更新: {existing.Label}");
                }
            }

            _mode = PageMode.ViewCards;
        }

        // ================================================================
        // 模型选择区
        // ================================================================

        private void DrawModelSection(Listing_Standard listing)
        {
            var models = Mgr.DiscoveredModels;
            var cards = Mgr.Cards;

            BeginSection(listing, "模型");

            // 获取模型列表按钮
            var btnLabel = _isDiscovering ? "正在获取..." : "获取模型列表";
            var hasActiveCard = cards.Any(c => c.IsActive && c.IsValid());
            var btnResults = DrawButtonRow(listing,
                new[] { btnLabel },
                new[] { BtnWidthLarge });

            if (btnResults[0] && !_isDiscovering && hasActiveCard)
            {
                StartModelDiscovery();
            }

            if (!hasActiveCard)
            {
                Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>请先添加并激活至少一张凭证。</size></color>");
            }

            // 发现进度
            if (_isDiscovering && !string.IsNullOrEmpty(_discoveryStatus))
            {
                Widgets.Label(listing.GetRect(22f), $"<color=#88AAFF><size=12>{_discoveryStatus}</size></color>");
            }

            if (_discoveryComplete)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#88FF88><size=12>模型发现完成</size></color>");
            }

            if (!string.IsNullOrEmpty(_discoveryError))
            {
                Widgets.Label(listing.GetRect(22f), $"<color=#FF6666><size=12>{_discoveryError}</size></color>");
            }

            listing.Gap(GapSmall);

            // 模型列表（按来源分组）
            if (models.Count > 0)
            {
                DrawModelList(listing, models, cards);
            }

            // 手动添加模型
            DrawManualModelEntry(listing, cards);

            listing.Gap(GapSmall);

            // 应用按钮 + 运行时信息
            int selectedCount = models.Count(m => m.IsSelected);
            var applyResults = DrawButtonRow(listing,
                new[] { $"应用选择 ({selectedCount})" },
                new[] { BtnWidthMedium });
            if (applyResults[0])
            {
                Mgr.BuildActiveModelOrder();
                SetStatus($"已应用: {selectedCount} 个模型就绪");
            }

            // 运行时策略说明
            var order = Mgr.ActiveModelOrder;
            if (order.Count > 0)
            {
                var infoRect = listing.GetRect(22f);
                Widgets.Label(infoRect,
                    $"<color=#888888><size=11>运行时策略: 按顺序尝试，失败自动切换下一个 | 队列: {order.Count} 个</size></color>");
            }

            EndSection(listing);
        }

        private void DrawModelList(Listing_Standard listing, List<ModelEntry> models, List<ApiCredentialCard> cards)
        {
            var grouped = models.GroupBy(m => m.SourceCardId);

            foreach (var group in grouped)
            {
                var card = cards.FirstOrDefault(c => c.Id == group.Key);
                var cardLabel = card?.Label ?? "未知来源";
                var cardActive = card?.IsActive ?? false;

                // 分组标题
                var headerRect = listing.GetRect(22f);
                var activeTag = cardActive ? "<color=#88FF88>●</color>" : "<color=#666666>○</color>";
                Widgets.Label(headerRect, $"<size=12>{activeTag} {cardLabel}</size>");

                // 模型列表（紧凑排列，每行多个）
                var modelList = group.ToList();
                var modelsPerRow = 2;
                var modelWidth = (listing.ColumnWidth - GapSmall) / modelsPerRow;

                for (int i = 0; i < modelList.Count; i += modelsPerRow)
                {
                    var rowRect = listing.GetRect(22f);
                    for (int j = 0; j < modelsPerRow && i + j < modelList.Count; j++)
                    {
                        var model = modelList[i + j];
                        var modelRect = new Rect(rowRect.x + j * (modelWidth + GapSmall), rowRect.y, modelWidth, 22f);
                        bool selected = model.IsSelected;
                        Widgets.CheckboxLabeled(modelRect, $" {model.ModelName}", ref selected);
                        if (selected != model.IsSelected)
                        {
                            Mgr.SetModelSelected(model.ModelName, selected);
                        }
                    }
                }

                listing.Gap(GapTiny);
            }
        }

        private void DrawManualModelEntry(Listing_Standard listing, List<ApiCredentialCard> cards)
        {
            var activeCards = cards.Where(c => c.IsActive).ToList();
            if (activeCards.Count == 0) return;

            listing.Gap(GapSmall);
            Widgets.Label(listing.GetRect(20f), "<color=#888888><size=11>手动添加模型（用于不支持列表查询的 API）:</size></color>");
            listing.Gap(GapTiny);

            foreach (var card in activeCards)
            {
                // 输入框 + 添加按钮
                var rowRect = listing.GetRect(28f);
                var inputWidth = rowRect.width - BtnWidthMedium - BtnGap;
                var inputRect = new Rect(rowRect.x, rowRect.y, inputWidth, 28f);
                var btnRect = new Rect(rowRect.x + inputWidth + BtnGap, rowRect.y, BtnWidthMedium, 28f);

                // 确保输入状态存在
                if (!_manualModelInputs.ContainsKey(card.Id))
                {
                    _manualModelInputs[card.Id] = "";
                }

                _manualModelInputs[card.Id] = Widgets.TextField(inputRect, _manualModelInputs[card.Id]);

                if (Widgets.ButtonText(btnRect, $"添加到 {card.Label}"))
                {
                    var modelName = _manualModelInputs[card.Id]?.Trim();
                    if (!string.IsNullOrEmpty(modelName))
                    {
                        Mgr.AddManualModel(modelName, card.Id);
                        SetStatus($"已添加: {modelName} → {card.Label}");
                        _manualModelInputs[card.Id] = "";
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
            _discoveryComplete = false;
            _discoveryError = null;
            _discoveryStatus = "正在查询模型列表...";
            _discoveryCts = new CancellationTokenSource();

            try
            {
                var models = await Mgr.DiscoverModelsAsync(
                    llmService,
                    (current, total, label, modelCount) =>
                    {
                        if (modelCount >= 0)
                            _discoveryStatus = $"[{current}/{total}] {label}: 发现 {modelCount} 个模型";
                        else
                            _discoveryStatus = $"[{current}/{total}] {label}: 查询失败";
                    },
                    _discoveryCts.Token);

                _discoveryComplete = true;
                _discoveryStatus = "";
                SetStatus($"共发现 {models.Count} 个模型（来自 {models.Select(m => m.SourceCardId).Distinct().Count()} 个端点）");
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
