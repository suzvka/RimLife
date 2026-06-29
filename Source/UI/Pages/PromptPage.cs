using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 提示词页面：编辑 RimLife 在 NPCLife 基础身份之上追加的指令与 LLM 采样参数。
    /// 基础身份由 NPCLife 维护，对用户不可见也不可编辑；此处只管理附加部分。
    /// </summary>
    public class PromptPage : IConfigPage
    {
        public string Id => "prompt";
        public string Label => "提示词";
        public string Group => "核心";
        public int Order => 1;

        // 本地编辑缓冲（仅附加指令，不含 NPCLife 基座身份）
        private bool _initialized;
        private string _directorAdditions = "";
        private string[] _directorBuffers;
        private string _screenwriterAdditions = "";
        private string[] _screenwriterBuffers;
        private string _improviserAdditions = "";
        private string[] _improviserBuffers;
        private string _styleInstruction = "";
        private string[] _styleBuffers;
        private float _temperature = 0.7f;

        // 保存反馈
        private string _statusMessage;
        private float _statusMessageTime;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            InitializeIfNeeded();

            // ---- 顶部说明 ----
            Widgets.Label(listing.GetRect(36f),
                "<color=#aaaaaa><size=12>每个 Agent 的基础身份由 NPCLife 框架维护，不可编辑。下方文本框用于追加 RimWorld 特定指令、规则或约束。</size></color>");
            listing.Gap(GapTiny);

            // ---- LLM 采样参数 ----
            BeginSection(listing, "LLM 参数");

            var r1 = listing.GetRect(28f);
            Widgets.Label(new Rect(r1.x, r1.y, 100f, 24f), "Temperature:");
            _temperature = Widgets.HorizontalSlider(
                new Rect(r1.x + 105f, r1.y + 2f, 200f, 20f),
                _temperature, 0f, 2f, false, _temperature.ToString("F2"));
            Widgets.Label(new Rect(r1.x + 310f, r1.y, 200f, 24f),
                $"<color=#888888><size=11>越低越确定，越高越有创意</size></color>");

            listing.Gap(GapTiny);
            EndSection(listing);

            // ---- 全局风格指令 ----
            BeginSection(listing, "全局风格指令");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>运行时追加到所有 Agent 的提示词末尾。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _styleInstruction, ref _styleBuffers);
            EndSection(listing);

            // ---- 导演附加指令 ----
            BeginSection(listing, "导演 Agent (Director) — 附加指令");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>在框架基础身份之上追加指令。留空则不追加任何内容。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _directorAdditions, ref _directorBuffers);
            listing.Gap(GapTiny);
            var dirBtns = DrawButtonRow(listing,
                new[] { "清空附加" },
                new[] { BtnWidthMedium });
            if (dirBtns[0])
            {
                _directorAdditions = "";
                _directorBuffers = null;
                _statusMessage = "导演附加指令已清空（需保存生效）";
                _statusMessageTime = Time.time;
            }
            EndSection(listing);

            // ---- 编剧附加指令 ----
            BeginSection(listing, "编剧 Agent (Screenwriter) — 附加指令");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>在框架基础身份之上追加指令。动态上下文（工作空间 ID、关联角色等）在运行时自动追加。留空则不追加任何内容。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _screenwriterAdditions, ref _screenwriterBuffers);
            listing.Gap(GapTiny);
            var swBtns = DrawButtonRow(listing,
                new[] { "清空附加" },
                new[] { BtnWidthMedium });
            if (swBtns[0])
            {
                _screenwriterAdditions = "";
                _screenwriterBuffers = null;
                _statusMessage = "编剧附加指令已清空（需保存生效）";
                _statusMessageTime = Time.time;
            }
            EndSection(listing);

            // ---- 即兴编剧附加指令 ----
            BeginSection(listing, "即兴编剧 Agent (Improviser) — 附加指令");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>在框架基础身份之上追加指令。动态上下文（工作空间 ID 等）在运行时自动追加。留空则不追加任何内容。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _improviserAdditions, ref _improviserBuffers);
            listing.Gap(GapTiny);
            var flBtns = DrawButtonRow(listing,
                new[] { "清空附加" },
                new[] { BtnWidthMedium });
            if (flBtns[0])
            {
                _improviserAdditions = "";
                _improviserBuffers = null;
                _statusMessage = "即兴编剧附加指令已清空（需保存生效）";
                _statusMessageTime = Time.time;
            }
            EndSection(listing);

            // ---- 全局操作按钮 ----
            var btnResults = DrawButtonRow(listing,
                new[] { "保存并应用", "清空所有附加" },
                new[] { BtnWidthLarge, BtnWidthLarge });

            if (btnResults[0])
            {
                var pa = new PromptAdditions
                {
                    DirectorAdditions = _directorAdditions,
                    ScreenwriterAdditions = _screenwriterAdditions,
                    ImproviserAdditions = _improviserAdditions,
                    StyleInstruction = _styleInstruction,
                    Temperature = _temperature
                };
                RimLifeCore.SetPromptAdditions(pa);
                RimLifeCore.RebuildAgents();
                _statusMessage = "已保存并重建 Agent";
                _statusMessageTime = Time.time;
                Log.Message("[RimLife.UI] Prompt additions saved");
            }

            if (btnResults[1])
            {
                _directorAdditions = ""; _directorBuffers = null;
                _screenwriterAdditions = ""; _screenwriterBuffers = null;
                _improviserAdditions = ""; _improviserBuffers = null;
                _styleInstruction = ""; _styleBuffers = null;
                _temperature = 0.7f;
                _statusMessage = "所有附加已清空（需保存生效）";
                _statusMessageTime = Time.time;
            }

            // 状态消息（统一淡出效果）
            DrawStatusMessage(listing, _statusMessage, _statusMessageTime);
        }

        private void InitializeIfNeeded()
        {
            if (_initialized) return;
            _initialized = true;
            var pa = RimLifeCore.PromptAdditions;
            _directorAdditions = pa.DirectorAdditions ?? "";
            _screenwriterAdditions = pa.ScreenwriterAdditions ?? "";
            _improviserAdditions = pa.ImproviserAdditions ?? "";
            _styleInstruction = pa.StyleInstruction ?? "";
            _temperature = pa.Temperature;
        }

        // ================================================================
        // 多行文本输入辅助
        // ================================================================

        private static void DrawMultilineInput(
            Listing_Standard listing,
            ref string text,
            ref string[] buffers)
        {
            // 将数组转换为 List，避免在 for 循环中修改数组引用导致索引混乱
            var lines = new System.Collections.Generic.List<string>();
            if (buffers != null)
            {
                foreach (var b in buffers)
                    lines.Add(b ?? "");
            }
            else if (!string.IsNullOrEmpty(text))
            {
                lines.AddRange(text.Split('\n'));
            }

            // 按实际行数动态高度，0 行时仍留一行空间
            int displayRows = Mathf.Max(1, lines.Count);
            var totalHeight = displayRows * 28f + 4f;
            var areaRect = listing.GetRect(totalHeight);
            Widgets.DrawBoxSolid(areaRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            Widgets.DrawBox(areaRect, 1);

            // 按钮尺寸常量
            const float btnSize = 22f;
            const float btnGap = 2f;
            const float numWidth = 26f;
            const float rowHeight = 26f;
            const float controlsWidth = (btnSize + btnGap) * 3; // ↑ ↓ ×

            // 操作收集（每帧最多一个按钮被点击，无需担心冲突）
            int? deleteIndex = null;
            int? swapUp = null;
            int? swapDown = null;

            for (int i = 0; i < lines.Count; i++)
            {
                float rowY = areaRect.y + 2f + i * 28f;
                float rowX = areaRect.x + 4f;
                float usableWidth = areaRect.width - 8f;

                // 行号
                Widgets.Label(new Rect(rowX, rowY, numWidth, rowHeight),
                    $"<color=#666666><size=10>{i + 1}</size></color>");

                // 文本框
                float textFieldX = rowX + numWidth;
                float textFieldW = usableWidth - numWidth - controlsWidth - btnGap * 2;
                var content = lines[i];
                var newContent = Widgets.TextField(
                    new Rect(textFieldX, rowY, textFieldW, rowHeight), content);
                if (newContent != content)
                    lines[i] = newContent;

                // ↑ 上移
                float cx = textFieldX + textFieldW + btnGap;
                if (i > 0)
                {
                    if (Widgets.ButtonText(
                        new Rect(cx, rowY, btnSize, rowHeight), "↑"))
                    {
                        swapUp = i;
                    }
                }

                // ↓ 下移
                cx += btnSize + btnGap;
                if (i < lines.Count - 1)
                {
                    if (Widgets.ButtonText(
                        new Rect(cx, rowY, btnSize, rowHeight), "↓"))
                    {
                        swapDown = i;
                    }
                }

                // × 删除
                cx += btnSize + btnGap;
                if (Widgets.ButtonText(
                    new Rect(cx, rowY, btnSize, rowHeight), "×"))
                {
                    deleteIndex = i;
                }
            }

            // 渲染后应用操作（每帧最多一个）
            if (deleteIndex.HasValue)
            {
                lines.RemoveAt(deleteIndex.Value);
            }
            else if (swapUp.HasValue)
            {
                int idx = swapUp.Value;
                var tmp = lines[idx];
                lines[idx] = lines[idx - 1];
                lines[idx - 1] = tmp;
            }
            else if (swapDown.HasValue)
            {
                int idx = swapDown.Value;
                var tmp = lines[idx];
                lines[idx] = lines[idx + 1];
                lines[idx + 1] = tmp;
            }

            // 添加行按钮
            var btnRect = listing.GetRect(22f);
            if (Widgets.ButtonText(
                new Rect(btnRect.x, btnRect.y, BtnWidthSmall, BtnHeight), "+ 添加行"))
            {
                lines.Add("");
            }

            // 合并回 text（去掉尾部空行）
            while (lines.Count > 0 && string.IsNullOrEmpty(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);
            buffers = lines.ToArray();
            text = lines.Count > 0 ? string.Join("\n", lines) : "";
        }
    }
}
