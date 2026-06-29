using NPCLife.Framework;
using RimLife.UI;
using UnityEngine;
using Verse;

namespace RimLife.Settings
{
    /// <summary>
    /// RimLife 模组设置。
    /// 直接在 Mod Settings 窗口中显示完整的配置界面。
    /// 通过 ExposeData() 持久化 LLM 凭证等全局配置（不绑定存档）。
    /// 所有全局配置均通过此 ModSettings 持久化，切换存档不影响。
    /// </summary>
    public class RimLifeModSettings : ModSettings
    {
        public static RimLifeModSettings Instance { get; private set; }

        /// <summary>
        /// 凭证注册表完整状态的 JSON 序列化字符串。
        /// 由 CredentialRegistry (Infrastructure/Llm) 负责读写。
        /// </summary>
        public string LlmCredentialsJson;

        /// <summary>
        /// Agent 驱动配置的 JSON 序列化字符串。
        /// 由 RimLifeCore.Config 负责读写。
        /// </summary>
        public string DriverConfigJson;

        /// <summary>
        /// 提示词附加指令的 JSON 序列化字符串。
        /// 由 RimLifeCore.Config 负责读写。
        /// </summary>
        public string PromptAdditionsJson;

        /// <summary>
        /// 框架全局配置的 JSON 序列化字符串。
        /// 由 RimLifeCore.Config 负责读写。
        /// </summary>
        public string FrameworkConfigJson;

        public RimLifeModSettings()
        {
            Instance = this;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref LlmCredentialsJson, "llmCredentialsJson");
            Scribe_Values.Look(ref DriverConfigJson, "driverConfigJson");
            Scribe_Values.Look(ref PromptAdditionsJson, "promptAdditionsJson");
            Scribe_Values.Look(ref FrameworkConfigJson, "frameworkConfigJson");
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

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 主页没有 GameComponent，不会触发 RimWorldAgentDriver.GameComponentUpdate，
            // 因此 MainThreadDispatcher 队列无人消费。此处每帧手动 Drain，
            // 确保异步操作（如模型发现、凭证测试）的回调能在主页执行。
            MainThreadDispatcher.DrainQueue();
            _layout.Draw(inRect);
        }
    }
}
