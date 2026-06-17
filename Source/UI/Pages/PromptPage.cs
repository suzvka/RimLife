using RimLife.Driver;
using RimLife.Infrastructure;
using UnityEngine;
using Verse;
using static RimLife.UI.UIHelper;

namespace RimLife.UI
{
    /// <summary>
    /// 提示词页面：各角色完整提示词全文编辑 + LLM 采样参数。
    /// 缓存即真相，恢复 = 将缓存覆盖为默认值。
    /// </summary>
    public class PromptPage : IConfigPage
    {
        public string Id => "prompt";
        public string Label => "提示词";
        public string Group => "核心";
        public int Order => 2;

        // 本地编辑缓冲
        private bool _initialized;
        private string _directorPrompt = "";
        private string[] _directorBuffers;
        private string _screenwriterPrompt = "";
        private string[] _screenwriterBuffers;
        private string _freelancerPrompt = "";
        private string[] _freelancerBuffers;
        private string _styleInstruction = "";
        private string[] _styleBuffers;
        private float _temperature = 0.7f;

        // 保存反馈
        private string _statusMessage;

        public void Draw(Rect rect, Listing_Standard listing)
        {
            InitializeIfNeeded();

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

            // ---- 导演提示词 ----
            BeginSection(listing, "导演 Agent (Director)");
            DrawMultilineInput(listing, ref _directorPrompt, ref _directorBuffers);
            listing.Gap(GapTiny);
            var dirBtns = DrawButtonRow(listing,
                new[] { "恢复默认" },
                new[] { BtnWidthMedium });
            if (dirBtns[0])
            {
                _directorPrompt = PromptConfig.DefaultDirectorPrompt;
                _directorBuffers = null;
                _statusMessage = "导演提示词已恢复默认（需保存生效）";
            }
            EndSection(listing);

            // ---- 编剧提示词 ----
            BeginSection(listing, "编剧 Agent (Screenwriter)");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>动态上下文（工作空间 ID、关联角色等）在运行时自动追加。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _screenwriterPrompt, ref _screenwriterBuffers);
            listing.Gap(GapTiny);
            var swBtns = DrawButtonRow(listing,
                new[] { "恢复默认" },
                new[] { BtnWidthMedium });
            if (swBtns[0])
            {
                _screenwriterPrompt = PromptConfig.DefaultScreenwriterPrompt;
                _screenwriterBuffers = null;
                _statusMessage = "编剧提示词已恢复默认（需保存生效）";
            }
            EndSection(listing);

            // ---- Freelancer 提示词 ----
            BeginSection(listing, "临时工 Agent (Freelancer)");
            Widgets.Label(listing.GetRect(22f),
                "<color=#888888><size=12>动态上下文（工作空间 ID 等）在运行时自动追加。</size></color>");
            listing.Gap(GapTiny);
            DrawMultilineInput(listing, ref _freelancerPrompt, ref _freelancerBuffers);
            listing.Gap(GapTiny);
            var flBtns = DrawButtonRow(listing,
                new[] { "恢复默认" },
                new[] { BtnWidthMedium });
            if (flBtns[0])
            {
                _freelancerPrompt = PromptConfig.DefaultFreelancerPrompt;
                _freelancerBuffers = null;
                _statusMessage = "临时工提示词已恢复默认（需保存生效）";
            }
            EndSection(listing);

            // ---- 全局操作按钮 ----
            var btnResults = DrawButtonRow(listing,
                new[] { "保存并应用", "全部恢复默认" },
                new[] { BtnWidthLarge, BtnWidthLarge });

            if (btnResults[0])
            {
                var pc = new PromptConfig
                {
                    DirectorPrompt = _directorPrompt,
                    ScreenwriterPrompt = _screenwriterPrompt,
                    FreelancerPrompt = _freelancerPrompt,
                    StyleInstruction = _styleInstruction,
                    Temperature = _temperature
                };
                RimLifeCore.SetPromptConfig(pc);
                RimLifeCore.RebuildAgents();
                _statusMessage = "已保存并重建 Agent";
                Log.Message("[RimLife.UI] Prompt settings saved");
            }

