using System;
using System.Collections.Generic;
using System.Linq;
using NPCLife.Core;
using RimLife.Infrastructure;
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
        public string Group => "数据";
        public int Order => 0;

        // ================================================================
        // 数据缓存
        // ================================================================

        private List<KnowledgeEntry> _cachedEntries = new List<KnowledgeEntry>();
        private List<KnowledgeEntry> _filteredEntries = new List<KnowledgeEntry>();
        private List<string> _allTags = new List<string>();

        // ================================================================
        // 搜索与筛选
        // ================================================================

        private string _searchText = "";
        private string _activeTagFilter; // null = 不限

        // ================================================================
        // UI 状态
        // ================================================================

        private string _expandedTerm; // 展开详情的词条
        private string _editingTerm; // 正在编辑的词条（null = 非编辑模式）
        private string _editTermBuffer = "";
        private string _editDefBuffer = "";
        private string _editTagsBuffer = "";

        private string _pendingDeleteTerm; // 两步确认删除

        private bool _addingNew;
        private string _newTermBuffer = "";
        private string _newDefBuffer = "";
        private string _newTagsBuffer = "";

        private string _statusMessage;
        private float _statusMessageTime;

        // ================================================================
        // Draw 入口
        // ================================================================

        public void Draw(Rect rect, Listing_Standard listing)
        {
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

            // 搜索框（占大部分宽度）
            var searchWidth = rowRect.width - BtnWidthMedium - BtnGap;
            var searchRect = new Rect(rowRect.x, rowRect.y, searchWidth, BtnHeight);
            var newSearch = Widgets.TextField(searchRect, _searchText);
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                ApplyFilter();
            }

            // 标签筛选按钮
            var filterRect = new Rect(rowRect.x + searchWidth + BtnGap, rowRect.y, BtnWidthMedium, BtnHeight);
            var filterLabel = _activeTagFilter != null ? $"标签: {_activeTagFilter}" : "标签筛选";
            if (Widgets.ButtonText(filterRect, filterLabel))
            {
                ShowTagFilterMenu(filterRect);
            }
        }

        private void ShowTagFilterMenu(Rect anchor)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("全部标签", () =>
            {
                _activeTagFilter = null;
                ApplyFilter();
            }));
            foreach (var tag in _allTags)
            {
                var t = tag;
                options.Add(new FloatMenuOption(t, () =>
                {
                    _activeTagFilter = t;
                    ApplyFilter();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
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

            // 估算卡片高度
            var defPreview = Truncate(entry.Definition ?? "", 100);
            var defHeight = CalcTextHeight(defPreview, listing.ColumnWidth - 20f);
            var cardHeight = 30f + defHeight + 8f;
            if (isExpanded)
                cardHeight += CalcTextHeight(entry.Definition ?? "", listing.ColumnWidth - 20f) + 30f;

            // 卡片背景
            listing.Gap(GapTiny);
            var cardRect = listing.GetRect(cardHeight);
            var bgColor = isPendingDelete ? ColorDangerBg : ColorCardBg;
            Widgets.DrawBoxSolid(cardRect, bgColor);
            Widgets.DrawBox(cardRect, 1);

            // 标题行：词条名 + 标签 + 操作按钮
            var titleY = cardRect.y + 4f;
            var titleRect = new Rect(cardRect.x + 8f, titleY, cardRect.width * 0.5f, 22f);
            Widgets.Label(titleRect, $"<b>{entry.Term}</b>");

            // 标签
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
            {
                var tagText = string.Join(", ", entry.ContextTags);
                var tagRect = new Rect(titleRect.x, titleY + 20f, cardRect.width - 16f, 16f);
                Widgets.Label(tagRect, $"<color=#888888><size=11>{tagText}</size></color>");
            }

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
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                previewY += 16f;

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
            _editTagsBuffer = entry.ContextTags != null ? string.Join(", ", entry.ContextTags) : "";
            _pendingDeleteTerm = null;
            _expandedTerm = null;
        }

        private void DrawEditForm(Listing_Standard listing, KnowledgeEntry entry)
        {
            var formHeight = 30f + 22f + 60f + 22f + BtnHeight + 8f + BtnHeight + 8f;
            listing.Gap(GapTiny);
            var cardRect = listing.GetRect(formHeight);
            Widgets.DrawBoxSolid(cardRect, new Color(0.18f, 0.22f, 0.28f, 1f));
            Widgets.DrawBox(cardRect, 1);

            var innerX = cardRect.x + 8f;
            var innerW = cardRect.width - 16f;
            var curY = cardRect.y + 6f;

            // 词条名
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>词条名:</size>");
            curY += 20f;
            _editTermBuffer = Widgets.TextField(new Rect(innerX, curY, innerW, 26f), _editTermBuffer);
            curY += 30f;

            // 释义
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>释义:</size>");
            curY += 20f;
            _editDefBuffer = Widgets.TextArea(new Rect(innerX, curY, innerW, 60f), _editDefBuffer);
            curY += 64f;

            // 标签
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>标签 (逗号分隔):</size>");
            curY += 20f;
            _editTagsBuffer = Widgets.TextField(new Rect(innerX, curY, innerW, 26f), _editTagsBuffer);
            curY += 32f;

            // 按钮行
            var btnResults = DrawButtonRowInRect(
                new Rect(innerX, curY, innerW, BtnHeight),
                new[] { "保存", "取消" },
                new[] { BtnWidthMedium, BtnWidthMedium });

            if (btnResults[0])
                SaveEdit(entry.Term);
            if (btnResults[1])
                CancelEdit();
        }

        private void SaveEdit(string originalTerm)
        {
            var svc = RimLifeCore.KnowledgeService;
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
                Source = "LLM",
                ContextTags = ParseTags(_editTagsBuffer)
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
            var formHeight = 30f + 22f + 60f + 22f + BtnHeight + 8f + BtnHeight + 8f;
            listing.Gap(GapTiny);
            var cardRect = listing.GetRect(formHeight);
            Widgets.DrawBoxSolid(cardRect, new Color(0.18f, 0.28f, 0.22f, 1f));
            Widgets.DrawBox(cardRect, 1);

            var innerX = cardRect.x + 8f;
            var innerW = cardRect.width - 16f;
            var curY = cardRect.y + 6f;

            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=14><b>新增词条</b></size>");
            curY += 24f;

            // 词条名
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>词条名:</size>");
            curY += 20f;
            _newTermBuffer = Widgets.TextField(new Rect(innerX, curY, innerW, 26f), _newTermBuffer);
            curY += 30f;

            // 释义
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>释义:</size>");
            curY += 20f;
            _newDefBuffer = Widgets.TextArea(new Rect(innerX, curY, innerW, 60f), _newDefBuffer);
            curY += 64f;

            // 标签
            Widgets.Label(new Rect(innerX, curY, innerW, 20f), "<size=12>标签 (逗号分隔):</size>");
            curY += 20f;
            _newTagsBuffer = Widgets.TextField(new Rect(innerX, curY, innerW, 26f), _newTagsBuffer);
            curY += 32f;

            // 按钮行
            var btnResults = DrawButtonRowInRect(
                new Rect(innerX, curY, innerW, BtnHeight),
                new[] { "添加", "取消" },
                new[] { BtnWidthMedium, BtnWidthMedium });

            if (btnResults[0])
                AddNewEntry();
            if (btnResults[1])
                CancelAdd();
        }

        private void AddNewEntry()
        {
            var svc = RimLifeCore.KnowledgeService;
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
                Source = "LLM",
                ContextTags = ParseTags(_newTagsBuffer)
            });

            _addingNew = false;
            _newTermBuffer = "";
            _newDefBuffer = "";
            _newTagsBuffer = "";
            RefreshCache();
            SetStatus($"已添加词条「{term}」");
        }

        private void CancelAdd()
        {
            _addingNew = false;
            _newTermBuffer = "";
            _newDefBuffer = "";
            _newTagsBuffer = "";
        }

        // ================================================================
        // 删除
        // ================================================================

        private void ExecuteDelete(string term)
        {
            var svc = RimLifeCore.KnowledgeService;
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
            var countText = _searchText.Length > 0 || _activeTagFilter != null
                ? $"显示 {_filteredEntries.Count} / {_cachedEntries.Count} 条"
                : $"共 {_cachedEntries.Count} 条";
            var countRect = new Rect(rowRect.x + BtnWidthMedium + BtnGap, rowRect.y,
                rowRect.width - BtnWidthMedium - BtnGap, BtnHeight);
            Widgets.Label(countRect, $"<color=#888888><size=12>{countText}</size></color>");
        }

        // ================================================================
        // 缓存与过滤
        // ================================================================

        private void EnsureCacheLoaded()
        {
            var svc = RimLifeCore.KnowledgeService;
            if (svc == null) return;

            if (_cachedEntries.Count == 0)
                RefreshCache();
        }

        private void RefreshCache()
        {
            var svc = RimLifeCore.KnowledgeService;
            if (svc == null)
            {
                _cachedEntries.Clear();
                _filteredEntries.Clear();
                _allTags.Clear();
                return;
            }

            _cachedEntries = svc.ListAll().ToList();

            // 聚合所有标签
            var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _cachedEntries)
            {
                if (e.ContextTags != null)
                    foreach (var t in e.ContextTags)
                        tagSet.Add(t);
            }
            _allTags = tagSet.OrderBy(t => t).ToList();

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = _searchText?.Trim();
            var hasQuery = !string.IsNullOrEmpty(query);
            var hasTag = !string.IsNullOrEmpty(_activeTagFilter);

            if (!hasQuery && !hasTag)
            {
                _filteredEntries = new List<KnowledgeEntry>(_cachedEntries);
                return;
            }

            _filteredEntries = _cachedEntries.Where(e =>
            {
                if (hasTag)
                {
                    if (e.ContextTags == null || !e.ContextTags.Any(
                        t => string.Equals(t, _activeTagFilter, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }

                if (hasQuery)
                {
                    var termMatch = e.Term != null &&
                        e.Term.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    var defMatch = e.Definition != null &&
                        e.Definition.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!termMatch && !defMatch)
                        return false;
                }

                return true;
            }).ToList();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static List<string> ParseTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private void SetStatus(string message)
        {
            _statusMessage = message;
            _statusMessageTime = Time.time;
        }
    }
}
