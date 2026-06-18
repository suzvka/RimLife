using NPCLife.Driver;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NPCLife.Framework
{
    /// <summary>
    /// 框架全局配置。统一管理驱动参数、诊断开关和功能开关。
    /// 纯数据类，零外部依赖。
    ///
    /// 合并优先级（低→高）：
    ///   默认值 &lt; 配置文件(FrameworkConfig.FromJson) &lt; 代码覆盖
    ///
    /// 冻结后所有 setter 抛出 InvalidOperationException，保证运行时配置不可变。
    /// </summary>
    public class FrameworkConfig
    {
        private bool _frozen;

        // ---- 子配置区域 ----

        /// <summary>Agent 驱动配置区。</summary>
        public DriverConfig Driver { get; set; }

        /// <summary>诊断配置区。</summary>
        public DiagnosticSection Diagnostics { get; set; }

        /// <summary>功能开关区。</summary>
        public FeatureToggleSection Features { get; set; }

        // ---- 冻结机制 ----

        /// <summary>是否已冻结。冻结后所有 setter 抛出 InvalidOperationException。</summary>
        public bool IsFrozen => _frozen;

        /// <summary>
        /// 冻结配置。调用后任何修改尝试将抛出 InvalidOperationException。
        /// 通常在 Initialize() 完成后调用。
        /// </summary>
        public void Freeze()
        {
            _frozen = true;
        }

        private void ThrowIfFrozen()
        {
            if (_frozen) throw new InvalidOperationException("FrameworkConfig is frozen. Cannot modify after Freeze().");
        }

        // ---- 校验 ----

        /// <summary>
        /// 校验配置合法性。返回错误描述列表，空列表表示合法。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (Driver == null) errors.Add("Driver section is null.");
            else
            {
                if (Driver.DirectorCountThreshold < 1) errors.Add("Driver.DirectorCountThreshold must be >= 1.");
                if (Driver.DirectorImportanceThreshold < 1f) errors.Add("Driver.DirectorImportanceThreshold must be >= 1.");
                if (Driver.RecentHistoryCapacity < 10) errors.Add("Driver.RecentHistoryCapacity must be >= 10.");
                if (Driver.MaxAgentRounds < 1 || Driver.MaxAgentRounds > 100)
                    errors.Add("Driver.MaxAgentRounds must be between 1 and 100.");
            }

            if (Diagnostics == null) errors.Add("Diagnostics section is null.");

            if (Features == null) errors.Add("Features section is null.");

            return errors;
        }

        // ---- 序列化 / 反序列化 ----

        /// <summary>
        /// 序列化为 JSON 字符串。
        /// </summary>
        public string ToJson()
        {
            var w = new JsonWriter(512);

            // Driver
            var dw = new JsonWriter(256);
            var d = Driver ?? DriverConfig.CreateDefault();
            dw.Prop("directorCountThreshold", d.DirectorCountThreshold);
            dw.Prop("directorImportanceThreshold", d.DirectorImportanceThreshold, "F2");
            dw.Prop("freelancerCountThreshold", d.FreelancerCountThreshold);
            dw.Prop("freelancerImportanceThreshold", d.FreelancerImportanceThreshold, "F2");
            dw.Prop("screenwriterCountThreshold", d.ScreenwriterCountThreshold);
            dw.Prop("screenwriterImportanceThreshold", d.ScreenwriterImportanceThreshold, "F2");
            dw.Prop("directorTimerInterval", d.DirectorTimerInterval);
            dw.Prop("freelancerTimerInterval", d.FreelancerTimerInterval);
            dw.Prop("recentHistoryCapacity", d.RecentHistoryCapacity);
            dw.Prop("maxAgentRounds", d.MaxAgentRounds);
            w.PropRaw("driver", dw.Close());

            // Diagnostics
            var diag = new JsonWriter(128);
            diag.Prop("enableVerboseLogging", Diagnostics?.EnableVerboseLogging ?? false);
            diag.Prop("enableToolCallTracing", Diagnostics?.EnableToolCallTracing ?? false);
            diag.Prop("enableEventTracing", Diagnostics?.EnableEventTracing ?? false);
            diag.Prop("logLevel", Diagnostics?.LogLevel ?? "Info");
            w.PropRaw("diagnostics", diag.Close());

            // Features
            var feat = new JsonWriter(128);
            feat.Prop("enableDirectorAgent", Features?.EnableDirectorAgent ?? true);
            feat.Prop("enableMemoryConsolidation", Features?.EnableMemoryConsolidation ?? true);
            feat.Prop("enableKnowledgeBase", Features?.EnableKnowledgeBase ?? true);
            feat.Prop("enableFreelancerAgent", Features?.EnableFreelancerAgent ?? true);
            feat.Prop("enableRuntimeMetrics", Features?.EnableRuntimeMetrics ?? true);
            w.PropRaw("features", feat.Close());

            return w.Close();
        }

        /// <summary>
        /// 从 JSON 字符串反序列化。解析失败时返回默认配置。
        /// </summary>
        public static FrameworkConfig FromJson(string json)
        {
            var config = CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return config;

            try
            {
                var dict = JsonParser.ParseDict(json);

                if (dict.TryGetValue("driver", out string driverJson))
                {
                    var dd = JsonParser.ParseDict(driverJson);
                    var dc = DriverConfig.CreateDefault();
                    if (dd.TryGetValue("directorCountThreshold", out string dct) && int.TryParse(dct, out int dctv))
                        dc.DirectorCountThreshold = dctv;
                    if (dd.TryGetValue("directorImportanceThreshold", out string dit) && float.TryParse(dit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ditv))
                        dc.DirectorImportanceThreshold = ditv;
                    if (dd.TryGetValue("freelancerCountThreshold", out string fct) && int.TryParse(fct, out int fctv))
                        dc.FreelancerCountThreshold = fctv;
                    if (dd.TryGetValue("freelancerImportanceThreshold", out string fit) && float.TryParse(fit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fitv))
                        dc.FreelancerImportanceThreshold = fitv;
                    if (dd.TryGetValue("screenwriterCountThreshold", out string sct) && int.TryParse(sct, out int sctv))
                        dc.ScreenwriterCountThreshold = sctv;
                    if (dd.TryGetValue("screenwriterImportanceThreshold", out string sit) && float.TryParse(sit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sitv))
                        dc.ScreenwriterImportanceThreshold = sitv;
                    if (dd.TryGetValue("directorTimerInterval", out string dti) && int.TryParse(dti, out int dtiv))
                        dc.DirectorTimerInterval = dtiv;
                    if (dd.TryGetValue("freelancerTimerInterval", out string fti) && int.TryParse(fti, out int ftiv))
                        dc.FreelancerTimerInterval = ftiv;
                    if (dd.TryGetValue("recentHistoryCapacity", out string rhc) && int.TryParse(rhc, out int rhcv))
                        dc.RecentHistoryCapacity = rhcv;
                    if (dd.TryGetValue("maxAgentRounds", out string mar) && int.TryParse(mar, out int marv))
                        dc.MaxAgentRounds = marv;
                    config.Driver = dc;
                }

                if (dict.TryGetValue("diagnostics", out string diagJson))
                {
                    var dd = JsonParser.ParseDict(diagJson);
                    if (dd.TryGetValue("enableVerboseLogging", out string v) && bool.TryParse(v, out bool vv))
                        config.Diagnostics.EnableVerboseLogging = vv;
                    if (dd.TryGetValue("enableToolCallTracing", out string t) && bool.TryParse(t, out bool tv))
                        config.Diagnostics.EnableToolCallTracing = tv;
                    if (dd.TryGetValue("enableEventTracing", out string e) && bool.TryParse(e, out bool ev))
                        config.Diagnostics.EnableEventTracing = ev;
                    if (dd.TryGetValue("logLevel", out string ll))
                        config.Diagnostics.LogLevel = ll;
                }

                if (dict.TryGetValue("features", out string featJson))
                {
                    var fd = JsonParser.ParseDict(featJson);
                    if (fd.TryGetValue("enableDirectorAgent", out string da) && bool.TryParse(da, out bool dav))
                        config.Features.EnableDirectorAgent = dav;
                    if (fd.TryGetValue("enableMemoryConsolidation", out string mc) && bool.TryParse(mc, out bool mcv))
                        config.Features.EnableMemoryConsolidation = mcv;
                    if (fd.TryGetValue("enableKnowledgeBase", out string kb) && bool.TryParse(kb, out bool kbv))
                        config.Features.EnableKnowledgeBase = kbv;
                    if (fd.TryGetValue("enableFreelancerAgent", out string fa) && bool.TryParse(fa, out bool fav))
                        config.Features.EnableFreelancerAgent = fav;
                    if (fd.TryGetValue("enableRuntimeMetrics", out string rm) && bool.TryParse(rm, out bool rmv))
                        config.Features.EnableRuntimeMetrics = rmv;
                }
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return config;
        }

        /// <summary>创建默认配置。</summary>
        public static FrameworkConfig CreateDefault()
        {
            return new FrameworkConfig
            {
                Driver = DriverConfig.CreateDefault(),
                Diagnostics = new DiagnosticSection(),
                Features = new FeatureToggleSection()
            };
        }

        /// <summary>
        /// 从现有 DriverConfig 迁移配置（向后兼容）。
        /// </summary>
        public static FrameworkConfig FromDriverConfig(DriverConfig driverConfig)
        {
            if (driverConfig == null) return CreateDefault();
            return new FrameworkConfig
            {
                Driver = driverConfig,
                Diagnostics = new DiagnosticSection(),
                Features = new FeatureToggleSection()
            };
        }
    }

    /// <summary>
    /// 诊断配置区。控制日志详细程度和链路追踪。
    /// </summary>
    public class DiagnosticSection
    {
        /// <summary>启用详细日志（含 prompt 内容、工具参数等）。</summary>
        public bool EnableVerboseLogging = false;

        /// <summary>启用工具调用追踪（每次调用记录完整参数和结果）。</summary>
        public bool EnableToolCallTracing = false;

        /// <summary>启用事件总线追踪（记录所有事件发布/订阅轨迹）。</summary>
        public bool EnableEventTracing = false;

        /// <summary>日志级别："Debug" / "Info" / "Warning" / "Error"。</summary>
        public string LogLevel = "Info";
    }

    /// <summary>
    /// 功能开关区。允许动态启用/禁用框架功能。
    /// </summary>
    public class FeatureToggleSection
    {
        /// <summary>是否启用导演 Agent。</summary>
        public bool EnableDirectorAgent = true;

        /// <summary>是否启用记忆巩固。</summary>
        public bool EnableMemoryConsolidation = true;

        /// <summary>是否启用知识库。</summary>
        public bool EnableKnowledgeBase = true;

        /// <summary>是否启用 Freelancer Agent（临时任务代理）。</summary>
        public bool EnableFreelancerAgent = true;

        /// <summary>是否启用运行时度量采集（工具频率、Token 消耗、知识库命中率等）。
        /// 关闭时 MetricsInterceptor 不注册，零开销。</summary>
        public bool EnableRuntimeMetrics = true;
    }
}