            if (btnResults[1])
            {
                var def = PromptConfig.CreateDefault();
                _directorPrompt = def.DirectorPrompt; _directorBuffers = null;
                _screenwriterPrompt = def.ScreenwriterPrompt; _screenwriterBuffers = null;
                _freelancerPrompt = def.FreelancerPrompt; _freelancerBuffers = null;
                _styleInstruction = ""; _styleBuffers = null;
                _temperature = def.Temperature;
                _statusMessage = "全部已恢复默认（需保存生效）";
            }

            // 状态消息
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                listing.Gap(GapTiny);
                Widgets.Label(listing.GetRect(22f),
                    $"<color=#88FF88><size=12>{_statusMessage}</size></color>");
            }
        }

        private void InitializeIfNeeded()
        {
            if (_initialized) return;
            _initialized = true;
            var pc = RimLifeCore.PromptConfig;
            _directorPrompt = pc.DirectorPrompt ?? PromptConfig.DefaultDirectorPrompt;
            _screenwriterPrompt = pc.ScreenwriterPrompt ?? PromptConfig.DefaultScreenwriterPrompt;
            _freelancerPrompt = pc.FreelancerPrompt ?? PromptConfig.DefaultFreelancerPrompt;
            _styleInstruction = pc.StyleInstruction ?? "";
            _temperature = pc.Temperature;
        }

        // ================================================================
        // 多行文本输入辅助
        // ================================================================

        private static void DrawMultilineInput(
            Listing_Standard listing,
            ref string text,
            ref string[] buffers)
        {
            if (buffers == null)
            {
                if (string.IsNullOrEmpty(text))
                {
                    buffers = new string[0];
                }
                else
                {
                    var lines = text.Split('\n');
                    buffers = new string[lines.Length];
                    for (int i = 0; i < lines.Length; i++)
                        buffers[i] = lines[i];
                }
            }

            // 按实际行数动态高度，0 行时仍留一行空间
            int rowCount = buffers.Length;
            int displayRows = Mathf.Max(1, rowCount);
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

            for (int i = 0; i < buffers.Length; i++)
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
                var content = buffers[i] ?? "";
                var newContent = Widgets.TextField(
                    new Rect(textFieldX, rowY, textFieldW, rowHeight), content);
                if (newContent != content)
                    buffers[i] = newContent;

                // ↑ 上移
                float cx = textFieldX + textFieldW + btnGap;
                if (i > 0)
                {
                    if (Widgets.ButtonText(
                        new Rect(cx, rowY, btnSize, rowHeight), "↑"))
                    {
                        var tmp = buffers[i];
                        buffers[i] = buffers[i - 1];
                        buffers[i - 1] = tmp;
                    }
                }

                // ↓ 下移
                cx += btnSize + btnGap;
                if (i < buffers.Length - 1)
                {
                    if (Widgets.ButtonText(
                        new Rect(cx, rowY, btnSize, rowHeight), "↓"))
                    {
                        var tmp = buffers[i];
                        buffers[i] = buffers[i + 1];
                        buffers[i + 1] = tmp;
                    }
                }

                // × 删除
                cx += btnSize + btnGap;
                if (Widgets.ButtonText(
                    new Rect(cx, rowY, btnSize, rowHeight), "×"))
                {
                    var nb = new string[buffers.Length - 1];
                    for (int j = 0, k = 0; j < buffers.Length; j++)
                    {
                        if (j != i) nb[k++] = buffers[j];
                    }
                    buffers = nb;
                }
            }

            // 添加行按钮
            var btnRect = listing.GetRect(22f);
            if (Widgets.ButtonText(
                new Rect(btnRect.x, btnRect.y, BtnWidthSmall, BtnHeight), "+ 添加行"))
            {
                var nb = new string[buffers.Length + 1];
                System.Array.Copy(buffers, nb, buffers.Length);
                nb[buffers.Length] = "";
                buffers = nb;
            }

            // 合并回 text
            int nonEmpty = 0;
            for (int i = buffers.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(buffers[i])) { nonEmpty = i + 1; break; }
            }
            var parts = new string[nonEmpty];
            for (int i = 0; i < nonEmpty; i++)
                parts[i] = buffers[i] ?? "";
            text = nonEmpty > 0 ? string.Join("\n", parts) : "";
        }
    }
}
