using System;
using System.Globalization;
using NPCLife.Driver;
using NPCLife.Framework;
using RimLife.Settings;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLifeCore 的配置管理部分。
    /// 包含 DriverConfig 和 PromptAdditions 的持久化加载与保存。
    /// </summary>
    public static partial class RimLifeCore
    {
        private static DriverConfig _driverConfig;
        private static readonly object _driverConfigLock = new object();

        /// <summary>
        /// Agent 驱动配置。从 ModSettings 加载，未配置时返回默认值。
        /// </summary>
        internal static DriverConfig DriverConfig
        {
            get
            {
                lock (_driverConfigLock)
                {
                    if (_driverConfig == null)
                        _driverConfig = LoadDriverConfig();
                    return _driverConfig;
                }
            }
        }

        /// <summary>
        /// 更新驱动配置并持久化到 ModSettings。修改后需调用 RebuildAgents() 才能生效。
        /// </summary>
        public static void SetDriverConfig(DriverConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_driverConfigLock)
            {
                _driverConfig = config;
                SaveDriverConfig(config);
            }
        }

        private static void SaveDriverConfig(DriverConfig config)
        {
            try
            {
                var w = new NPCLife.Framework.JsonWriter(256);
                w.Prop("directorCountThreshold", config.DirectorCountThreshold);
                w.Prop("directorImportanceThreshold", config.DirectorImportanceThreshold, "F2");
                w.Prop("freelancerCountThreshold", config.ImproviserCountThreshold);
                w.Prop("freelancerImportanceThreshold", config.ImproviserImportanceThreshold, "F2");
                w.Prop("screenwriterCountThreshold", config.ScreenwriterCountThreshold);
                w.Prop("screenwriterImportanceThreshold", config.ScreenwriterImportanceThreshold, "F2");
                w.Prop("directorTimerInterval", config.DirectorTimerInterval);
                w.Prop("freelancerTimerInterval", config.ImproviserTimerInterval);
                w.Prop("screenwriterTimerInterval", config.ScreenwriterTimerInterval);
                w.Prop("maxAgentRounds", config.MaxAgentRounds);
                var settings = RimLifeModSettings.Instance;
                if (settings != null)
                {
                    settings.DriverConfigJson = w.Close();
                    settings.SaveNow();
                }
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] Failed to save DriverConfig: {e.Message}");
            }
        }

        /// <summary>
        /// 将新 DriverConfig 的值写入现有 DriverConfig 实例（如果存在），
        /// 而非替换引用。此举确保 WorkspaceManager → WorkspaceEventPool
        /// 持有的 DriverConfig 引用能同步感知配置变更，避免 EventPool
        /// 使用过时阈值而导致 TimerPulse 无法触发 Agent 激活。
        /// </summary>
        private static void ApplyDriverConfigInPlace(DriverConfig newConfig)
        {
            if (newConfig == null) return;

            // 确保现有实例已加载（DriverConfig 属性会延迟加载）
            var current = DriverConfig;
            if (current == newConfig) return; // 同一个实例无需复制

            current.DirectorCountThreshold = newConfig.DirectorCountThreshold;
            current.DirectorImportanceThreshold = newConfig.DirectorImportanceThreshold;
            current.ImproviserCountThreshold = newConfig.ImproviserCountThreshold;
            current.ImproviserImportanceThreshold = newConfig.ImproviserImportanceThreshold;
            current.ScreenwriterCountThreshold = newConfig.ScreenwriterCountThreshold;
            current.ScreenwriterImportanceThreshold = newConfig.ScreenwriterImportanceThreshold;
            current.DirectorTimerInterval = newConfig.DirectorTimerInterval;
            current.ImproviserTimerInterval = newConfig.ImproviserTimerInterval;
            current.ScreenwriterTimerInterval = newConfig.ScreenwriterTimerInterval;
            current.MaxAgentRounds = newConfig.MaxAgentRounds;
        }

        // ================================================================
        // 配置加载 / 保存
        // ================================================================

        private static FrameworkConfig LoadFrameworkConfig()
        {
            try
            {
                var settings = RimLifeModSettings.Instance;
                var json = settings?.FrameworkConfigJson;
                if (!string.IsNullOrEmpty(json) && json.TrimStart().StartsWith("{"))
                {
                    var loaded = FrameworkConfig.FromJson(json);
                    Logger?.Message("[RimLife.Core] FrameworkConfig loaded from ModSettings.");
                    return loaded;
                }
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] Failed to load FrameworkConfig: {e.Message}");
            }
            return FrameworkConfig.CreateDefault();
        }

        private static void SaveFrameworkConfig(FrameworkConfig config)
        {
            try
            {
                var settings = RimLifeModSettings.Instance;
                if (settings != null)
                {
                    settings.FrameworkConfigJson = config.ToJson();
                    settings.SaveNow();
                }
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] Failed to save FrameworkConfig: {e.Message}");
            }
        }

        private static DriverConfig LoadDriverConfig()
        {
            try
            {
                var settings = RimLifeModSettings.Instance;
                var json = settings?.DriverConfigJson;
                if (!string.IsNullOrEmpty(json) && json.StartsWith("{"))
                {
                    var dict = NPCLife.Framework.JsonParser.ParseDict(json);
                    var dc = DriverConfig.CreateDefault();
                    if (dict.TryGetValue("directorCountThreshold", out var dct) && int.TryParse(dct, out var dctv))
                        dc.DirectorCountThreshold = dctv;
                    if (dict.TryGetValue("directorImportanceThreshold", out var dit) && float.TryParse(dit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ditv))
                        dc.DirectorImportanceThreshold = ditv;
                    if (dict.TryGetValue("freelancerCountThreshold", out var fct) && int.TryParse(fct, out var fctv))
                        dc.ImproviserCountThreshold = fctv;
                    if (dict.TryGetValue("freelancerImportanceThreshold", out var fit) && float.TryParse(fit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fitv))
                        dc.ImproviserImportanceThreshold = fitv;
                    if (dict.TryGetValue("screenwriterCountThreshold", out var sct) && int.TryParse(sct, out var sctv))
                        dc.ScreenwriterCountThreshold = sctv;
                    if (dict.TryGetValue("screenwriterImportanceThreshold", out var sit) && float.TryParse(sit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sitv))
                        dc.ScreenwriterImportanceThreshold = sitv;
                    if (dict.TryGetValue("directorTimerInterval", out var dti) && int.TryParse(dti, out var dtiv))
                        dc.DirectorTimerInterval = dtiv;
                    if (dict.TryGetValue("freelancerTimerInterval", out var fti) && int.TryParse(fti, out var ftiv))
                        dc.ImproviserTimerInterval = ftiv;
                    if (dict.TryGetValue("screenwriterTimerInterval", out var swti) && int.TryParse(swti, out var swtiv))
                        dc.ScreenwriterTimerInterval = swtiv;
                    if (dict.TryGetValue("maxAgentRounds", out var mar) && int.TryParse(mar, out var marv))
                        dc.MaxAgentRounds = marv;
                    Logger?.Message("[RimLife.Core] DriverConfig loaded from ModSettings.");
                    return dc;
                }
            }
            catch
            {
                // 加载失败，返回优化默认值
            }
            return CreateOptimizedDriverConfig();
        }

        /// <summary>
        /// 创建针对首次使用优化的默认驱动配置。
        /// 相比 DriverConfig.CreateDefault()，降低了事件积累阈值并启用了定时器脉冲，
        /// 避免导演在事件稀疏时长时间空转等待。
        /// 用户可通过 UI（RunStrategyPage）随时调整。
        /// </summary>
        private static DriverConfig CreateOptimizedDriverConfig()
        {
            var dc = DriverConfig.CreateDefault();
            dc.DirectorCountThreshold = 3;        // 5→3：3 个事件即可触发导演
            dc.DirectorImportanceThreshold = 8f;   // 15→8：一个 ThreatBig(5)+两个普通事件即可触发
            dc.DirectorTimerInterval = 0;           // 90→0：导演不再定时唤醒，纯事件驱动
            dc.ScreenwriterCountThreshold = 2;      // 5→2：导演路由2个事件即可触发编剧
            dc.ScreenwriterImportanceThreshold = 6f; // 15→6：降低编剧激活门槛
            dc.ScreenwriterTimerInterval = 120;     // 兜底定时器：pending 事件未达阈值时，120秒后强制触发编剧
            dc.ImproviserTimerInterval = 180;       // 即兴编剧强制唤醒：180秒主动巡逻
            return dc;
        }

        private static PromptAdditions LoadPromptAdditions()
        {
            try
            {
                var settings = RimLifeModSettings.Instance;
                var json = settings?.PromptAdditionsJson;
                if (!string.IsNullOrEmpty(json))
                    return PromptAdditions.FromJson(json);
            }
            catch
            {
                // 加载失败，返回默认
            }
            return PromptAdditions.CreateDefault();
        }

        private static void SavePromptAdditions(PromptAdditions additions)
        {
            try
            {
                var settings = RimLifeModSettings.Instance;
                if (settings != null)
                {
                    settings.PromptAdditionsJson = additions.ToJson();
                    settings.SaveNow();
                }
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] Failed to save PromptAdditions: {e.Message}");
            }
        }
    }
}
