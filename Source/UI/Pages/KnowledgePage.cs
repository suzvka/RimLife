using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPCLife.Core;
using NPCLife.Infrastructure.Knowledge;
using RimLife.Infrastructure;
using RimLife.Infrastructure.Knowledge;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 知识库页面：展示内置知识库内容，支持模糊搜索和增删改查。
    /// 采用方案 A：客户端缓存 + 子串模糊搜索。
    /// </summary>
    public class KnowledgePage : IConfigPage
    {
        public string Id => "knowledge";
        public string Label => "知识库";
        public string Group => "核心";
        public int Order => 3;

        // ================================================================
        // 数据缓存
        // ================================================================

        private List<KnowledgeEntry> _cachedEntries = new List<KnowledgeEntry>();
        private List<KnowledgeEntry> _filteredEntries = new List<KnowledgeEntry>();

        // ================================================================
        // 搜索与筛选
        // ================================================================

        private string _searchText = "";

        // ================================================================
        // UI 状态
        // ================================================================

        private string _expandedTerm; // 展开详情的词条
        private string _editingTerm; // 正在编辑的词条（null = 非编辑模式）
        private string _editTermBuffer = "";
        private string _editDefBuffer = "";

        private string _pendingDeleteTerm; // 两步确认删除

        private bool _addingNew;
        private string _newTermBuffer = "";
        private string _newDefBuffer = "";

        private string _statusMessage;
        private float _statusMessageTime;

        // ================================================================
        // 模式感知（游戏内锁定 vs 主菜单选择器）
        // ================================================================

        private bool _isInGame;
        private IKnowledgeService _activeKnowledgeBase;

        // 主菜单选择器
        private List<KnowledgeBaseInfo> _availableBases = new List<KnowledgeBaseInfo>();
        private string _selectedBaseId;   // 当前选中的 saveId，null 表示未选择

        // ================================================================
        // 自适应卡片追踪器（每帧测量实际高度，下一帧使用测量值）
        // ================================================================

        private readonly LayoutHelper.AdaptiveCardTracker _formCardTracker
            = new LayoutHelper.AdaptiveCardTracker();
        private readonly LayoutHelper.AdaptiveCardTracker _editCardTracker
            = new LayoutHelper.AdaptiveCardTracker();

        // ================================================================
        // Draw 入口
        // ================================================================

        public void Draw(Rect rect, Listing_Standard listing)
        {
            EnsureActiveBaseCorrect();
            DrawContextHeader(listing);
            listing.Gap(GapSmall);

            EnsureCacheLoaded();

            // 搜索栏
            DrawSearchBar(listing);
            listing.Gap(GapSmall);

            // 状态消息
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);

            // 新增表单（置顶）
            if (_addingNew)
            {
                DrawNewEntryForm(listing);
                listing.Gap(GapSmall);
            }

            // 词条列表
            DrawEntryList(listing);

            // 底栏
            listing.Gap(GapSmall);
            DrawBottomBar(listing);
        }

        // ================================================================
        // 搜索栏
        // ================================================================

        private void DrawSearchBar(Listing_Standard listing)
        {
            var rowRect = listing.GetRect(BtnHeight + 2f);

            // 搜索框
            var searchRect = new Rect(rowRect.x, rowRect.y, rowRect.width, BtnHeight);
            var newSearch = Widgets.TextField(searchRect, _searchText);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                ApplyFilter();
            }
        }

        // ================================================================
        // 词条列表
        // ================================================================

        private void DrawEntryList(Listing_Standard listing)
        {
            if (_filteredEntries.Count == 0)
            {
                listing.Gap(GapTiny);
                var emptyText = _cachedEntries.Count == 0
                    ? "<color=#888888>知识库为空。Agent 尚未学习任何词条。</color>"
                    : "<color=#888888>没有匹配的词条。</color>";
                Widgets.Label(listing.GetRect(40f), emptyText);
                return;
            }

            foreach (var entry in _filteredEntries)
            {
                DrawEntryCard(listing, entry);
                listing.Gap(GapTiny);
            }
        }

        private void DrawEntryCard(Listing_Standard listing, KnowledgeEntry entry)
        {
            var isEditing = _editingTerm == entry.Term;
            var isExpanded = _expandedTerm == entry.Term;
            var isPendingDelete = _pendingDeleteTerm == entry.Term;

            if (isEditing)
            {
                DrawEditForm(listing, entry);
                return;
            }

            // 精确计算卡片高度：顶部填充 + 标题行 + 释义预览 + 底部填充
            var defPreview = UIHelper.Truncate(entry.Definition ?? "", 100);
            var contentWidth = listing.ColumnWidth - 16f;
            var defHeight = CalcTextHeight(defPreview, contentWidth);
            // 4(顶) + 22(标题) + defHeight(预览) + 4(底)
            var cardHeight = 4f + 22f + defHeight + 4f;
            if (isExpanded)
                cardHeight += 4f + CalcTextHeight(entry.Definition ?? "", contentWidth);

            // 卡片背景
            listing.Gap(GapTiny);
            var cardRect = listing.GetRect(cardHeight);
            var bgColor = isPendingDelete ? ColorDangerBg : ColorCardBg;
            Widgets.DrawBoxSolid(cardRect, bgColor);
            Widgets.DrawBox(cardRect, 1);

            // 标题行：词条名 + 操作按钮
            var titleY = cardRect.y + 4f;
            var titleRect = new Rect(cardRect.x + 8f, titleY, cardRect.width * 0.5f, 22f);
            Widgets.Label(titleRect, $"<b>{entry.Term}</b>");

            // 操作按钮（右上角）
            var btnY = cardRect.y + 4f;
            var btnX = cardRect.xMax - BtnWidthSmall * 2 - BtnGap - 8f;

            var editRect = new Rect(btnX, btnY, BtnWidthSmall, 24f);
            if (Widgets.ButtonText(editRect, "编辑"))
            {
                BeginEdit(entry);
            }

            var delRect = new Rect(btnX + BtnWidthSmall + BtnGap, btnY, BtnWidthSmall, 24f);
            var delLabel = isPendingDelete ? "确认删除" : "删除";
            if (Widgets.ButtonText(delRect, delLabel))
            {
                if (isPendingDelete)
                {
                    ExecuteDelete(entry.Term);
                }
                else
                {
                    _pendingDeleteTerm = entry.Term;
                }
            }

            // 释义预览（可点击展开/折叠）
            var previewY = titleY + 22f;

            var previewRect = new Rect(cardRect.x + 8f, previewY, cardRect.width - 16f, defHeight);
            var previewColor = isPendingDelete ? "#FF8888" : "#CCCCCC";
            Widgets.Label(previewRect, $"<color={previewColor}><size=12>{defPreview}</size></color>");

            // 点击卡片主体区域切换展开状态
            var clickArea = new Rect(cardRect.x, previewY, cardRect.width - BtnWidthSmall * 2 - BtnGap - 16f, defHeight);
            if (Widgets.ButtonInvisible(clickArea))
            {
                if (isPendingDelete)
                {
                    // 点击其他区域取消删除确认
                    _pendingDeleteTerm = null;
                }
                else
                {
                    _expandedTerm = isExpanded ? null : entry.Term;
                }
            }

            // 展开详情
            if (isExpanded)
            {
                var detailY = previewY + defHeight + 4f;
                var detailRect = new Rect(cardRect.x + 8f, detailY, cardRect.width - 16f,
                    CalcTextHeight(entry.Definition ?? "", cardRect.width - 16f));
                Widgets.Label(detailRect, $"<size=12>{entry.Definition ?? ""}</size>");
            }
        }

        // ================================================================
        // 编辑表单
        // ================================================================

        private void BeginEdit(KnowledgeEntry entry)
        {
            _editingTerm = entry.Term;
            _editTermBuffer = entry.Term;
            _editDefBuffer = entry.Definition ?? "";
            _pendingDeleteTerm = null;
            _expandedTerm = null;
        }

        private void DrawEditForm(Listing_Standard listing, KnowledgeEntry entry)
        {
            _editCardTracker.Draw(listing, null, inner =>
            {
                inner.Gap(GapTiny);
                // 词条名
                Widgets.Label(inner.GetRect(20f), "<size=12>词条名:</size>");
                inner.Gap(2f);
                _editTermBuffer = Widgets.TextField(inner.GetRect(26f), _editTermBuffer);
                inner.Gap(GapSmall);
                // 释义
                Widgets.Label(inner.GetRect(20f), "<size=12>释义:</size>");
                inner.Gap(2f);
                _editDefBuffer = Widgets.TextArea(inner.GetRect(60f), _editDefBuffer);
                inner.Gap(GapSmall);
                // 按钮行
                var btnResults = DrawButtonRow(inner,
                    new[] { "保存", "取消" },
                    new[] { BtnWidthMedium, BtnWidthMedium });
                if (btnResults[0]) SaveEdit(entry.Term);
                if (btnResults[1]) CancelEdit();
            });
        }

        private void SaveEdit(string originalTerm)
        {
            var svc = _activeKnowledgeBase;
            if (svc == null) return;

            var newTerm = _editTermBuffer.Trim();
            if (string.IsNullOrEmpty(newTerm))
            {
                SetStatus("[错误] 词条名不能为空");
                return;
            }

            // 若词条名变更，先删旧的
            if (!string.Equals(originalTerm, newTerm, StringComparison.OrdinalIgnoreCase))
                svc.Delete(originalTerm);

            svc.Store(new KnowledgeEntry
            {
                Term = newTerm,
                Definition = _editDefBuffer,
                Source = "LLM"
            });

            _editingTerm = null;
            RefreshCache();
            SetStatus($"已更新词条「{newTerm}」");
        }

        private void CancelEdit()
        {
            _editingTerm = null;
        }

        // ================================================================
        // 新增表单
        // ================================================================

        private void DrawNewEntryForm(Listing_Standard listing)
        {
            _formCardTracker.Draw(listing, "新增词条", inner =>
            {
                inner.Gap(GapTiny);
                // 词条名
                Widgets.Label(inner.GetRect(20f), "<size=12>词条名:</size>");
                inner.Gap(2f);
                _newTermBuffer = Widgets.TextField(inner.GetRect(26f), _newTermBuffer);
                inner.Gap(GapSmall);
                // 释义
                Widgets.Label(inner.GetRect(20f), "<size=12>释义:</size>");
                inner.Gap(2f);
                _newDefBuffer = Widgets.TextArea(inner.GetRect(60f), _newDefBuffer);
                inner.Gap(GapSmall);
                // 按钮行
                var btnResults = DrawButtonRow(inner,
                    new[] { "添加", "取消" },
                    new[] { BtnWidthMedium, BtnWidthMedium });
                if (btnResults[0]) AddNewEntry();
                if (btnResults[1]) CancelAdd();
            });
        }

        private void AddNewEntry()
        {
            var svc = _activeKnowledgeBase;
            if (svc == null) return;

            var term = _newTermBuffer.Trim();
            if (string.IsNullOrEmpty(term))
            {
                SetStatus("[错误] 词条名不能为空");
                return;
            }

            svc.Store(new KnowledgeEntry
            {
                Term = term,
                Definition = _newDefBuffer,
                Source = "LLM"
            });

            _addingNew = false;
            _newTermBuffer = "";
            _newDefBuffer = "";
            RefreshCache();
            SetStatus($"已添加词条「{term}」");
        }

        private void CancelAdd()
        {
            _addingNew = false;
            _newTermBuffer = "";
            _newDefBuffer = "";
        }

        // ================================================================
        // 删除
        // ================================================================

        private void ExecuteDelete(string term)
        {
            var svc = _activeKnowledgeBase;
            if (svc == null) return;

            svc.Delete(term);
            _pendingDeleteTerm = null;

            if (_expandedTerm == term) _expandedTerm = null;
            if (_editingTerm == term) _editingTerm = null;

            RefreshCache();
            SetStatus($"已删除词条「{term}」");
        }

        // ================================================================
        // 底栏
        // ================================================================

        private void DrawBottomBar(Listing_Standard listing)
        {
            var rowRect = listing.GetRect(BtnHeight + 2f);

            // 新增按钮
            var addRect = new Rect(rowRect.x, rowRect.y, BtnWidthMedium, BtnHeight);
            if (Widgets.ButtonText(addRect, "+ 新增词条"))
            {
                _addingNew = true;
                _editingTerm = null;
                _pendingDeleteTerm = null;
            }

            // 计数
            var countText = _searchText.Length > 0
                ? $"显示 {_filteredEntries.Count} / {_cachedEntries.Count} 条"
                : $"共 {_cachedEntries.Count} 条";
            var countRect = new Rect(rowRect.x + BtnWidthMedium + BtnGap, rowRect.y,
                rowRect.width - BtnWidthMedium - BtnGap, BtnHeight);
            Widgets.Label(countRect, $"<color=#888888><size=12>{countText}</size></color>");
        }

        // ================================================================
        // 模式感知与知识库切换
        // ================================================================

        private void EnsureActiveBaseCorrect()
        {
            bool currentlyInGame = Current.Game != null;

            // 模式未改变且活跃实例已就绪，无需处理
            if (currentlyInGame == _isInGame && _activeKnowledgeBase != null)
                return;

            _isInGame = currentlyInGame;

            if (_isInGame)
            {
                // 游戏内：绑定全局 KnowledgeService
                _activeKnowledgeBase = RimLifeCore.KnowledgeService;
                _selectedBaseId = SaveIdResolver.CurrentSaveId;
            }
            else if (_selectedBaseId != null)
            {
                // 主菜单：打开之前选中或默认的知识库
                OpenBrowseBase(_selectedBaseId);
            }
            else
            {
                _activeKnowledgeBase = null;
            }

            _cachedEntries.Clear();
            _filteredEntries.Clear();
        }

        private void OpenBrowseBase(string saveId)
        {
            try
            {
                string filePath = Path.Combine(
                    GenFilePaths.ConfigFolderPath, "RimLife", "Cache", $"{saveId}.json");
                var store = new FileBackedCacheStore(filePath);
                var builtIn = new BuiltInKnowledgeBase(store, RimLifeCore.Logger);
                var externals = new List<IExternalKnowledgeSource> { new GameDefKnowledgeBase() };
                _activeKnowledgeBase = new KnowledgeService(builtIn, externals);
                // 不包裹 MetricsKnowledgeService——主菜单没有 RuntimeMetrics 会话
                _selectedBaseId = saveId;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.UI] Failed to open knowledge base '{saveId}': {e.Message}");
                _activeKnowledgeBase = null;
            }
        }

        private void DrawContextHeader(Listing_Standard listing)
        {
            var rowRect = listing.GetRect(28f);

            if (_isInGame)
            {
                string shortId = _selectedBaseId != null
                    ? _selectedBaseId.Substring(0, Math.Min(8, _selectedBaseId.Length))
                    : "???";
                Widgets.Label(rowRect,
                    $"<color=#666666><size=12>知识库: <color=#88CC88>{shortId}...</color> (当前存档)</size></color>");
            }
            else
            {
                DrawKnowledgeBaseSelector(listing);
            }
        }

        private void DrawKnowledgeBaseSelector(Listing_Standard listing)
        {
            if (_availableBases.Count == 0)
                _availableBases = LocalFileStore.ListKnowledgeBases();

            var rowRect = listing.GetRect(28f);

            // 标签
            var labelRect = new Rect(rowRect.x, rowRect.y, 60f, rowRect.height);
            Widgets.Label(labelRect, "<size=12>知识库:</size>");

            // 下拉按钮
            var btnRect = new Rect(rowRect.x + 60f, rowRect.y, 240f, rowRect.height);
            string displayText = _selectedBaseId != null
                ? _availableBases.FirstOrDefault(b => b.SaveId == _selectedBaseId)?.DisplayName
                    ?? _selectedBaseId.Substring(0, Math.Min(8, _selectedBaseId.Length)) + "..."
                : "请选择知识库...";

            if (Widgets.ButtonText(btnRect, displayText))
            {
                var options = new List<FloatMenuOption>();
                foreach (var baseInfo in _availableBases)
                {
                    var info = baseInfo;
                    options.Add(new FloatMenuOption(info.DisplayName, () =>
                    {
                        _selectedBaseId = info.SaveId;
                        OpenBrowseBase(info.SaveId);
                        _cachedEntries.Clear();
                        _filteredEntries.Clear();
                    }));
                }
                if (options.Count == 0)
                    options.Add(new FloatMenuOption("（无可用知识库）", null));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // ================================================================
        // 缓存与过滤
        // ================================================================

        private void EnsureCacheLoaded()
        {
            var svc = _activeKnowledgeBase;
            if (svc == null) return;

            if (_cachedEntries.Count == 0)
                RefreshCache();
        }

        private void RefreshCache()
        {
            var svc = _activeKnowledgeBase;
            if (svc == null)
            {
                _cachedEntries.Clear();
                _filteredEntries.Clear();
                return;
            }

            _cachedEntries = svc.ListAll().ToList();

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = _searchText?.Trim();
            var hasQuery = !string.IsNullOrEmpty(query);

            if (!hasQuery)
            {
                _filteredEntries = new List<KnowledgeEntry>(_cachedEntries);
                return;
            }

            _filteredEntries = _cachedEntries.Where(e =>
            {
                var termMatch = e.Term != null &&
                    e.Term.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                var defMatch = e.Definition != null &&
                    e.Definition.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                return termMatch || defMatch;
            }).ToList();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private void SetStatus(string message)
        {
            _statusMessage = message;
            _statusMessageTime = Time.time;
        }
    }
}
