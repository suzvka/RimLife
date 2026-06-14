using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Llm;
using System;
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
    public class LlmAccessor : ILlmChatService, IDisposable
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
        /// 在后台工作线程中执行 HTTP 调用，结果通过 MainThreadDispatcher 回调到主线程。
        /// </summary>
        /// <param name="request">对话请求。</param>
        /// <param name="onSuccess">成功回调（主线程）。</param>
        /// <param name="onError">失败回调（主线程）。</param>
        public void ChatAsync(LlmRequest request, Action<LlmResponse> onSuccess, Action<string> onError = null)
        {
            if (request == null)
            {
                MainThreadDispatcher.Enqueue(() => onError?.Invoke("request is null"));
                return;
            }

            ILlmApiProvider adapter;
            lock (_lock)
            {
                if (_adapter == null)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke("LLM not configured. Please set baseUrl, apiKey, and modelName first."));
                    return;
                }
                adapter = _adapter;
            }

            Task.Run(() =>
            {
                try
                {
                    var response = adapter.Chat(request);
                    MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(response));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(e.Message));
                }
            });
        }

        /// <summary>
        /// 异步测试连通性（供配置向导使用）。
        /// 在后台工作线程中执行，结果回调到主线程。
        /// </summary>
        /// <param name="config">要测试的配置。</param>
        /// <param name="onResult">结果回调（主线程）：success=true 表示连通，errorMessage 为空。</param>
        public void TestConnectionAsync(LlmConfig config, Action<bool, string> onResult)
        {
            if (config == null)
            {
                MainThreadDispatcher.Enqueue(() => onResult?.Invoke(false, "config is null"));
                return;
            }

            if (!config.IsValid())
            {
                MainThreadDispatcher.Enqueue(() => onResult?.Invoke(false, "incomplete config: baseUrl, apiKey and modelName are required"));
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var adapter = CreateAdapter(config);
                    bool ok = adapter.TestConnection(out string error);
                    MainThreadDispatcher.Enqueue(() => onResult?.Invoke(ok, error));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => onResult?.Invoke(false, e.Message));
                }
            });
        }

        /// <summary>
        /// 异步列出可用模型（供配置向导使用）。
        /// 在后台工作线程中执行，结果回调到主线程。
        /// 部分 API 不支持此功能（如 Anthropic），返回空数组。
        /// </summary>
        /// <param name="onSuccess">成功回调（主线程），返回模型 ID 列表。</param>
        /// <param name="onError">失败回调（主线程），返回错误描述。</param>
        public void ListModelsAsync(Action<string[]> onSuccess, Action<string> onError = null)
        {
            ILlmApiProvider adapter;
            lock (_lock)
            {
                if (_adapter == null)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke("LLM not configured."));
                    return;
                }
                adapter = _adapter;
            }

            Task.Run(() =>
            {
                try
                {
                    var models = adapter.ListModels();
                    MainThreadDispatcher.Enqueue(() => onSuccess?.Invoke(models));
                }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(e.Message));
                }
            });
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
