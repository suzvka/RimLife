using System;
using System.Globalization;
using NPCLife.Driver;
using NPCLife.Framework;

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
        /// Agent 驱动配置。从 CacheStore 加载，未配置时返回默认值。
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
        /// 更新驱动配置并持久化。修改后需调用 RebuildAgents() 才能生效。
        /// </summary>
        public static void SetDriverConfig(DriverConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_driverConfigLock)
            {
                _driverConfig = config;
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
                    w.Prop("recentHistoryCapacity", config.RecentHistoryCapacity);
                    w.Prop("maxAgentRounds", config.MaxAgentRounds);
                    CacheStore?.Cache("rimlife_driver_config", w.Close());
                }
                catch (Exception e)
                {
                    Logger?.Warning($"[RimLife.Core] Failed to save DriverConfig: {e.Message}");
                }
            }
        }

        // ================================================================
        // 配置加载 / 保存
        // ================================================================

        private static FrameworkConfig LoadFrameworkConfig()
        {
            try
            {
                var json = CacheStore?.FetchCache<string>("rimlife_framework_config", null);
                if (!string.IsNullOrEmpty(json) && json.TrimStart().StartsWith("{"))
                {
                    var loaded = FrameworkConfig.FromJson(json);
                    Logger?.Message("[RimLife.Core] FrameworkConfig loaded from CacheStore.");
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
                var json = config.ToJson();
                CacheStore?.Cache("rimlife_framework_config", json);
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
                var json = CacheStore?.FetchCache<string>("rimlife_driver_config", null);
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
                    if (dict.TryGetValue("recentHistoryCapacity", out var rhc) && int.TryParse(rhc, out var rhcv))
                        dc.RecentHistoryCapacity = rhcv;
                    if (dict.TryGetValue("maxAgentRounds", out var mar) && int.TryParse(mar, out var marv))
                        dc.MaxAgentRounds = marv;
                    return dc;
                }
            }
            catch
            {
                // 加载失败，返回默认
            }
            return DriverConfig.CreateDefault();
        }

        private static PromptAdditions LoadPromptAdditions()
        {
            try
            {
                var json = CacheStore?.FetchCache<string>("rimlife_prompt_additions", null);
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
                CacheStore?.Cache("rimlife_prompt_additions", additions.ToJson());
            }
            catch (Exception e)
            {
                Logger?.Warning($"[RimLife.Core] Failed to save PromptAdditions: {e.Message}");
            }
        }
    }
}
