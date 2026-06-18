using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NPCLife.Framework
{
    /// <summary>
    /// 框架状态内省接口。提供版本信息、健康检查和能力查询。
    /// 纯静态，零 RimWorld 依赖。
    ///
    /// 使用方式：
    ///   // 组件注册状态报告器
    ///   FrameworkStatus.RegisterReporter("Llm", () => new ComponentStatus { ... });
    ///
    ///   // 查询健康状态
    ///   var report = FrameworkStatus.HealthCheck();
    ///
    ///   // 查询能力
    ///   var caps = FrameworkStatus.GetCapabilities();
    /// </summary>
    public static class FrameworkStatus
    {
        private static readonly Dictionary<string, Func<ComponentStatus>> _reporters =
            new Dictionary<string, Func<ComponentStatus>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, bool> _capabilities =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();
        private static string _version;

        // ================================================================
        // 版本信息
        // ================================================================

        /// <summary>框架名称。</summary>
        public static string FrameworkName => "RimLife";

        /// <summary>
        /// 框架版本号。从程序集版本自动读取，也可手动设置。
        /// </summary>
        public static string Version
        {
            get
            {
                if (_version == null)
                {
                    try
                    {
                        _version = Assembly.GetExecutingAssembly()
                            .GetName().Version?.ToString() ?? "0.0.0";
                    }
                    catch
                    {
                        _version = "0.0.0";
                    }
                }
                return _version;
            }
            set => _version = value;
        }

        // ================================================================
        // 状态报告器注册
        // ================================================================

        /// <summary>
        /// 注册组件状态报告器。HealthCheck 时会调用所有报告器。
        /// 重复注册同名组件会替换旧报告器。
        /// </summary>
        /// <param name="componentName">组件名（如 "EventLog", "Llm", "Workspace"）。</param>
        /// <param name="reporter">状态报告函数，返回 ComponentStatus。</param>
        public static void RegisterReporter(string componentName, Func<ComponentStatus> reporter)
        {
            if (string.IsNullOrEmpty(componentName) || reporter == null) return;
            lock (_lock)
            {
                _reporters[componentName] = reporter;
            }
        }

        /// <summary>
        /// 移除组件状态报告器。
        /// </summary>
        public static void UnregisterReporter(string componentName)
        {
            if (string.IsNullOrEmpty(componentName)) return;
            lock (_lock)
            {
                _reporters.Remove(componentName);
            }
        }

        // ================================================================
        // 能力注册
        // ================================================================

        /// <summary>
        /// 注册能力标识。用于 GetCapabilities() 查询。
        /// </summary>
        /// <param name="capabilityName">能力名（如 "streaming", "multi_model"）。</param>
        /// <param name="supported">是否支持。</param>
        public static void RegisterCapability(string capabilityName, bool supported)
        {
            if (string.IsNullOrEmpty(capabilityName)) return;
            lock (_lock)
            {
                _capabilities[capabilityName] = supported;
            }
        }

        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 健康检查：收集所有已注册组件的状态，汇总为 HealthReport。
        /// </summary>
        public static HealthReport HealthCheck()
        {
            var report = new HealthReport
            {
                Components = new Dictionary<string, ComponentStatus>(),
                Issues = new List<string>()
            };

            Dictionary<string, Func<ComponentStatus>> snapshot;
            lock (_lock)
            {
                snapshot = new Dictionary<string, Func<ComponentStatus>>(_reporters);
            }

            foreach (var kv in snapshot)
            {
                try
                {
                    var status = kv.Value();
                    if (status == null)
                    {
                        status = new ComponentStatus
                        {
                            Name = kv.Key,
                            IsAvailable = false,
                            Detail = "Reporter returned null"
                        };
                        report.Issues.Add($"{kv.Key}: reporter returned null");
                    }
                    report.Components[kv.Key] = status;

                    if (!status.IsAvailable)
                        report.Issues.Add($"{kv.Key}: unavailable ({status.Detail ?? "no detail"})");
                }
                catch (Exception ex)
                {
                    var errorStatus = new ComponentStatus
                    {
                        Name = kv.Key,
                        IsAvailable = false,
                        Detail = $"Reporter error: {ex.Message}"
                    };
                    report.Components[kv.Key] = errorStatus;
                    report.Issues.Add($"{kv.Key}: reporter threw exception");
                }
            }

            // 基础检查
            if (!LifecycleManager.IsInitialized)
                report.Issues.Add("Framework not initialized");

            report.IsHealthy = report.Issues.Count == 0;
            return report;
        }

        /// <summary>
        /// 能力查询：返回当前支持的特性字典。
        /// </summary>
        public static IReadOnlyDictionary<string, bool> GetCapabilities()
        {
            lock (_lock)
            {
                return new Dictionary<string, bool>(_capabilities);
            }
        }

        /// <summary>
        /// 序列化为 JSON 字符串（用于 MCP 工具返回）。
        /// </summary>
        public static string ToJson()
        {
            var report = HealthCheck();
            var caps = GetCapabilities();

            var w = new JsonWriter(512);
            w.Prop("framework", FrameworkName);
            w.Prop("version", Version);
            w.Prop("initialized", LifecycleManager.IsInitialized);
            w.Prop("healthy", report.IsHealthy);

            // issues
            if (report.Issues.Count > 0)
                w.Array("issues", report.Issues);

            // capabilities
            var cw = new JsonWriter(128);
            foreach (var kv in caps)
                cw.Prop(kv.Key, kv.Value);
            w.PropRaw("capabilities", cw.Close());

            return w.Close();
        }

        /// <summary>清除所有报告器和能力注册。通常用于 Shutdown。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _reporters.Clear();
                _capabilities.Clear();
            }
        }
    }

    /// <summary>
    /// 健康检查报告。包含各组件状态和问题汇总。
    /// </summary>
    public class HealthReport
    {
        /// <summary>整体是否健康（Issues 为空时 true）。</summary>
        public bool IsHealthy;

        /// <summary>组件名 → 组件状态。</summary>
        public Dictionary<string, ComponentStatus> Components;

        /// <summary>发现的问题列表。</summary>
        public List<string> Issues;
    }

    /// <summary>
    /// 单个组件的状态描述。
    /// </summary>
    public class ComponentStatus
    {
        /// <summary>组件名。</summary>
        public string Name;

        /// <summary>组件是否可用。</summary>
        public bool IsAvailable;

        /// <summary>详细信息（如 "OpenAI adapter, model: gpt-4o"）。</summary>
        public string Detail;
    }
}
