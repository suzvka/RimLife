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
    /// 管理多张 API 凭证卡片（baseUrl + key 二元组）、
    /// 无状态模型发现以及模型选择。
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
                    DrawCardSection(listing);
                    DrawActionButtons(listing);
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
        // 状态消息
        // ================================================================

        private void DrawStatusMessage(Listing_Standard listing)
        {
            if (!string.IsNullOrEmpty(_statusMessage) && Time.time - _statusMessageTime < 5f)
            {
                var color = _statusMessage.StartsWith("[错误]") ? "#FF6666" : "#88FF88";
                Widgets.Label(listing.GetRect(22f), $"<color={color}><size=12>{_statusMessage}</size></color>");
                listing.Gap(GapSmall);
            }
        }

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusMessageTime = Time.time;
        }

        // ================================================================
        // 卡片列表区
        // ================================================================

        private void DrawCardSection(Listing_Standard listing)
        {
            BeginSection(listing, "API 凭证卡片");

            var cards = Mgr.Cards;
            if (cards.Count == 0)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#888888><size=12>尚无凭证卡片，点击下方按钮添加。</size></color>");
                listing.Gap(GapSmall);
                EndSection(listing);
                return;
            }

            foreach (var card in cards.ToList()) // 拷贝避免迭代中修改
            {
                DrawSingleCard(listing, card);
            }

            EndSection(listing);
        }

        private void DrawSingleCard(Listing_Standard listing, ApiCredentialCard card)
        {
            var cardHeight = 88f;
            var cardRect = listing.GetRect(cardHeight);

            // 卡片背景
            var bgColor = card.IsActive
                ? new Color(0.18f, 0.22f, 0.18f, 1f)
                : new Color(0.18f, 0.18f, 0.18f, 1f);
            Widgets.DrawBoxSolid(cardRect, bgColor);

            var pad = GapSmall;

            // ================================================================
            // Row 1: 复选框 + 标签（左） + 操作按钮（右）
            // ================================================================
            var row1Y = cardRect.y + 6f;
            var row1H = 26f;

            // 激活复选框
            var checkRect = new Rect(cardRect.x + pad, row1Y, cardRect.width * 0.55f, row1H);
            bool isActive = card.IsActive;
            Widgets.CheckboxLabeled(checkRect, $"<size=13>{card.Label}</size>", ref isActive);
            if (isActive != card.IsActive)
            {
                Mgr.SetCardActive(card.Id, isActive);
            }

            // 编辑 / 删除按钮（右对齐）
            var btnWidth = 50f;
            var btnGap = 6f;
            var editRect = new Rect(cardRect.x + cardRect.width - btnWidth * 2 - btnGap - pad, row1Y, btnWidth, row1H);
            if (Widgets.ButtonText(editRect, "编辑"))
            {
                StartEditCard(card);
            }

            var delRect = new Rect(cardRect.x + cardRect.width - btnWidth - pad, row1Y, btnWidth, row1H);
            if (Widgets.ButtonText(delRect, "删除"))
            {
                Mgr.RemoveCard(card.Id);
                SetStatus($"已删除卡片: {card.Label}");
            }

            // ================================================================
            // Row 2: Base URL
            // ================================================================
            var row2Y = cardRect.y + 36f;
            var urlRect = new Rect(cardRect.x + pad + 24f, row2Y, cardRect.width - pad * 2 - 24f, 18f);
            var displayUrl = card.BaseUrl.Length > 55 ? card.BaseUrl.Substring(0, 52) + "..." : card.BaseUrl;
            Widgets.Label(urlRect, $"<color=#AAAAAA><size=11>{displayUrl}</size></color>");

            // ================================================================
            // Row 3: API Key 脱敏 + ProviderType
            // ================================================================
            var row3Y = cardRect.y + 56f;
            var keyRect = new Rect(cardRect.x + pad + 24f, row3Y, cardRect.width - pad * 2 - 24f, 18f);
            var maskedKey = MaskApiKey(card.ApiKey);
            Widgets.Label(keyRect, $"<color=#888888><size=11>Key: {maskedKey}  |  {card.ProviderType}</size></color>");

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
            var title = isNew ? "新增凭证卡片" : "编辑凭证卡片";
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

            // API Key
            Widgets.Label(listing.GetRect(20f), "<size=12>API 密钥</size>");
            var keyRect = listing.GetRect(28f);
            _editApiKey = Widgets.TextField(keyRect, _editApiKey);
            listing.Gap(GapTiny);

            // Provider Type
            Widgets.Label(listing.GetRect(20f), "<size=12>提供商类型</size>");
            var provRect = listing.GetRect(30f);
            if (Widgets.ButtonText(new Rect(provRect.x, provRect.y, 130f, 28f),
                _editProviderType == LlmProviderType.OpenAI ? "▶ OpenAI 兼容" : "  OpenAI 兼容"))
            {
                _editProviderType = LlmProviderType.OpenAI;
            }
            if (Widgets.ButtonText(new Rect(provRect.x + 138f, provRect.y, 130f, 28f),
                _editProviderType == LlmProviderType.Anthropic ? "▶ Anthropic" : "  Anthropic"))
            {
                _editProviderType = LlmProviderType.Anthropic;
            }

            listing.Gap(GapMedium);

            // 按钮行
            var btnRow = listing.GetRect(30f);
            var saveLabel = isNew ? "添加卡片" : "保存修改";
            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, 110f, 30f), saveLabel))
            {
                SaveCardEdit(isNew);
            }
            if (Widgets.ButtonText(new Rect(btnRow.x + 118f, btnRow.y, 80f, 30f), "取消"))
            {
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
                SetStatus($"已添加卡片: {card.Label}");
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
                    SetStatus($"已更新卡片: {existing.Label}");
                }
            }

            _mode = PageMode.ViewCards;
        }

        // ================================================================
        // 操作按钮区
        // ================================================================

        private void DrawActionButtons(Listing_Standard listing)
        {
            listing.Gap(GapSmall);

            var btnRow = listing.GetRect(32f);

            // 添加卡片
            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, 140f, 30f), "+ 添加凭证卡片"))
            {
                StartAddCard();
            }

            listing.Gap(GapTiny);

            // 获取模型列表
            var discoverRow = listing.GetRect(32f);
            GUI.enabled = !_isDiscovering && Mgr.Cards.Any(c => c.IsActive && c.IsValid());
            if (Widgets.ButtonText(new Rect(discoverRow.x, discoverRow.y, 170f, 30f),
                _isDiscovering ? "正在获取模型..." : "确认并获取模型列表"))
            {
                StartModelDiscovery();
            }
            GUI.enabled = true;

            // 发现进度
            if (_isDiscovering && !string.IsNullOrEmpty(_discoveryStatus))
            {
                var statusRect = listing.GetRect(22f);
                Widgets.Label(statusRect, $"<color=#88AAFF><size=12>{_discoveryStatus}</size></color>");
            }

            if (_discoveryComplete)
            {
                var doneRect = listing.GetRect(22f);
                Widgets.Label(doneRect, "<color=#88FF88><size=12>✓ 模型发现完成</size></color>");
            }

            if (!string.IsNullOrEmpty(_discoveryError))
            {
                var errRect = listing.GetRect(22f);
                Widgets.Label(errRect, $"<color=#FF6666><size=12>{_discoveryError}</size></color>");
            }

            listing.Gap(GapSmall);
        }

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
        // 模型选择区
        // ================================================================

        private void DrawModelSection(Listing_Standard listing)
        {
            var models = Mgr.DiscoveredModels;

            BeginSection(listing, "可用模型");

            if (models.Count == 0)
            {
                Widgets.Label(listing.GetRect(22f), "<color=#888888><size=12>点击上方按钮获取模型列表。</size></color>");

                // 手动添加模型（用于 Anthropic 等）
                listing.Gap(GapSmall);
                DrawManualModelEntry(listing);
                EndSection(listing);
                return;
            }

            // 全选/全不选
            var selectRow = listing.GetRect(24f);
            if (Widgets.ButtonText(new Rect(selectRow.x, selectRow.y, 60f, 22f), "全选"))
            {
                foreach (var m in models) Mgr.SetModelSelected(m.ModelName, true);
            }
            if (Widgets.ButtonText(new Rect(selectRow.x + 68f, selectRow.y, 80f, 22f), "取消全选"))
            {
                foreach (var m in models) Mgr.SetModelSelected(m.ModelName, false);
            }

            listing.Gap(GapTiny);

            // 按来源卡片分组显示
            var cards = Mgr.Cards;
            var grouped = models.GroupBy(m => m.SourceCardId);

            foreach (var group in grouped)
            {
                var card = cards.FirstOrDefault(c => c.Id == group.Key);
                var cardLabel = card?.Label ?? "未知来源";
                var cardActive = card?.IsActive ?? false;

                var groupHeader = listing.GetRect(22f);
                var activeTag = cardActive ? "<color=#88FF88>●</color>" : "<color=#666666>○</color>";
                Widgets.Label(groupHeader, $"<size=12>{activeTag} 来源: <b>{cardLabel}</b></size>");

                foreach (var model in group)
                {
                    var modelRect = listing.GetRect(22f);
                    bool selected = model.IsSelected;
                    Widgets.CheckboxLabeled(modelRect, $"  {model.ModelName}", ref selected);
                    if (selected != model.IsSelected)
                    {
                        Mgr.SetModelSelected(model.ModelName, selected);
                    }
                }

                listing.Gap(GapTiny);
            }

            // 手动添加模型
            DrawManualModelEntry(listing);

            listing.Gap(GapSmall);

            // 应用按钮
            var applyRow = listing.GetRect(32f);
            int selectedCount = models.Count(m => m.IsSelected);
            if (Widgets.ButtonText(new Rect(applyRow.x, applyRow.y, 150f, 30f),
                $"应用模型选择 ({selectedCount})"))
            {
                Mgr.BuildActiveModelOrder();
                SetStatus($"已应用模型选择: {selectedCount} 个模型就绪");
            }

            // 状态信息
            var order = Mgr.ActiveModelOrder;
            if (order.Count > 0)
            {
                var infoRect = listing.GetRect(22f);
                Widgets.Label(infoRect,
                    $"<color=#AAAAAA><size=12>当前激活: {order.Count} 个模型 | 运行时索引: {Mgr.CurrentModelIndex}</size></color>");
            }

            EndSection(listing);
        }

        private void DrawManualModelEntry(Listing_Standard listing)
        {
            // 手动添加模型（用于不支持的 API）
            var cards = Mgr.Cards;
            if (cards.Count == 0) return;

            listing.Gap(GapTiny);
            Widgets.Label(listing.GetRect(22f), "<color=#888888><size=12>手动添加模型（用于不支持列表查询的 API）:</size></color>");

            // 为每张激活的卡提供输入
            foreach (var card in cards.Where(c => c.IsActive))
            {
                var manualRow = listing.GetRect(28f);
                var btnRect = new Rect(manualRow.x, manualRow.y, 130f, 26f);
                if (Widgets.ButtonText(btnRect, $"+ 添加模型到 {card.Label}"))
                {
                    var defaultModel = card.ProviderType == LlmProviderType.Anthropic
                        ? "claude-sonnet-4-20250514"
                        : "gpt-4o";
                    Mgr.AddManualModel(defaultModel, card.Id);
                    SetStatus($"已手动添加模型: {defaultModel} → {card.Label}");
                }
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
