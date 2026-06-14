using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimLife.Framework
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
        public DriverSection Driver { get; set; }

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
                if (Driver.CountThreshold < 1) errors.Add("Driver.CountThreshold must be >= 1.");
                if (Driver.ImportanceThreshold < 1) errors.Add("Driver.ImportanceThreshold must be >= 1.");
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
            var dw = new JsonWriter(128);
            dw.Prop("countThreshold", Driver?.CountThreshold ?? 5);
            dw.Prop("importanceThreshold", Driver?.ImportanceThreshold ?? 15);
            dw.Prop("recentHistoryCapacity", Driver?.RecentHistoryCapacity ?? 200);
            dw.Prop("maxAgentRounds", Driver?.MaxAgentRounds ?? 10);
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
                    if (dd.TryGetValue("countThreshold", out string ct) && int.TryParse(ct, out int ctv))
                        config.Driver.CountThreshold = ctv;
                    if (dd.TryGetValue("importanceThreshold", out string it) && int.TryParse(it, out int itv))
                        config.Driver.ImportanceThreshold = itv;
                    if (dd.TryGetValue("recentHistoryCapacity", out string rhc) && int.TryParse(rhc, out int rhcv))
                        config.Driver.RecentHistoryCapacity = rhcv;
                    if (dd.TryGetValue("maxAgentRounds", out string mar) && int.TryParse(mar, out int marv))
                        config.Driver.MaxAgentRounds = marv;
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
                Driver = new DriverSection(),
                Diagnostics = new DiagnosticSection(),
                Features = new FeatureToggleSection()
            };
        }

        /// <summary>
        /// 从现有 DriverConfig 迁移配置（向后兼容）。
        /// </summary>
        public static FrameworkConfig FromDriverConfig(Driver.DriverConfig driverConfig)
        {
            if (driverConfig == null) return CreateDefault();
            return new FrameworkConfig
            {
                Driver = new DriverSection
                {
                    CountThreshold = driverConfig.CountThreshold,
                    ImportanceThreshold = driverConfig.ImportanceThreshold,
                    RecentHistoryCapacity = driverConfig.RecentHistoryCapacity,
                    MaxAgentRounds = driverConfig.MaxAgentRounds
                },
                Diagnostics = new DiagnosticSection(),
                Features = new FeatureToggleSection()
            };
        }

        /// <summary>
        /// 转换为 DriverConfig（向后兼容）。
        /// </summary>
        public Driver.DriverConfig ToDriverConfig()
        {
            return new Driver.DriverConfig
            {
                CountThreshold = Driver?.CountThreshold ?? 5,
                ImportanceThreshold = Driver?.ImportanceThreshold ?? 15,
                RecentHistoryCapacity = Driver?.RecentHistoryCapacity ?? 200,
                MaxAgentRounds = Driver?.MaxAgentRounds ?? 10
            };
        }
    }

    /// <summary>
    /// Agent 驱动配置区。控制事件池触发阈值、历史容量和 Agent 轮数限制。
    /// </summary>
    public class DriverSection
    {
        /// <summary>事件数量阈值：pending 事件数达到此值时触发激活。</summary>
        public int CountThreshold = 5;

        /// <summary>重要度阈值：pending 事件总重要度达到此值时触发激活。</summary>
        public int ImportanceThreshold = 15;

        /// <summary>历史环形缓冲区容量。超出时裁剪最旧事件。</summary>
        public int RecentHistoryCapacity = 200;

        /// <summary>Agent 多轮工具调用最大轮数（防死循环）。</summary>
        public int MaxAgentRounds = 10;
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
    }
}
