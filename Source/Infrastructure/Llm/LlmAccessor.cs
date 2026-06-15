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
    /// 不存储 API 密钥：配置由前端 LlmCredentialManager 在启动时注入。
    ///
    /// 职责：
    /// 1. 持有运行时 LlmConfig（纯内存，不持久化）
    /// 2. 根据 ProviderType 创建/切换适配器（ILlmApiProvider）
    /// 3. 异步对话调用（ChatAsync）
    /// 4. 异步连通性测试（TestConnectionAsync，供配置向导使用）
    /// 5. 异步模型列表查询（ListModelsAsync，供配置向导使用）
    /// </summary>
    public class LlmAccessor : ILlmService, IDisposable
    {
        private LlmConfig _config;
        private ILlmApiProvider _adapter;
        private readonly object _lock = new object();

        /// <summary>
        /// 创建 LlmAccessor 实例。初始化为默认配置，等待前端注入。
        /// </summary>
        public LlmAccessor()
        {
            _config = LlmConfig.CreateDefault();
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
            string modelName = null;

            if (overrideConfig != null)
            {
                // 临时覆盖：创建临时 adapter，不影响全局配置
                adapter = CreateAdapter(overrideConfig);
                modelName = overrideConfig.ModelName;
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
                    modelName = _config?.ModelName;
                }
            }

            // 自动注入模型名称到请求（如果请求未设置）
            if (string.IsNullOrEmpty(request.Model) && !string.IsNullOrEmpty(modelName))
            {
                request.Model = modelName;
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
