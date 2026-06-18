using RimLife.UI;
using UnityEngine;
using Verse;

namespace RimLife.Settings
{
    /// <summary>
    /// RimLife 模组设置。
    /// 直接在 Mod Settings 窗口中显示完整的配置界面。
    /// 通过 ExposeData() 持久化 LLM 凭证等全局配置（不绑定存档）。
    /// </summary>
    public class RimLifeModSettings : ModSettings
    {
        public static RimLifeModSettings Instance { get; private set; }

        /// <summary>
        /// 凭证注册表完整状态的 JSON 序列化字符串。
        /// 由 CredentialRegistry (Infrastructure/Llm) 负责读写。
        /// </summary>
        public string LlmCredentialsJson;

        public RimLifeModSettings()
        {
            Instance = this;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref LlmCredentialsJson, "llmCredentialsJson");
        }

        /// <summary>
        /// 立即触发 ExposeData 写入磁盘。
        /// </summary>
        public void SaveNow()
        {
            Write();
        }
    }

    /// <summary>
    /// RimLife Mod 主类。
    /// 委托 ConfigPanelLayout 绘制配置面板。
    /// </summary>
    public class RimLifeMod : Mod
    {
        private readonly ConfigPanelLayout _layout;

        public RimLifeMod(ModContentPack content) : base(content)
        {
            GetSettings<RimLifeModSettings>();
            _layout = new ConfigPanelLayout();
        }

        public override string SettingsCategory() => "RimLife";

        public override void DoSettingsWindowContents(Rect inRect) => _layout.Draw(inRect);
    }
}
