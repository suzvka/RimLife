using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Llm;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimLife.Infrastructure.Llm
{
    /// <summary>
    /// LLM API 访问器。统一入口，功能纯净：只负责 API 调用和格式转换。
    /// 不包含 Agent 循环、对话状态管理或工具调用编排——这些由上层组件负责。
    ///
    /// 所有公共 API 均为异步回调模式（后台线程 → MainThreadDispatcher 回调），
    /// 不会阻塞 UI 线程。
    ///
    /// 职责：
    /// 1. 持有并持久化 LlmConfig（通过 CacheStore）
    /// 2. 根据 ProviderType 创建/切换适配器（ILlmApiProvider）
    /// 3. 异步对话调用（ChatAsync）
    /// 4. 异步连通性测试（TestConnectionAsync，供配置向导使用）
    /// 5. 异步模型列表查询（ListModelsAsync，供配置向导使用）
    /// </summary>
    public class LlmAccessor : ILlmService, IDisposable
    {
        private readonly ICacheStore _store;
        private const string ConfigKey = "rimlife_llm_config";

        private LlmConfig _config;
        private ILlmApiProvider _adapter;
        private readonly object _lock = new object();

        /// <summary>
        /// 创建 LlmAccessor 实例，从 CacheStore 加载配置。
        /// </summary>
        /// <param name="store">缓存存储（CacheStore）。</param>
        public LlmAccessor(ICacheStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            LoadConfig();
        }

        // ================================================================
        // 配置管理
        // ================================================================

        /// <summary>当前配置。修改后需调用 UpdateConfig() 生效。</summary>
        public LlmConfig Config
        {
            get
            {
                lock (_lock) { return _config; }
            }
        }

        /// <summary>是否已配置（baseUrl + key + modelName 非空）。</summary>
        public bool IsConfigured
        {
            get
            {
                lock (_lock)
                {
                    return _config != null && _config.IsValid();
                }
            }
        }

        /// <summary>当前适配器类型名称（调试用）。</summary>
        public string AdapterTypeName
        {
            get
            {
                lock (_lock)
                {
                    return _adapter?.GetType().Name ?? "none";
                }
            }
        }

        /// <summary>
        /// 更新配置并重新创建适配器。持久化到 CacheStore。
        /// </summary>
        /// <param name="config">新配置。</param>
        public void UpdateConfig(LlmConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (_lock)
            {
                _config = config;
                _adapter = CreateAdapter(config);
                PersistConfig();
            }
        }

        /// <summary>
        /// 确保适配器已创建。配置未变时幂等。
        /// </summary>
        public void EnsureAdapter()
        {
            lock (_lock)
            {
                if (_adapter != null) return;
                if (_config == null)
                    _config = LlmConfig.CreateDefault();
                _adapter = CreateAdapter(_config);
            }
        }

        // ================================================================
        // 异步调用（主线程 → 后台线程 → 主线程回调）
        // ================================================================

        /// <summary>
        /// 异步发送对话请求。
        /// 在后台工作线程中执行 HTTP 调用，Task 完成时已通过 MainThreadDispatcher 回主线程。
        /// </summary>
        /// <param name="request">对话请求。</param>
        /// <param name="overrideConfig">临时覆盖配置。非 null 时创建临时 adapter，不影响全局状态。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>LLM 响应 Task。</returns>
        public Task<LlmResponse> ChatAsync(LlmRequest request, LlmConfig overrideConfig = null, CancellationToken ct = default)
        {
            if (request == null)
            {
                return Task.FromException<LlmResponse>(
                    new ArgumentNullException(nameof(request)));
            }

            ILlmApiProvider adapter;

            if (overrideConfig != null)
            {
                // 临时覆盖：创建临时 adapter，不影响全局配置
                adapter = CreateAdapter(overrideConfig);
            }
            else
            {
                lock (_lock)
                {
                    if (_adapter == null)
                    {
                        return Task.FromException<LlmResponse>(
                            new InvalidOperationException("LLM not configured."));
                    }
                    adapter = _adapter;
                }
            }

            var tcs = new TaskCompletionSource<LlmResponse>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var response = adapter.Chat(request);
                    // 通过 MainThreadDispatcher 回主线程完成 Task
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (ct.IsCancellationRequested)
                            tcs.TrySetCanceled(ct);
                        else
                            tcs.TrySetResult(response);
                    });
                }
                catch (OperationCanceledException)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetCanceled(ct));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetException(e));
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 异步测试连通性（供配置向导使用）。
        /// 在后台工作线程中执行，Task 完成时已通过 MainThreadDispatcher 回主线程。
        /// </summary>
        /// <param name="config">要测试的配置。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>true 表示连通成功。</returns>
        public Task<bool> TestConnectionAsync(LlmConfig config, CancellationToken ct = default)
        {
            if (config == null)
            {
                return Task.FromException<bool>(
                    new ArgumentNullException(nameof(config)));
            }

            if (!config.IsValid())
            {
                return Task.FromException<bool>(
                    new ArgumentException("incomplete config: baseUrl, apiKey and modelName are required"));
            }

            var tcs = new TaskCompletionSource<bool>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var adapter = CreateAdapter(config);
                    bool ok = adapter.TestConnection(out string error);
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (ct.IsCancellationRequested)
                            tcs.TrySetCanceled(ct);
                        else if (!ok)
                            tcs.TrySetException(new Exception(error ?? "connection test failed"));
                        else
                            tcs.TrySetResult(true);
                    });
                }
                catch (OperationCanceledException)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetCanceled(ct));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetException(e));
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 异步列出可用模型（供配置向导使用）。
        /// 在后台工作线程中执行，Task 完成时已通过 MainThreadDispatcher 回主线程。
        /// 部分 API 不支持此功能（如 Anthropic），返回空数组。
        /// </summary>
        /// <param name="overrideConfig">临时覆盖配置。非 null 时创建临时 adapter 查询，不影响全局状态。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>模型 ID 列表。</returns>
        public Task<string[]> ListModelsAsync(LlmConfig overrideConfig = null, CancellationToken ct = default)
        {
            ILlmApiProvider adapter;

            if (overrideConfig != null)
            {
                // 临时覆盖：创建临时 adapter，不影响全局配置
                adapter = CreateAdapter(overrideConfig);
            }
            else
            {
                lock (_lock)
                {
                    if (_adapter == null)
                    {
                        return Task.FromException<string[]>(
                            new InvalidOperationException("LLM not configured."));
                    }
                    adapter = _adapter;
                }
            }

            var tcs = new TaskCompletionSource<string[]>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var models = adapter.ListModels();
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        if (ct.IsCancellationRequested)
                            tcs.TrySetCanceled(ct);
                        else
                            tcs.TrySetResult(models);
                    });
                }
                catch (OperationCanceledException)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetCanceled(ct));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => tcs.TrySetException(e));
                }
            });

            return tcs.Task;
        }

        // ================================================================
        // 内部
        // ================================================================

        private ILlmApiProvider CreateAdapter(LlmConfig config)
        {
            switch (config.ProviderType)
            {
                case LlmProviderType.Anthropic:
                    return new AnthropicAdapter(config);
                case LlmProviderType.OpenAI:
                default:
                    return new OpenAiAdapter(config);
            }
        }

        private void LoadConfig()
        {
            try
            {
                _config = _store.FetchOrRebuild(ConfigKey, () => LlmConfig.CreateDefault());
                if (_config != null && _config.IsValid())
                    _adapter = CreateAdapter(_config);
            }
            catch (Exception)
            {
                _config = LlmConfig.CreateDefault();
                _adapter = null;
            }
        }

        private void PersistConfig()
        {
            try
            {
                string json = SerializeConfig(_config);
                _store.Cache(ConfigKey, json);
            }
            catch
            {
                // 持久化失败不应影响运行时
            }
        }

        // ================================================================
        // 配置序列化（简单 JSON，与 JsonWriter 兼容）
        // ================================================================

        private static string SerializeConfig(LlmConfig config)
        {
            if (config == null) return "{}";

            var w = new JsonWriter(512);
            w.Prop("baseUrl", config.BaseUrl ?? "");
            w.Prop("apiKey", config.ApiKey ?? "");
            w.Prop("modelName", config.ModelName ?? "");
            w.Prop("providerType", config.ProviderType.ToString());
            w.Prop("timeoutSeconds", config.TimeoutSeconds);

            // ExtraHeaders
            if (config.ExtraHeaders != null && config.ExtraHeaders.Count > 0)
            {
                var hw = new JsonWriter(256);
                foreach (var kv in config.ExtraHeaders)
                    hw.Prop(kv.Key, kv.Value ?? "");
                w.PropRaw("extraHeaders", hw.Close());
            }

            return w.Close();
        }

        private static LlmConfig DeserializeConfig(string json)
        {
            var config = LlmConfig.CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return config;

            try
            {
                var dict = JsonParser.ParseDict(json);

                if (dict.TryGetValue("baseUrl", out string baseUrl))
                    config.BaseUrl = baseUrl;
                if (dict.TryGetValue("apiKey", out string apiKey))
                    config.ApiKey = apiKey;
                if (dict.TryGetValue("modelName", out string modelName))
                    config.ModelName = modelName;
                if (dict.TryGetValue("providerType", out string ptStr)
                    && Enum.TryParse<LlmProviderType>(ptStr, out var pt))
                    config.ProviderType = pt;
                if (dict.TryGetValue("timeoutSeconds", out string tsStr)
                    && int.TryParse(tsStr, out int ts))
                    config.TimeoutSeconds = ts;

                // ExtraHeaders
                if (dict.TryGetValue("extraHeaders", out string headersJson))
                {
                    var headersDict = JsonParser.ParseDict(headersJson);
                    if (headersDict.Count > 0)
                    {
                        config.ExtraHeaders = new System.Collections.Generic.Dictionary<string, string>();
                        foreach (var kv in headersDict)
                            config.ExtraHeaders[kv.Key] = kv.Value;
                    }
                }
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return config;
        }

        // ================================================================
        // IDisposable
        // ================================================================

        /// <summary>释放适配器资源。</summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_adapter is IDisposable disposable)
                    disposable.Dispose();
                _adapter = null;
                _config = null;
            }
        }
    }
}
