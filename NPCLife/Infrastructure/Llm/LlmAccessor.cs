using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Infrastructure.Llm
{
    /// <summary>
    /// LLM API 访问器。完全无状态——不持有配置、凭证或适配器。
    /// 每次调用根据传入的 LlmCredential 创建临时适配器，用完即弃。
    ///
    /// ChatAsync 支持多凭证 fallback：按顺序尝试，单个失败自动切换。
    ///
    /// 所有公共 API 均为异步回调模式（后台线程 → MainThreadDispatcher 回调），
    /// 不会阻塞 UI 线程。
    ///
    /// 职责：
    /// 1. 根据 LlmCredential.ProviderType 创建对应适配器
    /// 2. 异步对话调用（ChatAsync，含内置多凭证 fallback）
    /// 3. 异步连通性测试（TestConnectionAsync）
    /// 4. 异步模型列表查询（ListModelsAsync）
    /// </summary>
    public class LlmAccessor : ILlmService, IDisposable
    {
        private readonly ILogger _logger;

        /// <summary>
        /// 创建 LlmAccessor 实例。可注入 ILogger 用于日志输出。
        /// </summary>
        public LlmAccessor(ILogger logger = null)
        {
            _logger = logger;
        }

        // ================================================================
        // 异步调用（主线程 → 后台线程 → 主线程回调）
        // ================================================================

        /// <summary>
        /// 异步发送对话请求，支持多凭证 fallback。
        /// 按 credentials 顺序依次尝试：成功则立即返回，失败则自动切换下一个。
        /// 全部失败时返回最后一个错误信息。
        /// </summary>
        public Task<LlmResponse> ChatAsync(
            LlmRequest request,
            IReadOnlyList<LlmCredential> credentials,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                return Task.FromException<LlmResponse>(
                    new ArgumentNullException(nameof(request)));
            }
            if (credentials == null || credentials.Count == 0)
            {
                return Task.FromException<LlmResponse>(
                    new ArgumentException("At least one credential is required.", nameof(credentials)));
            }

            // 单凭证路径：跳过 fallback 开销
            if (credentials.Count == 1)
            {
                return ChatSingleAsync(request, credentials[0], ct);
            }

            // 多凭证路径：依次尝试 fallback
            return ChatWithFallbackAsync(request, credentials, ct);
        }

        private Task<LlmResponse> ChatSingleAsync(
            LlmRequest request, LlmCredential credential, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<LlmResponse>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    // 注入模型名到请求
                    if (string.IsNullOrEmpty(request.Model) && !string.IsNullOrEmpty(credential.ModelName))
                    {
                        request.Model = credential.ModelName;
                    }

                    var adapter = CreateAdapter(credential);
                    var response = adapter.Chat(request);

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

        private Task<LlmResponse> ChatWithFallbackAsync(
            LlmRequest request, IReadOnlyList<LlmCredential> credentials, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<LlmResponse>();

            Task.Run(async () =>
            {
                LlmResponse lastResponse = null;
                string lastError = null;

                for (int i = 0; i < credentials.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var credential = credentials[i];
                    if (credential == null || !credential.IsValid())
                    {
                        lastError = $"credential[{i}] is null or invalid";
                        continue;
                    }

                    try
                    {
                        // 每次尝试用独立的请求副本（避免前次修改污染）
                        var attemptRequest = CloneRequest(request);
                        if (string.IsNullOrEmpty(attemptRequest.Model) && !string.IsNullOrEmpty(credential.ModelName))
                        {
                            attemptRequest.Model = credential.ModelName;
                        }

                        var adapter = CreateAdapter(credential);
                        var response = adapter.Chat(attemptRequest);

                        if (response != null && response.IsSuccess)
                        {
                            // 成功：立即返回
                            MainThreadDispatcher.Enqueue(() =>
                            {
                                if (ct.IsCancellationRequested)
                                    tcs.TrySetCanceled(ct);
                                else
                                    tcs.TrySetResult(response);
                            });
                            return;
                        }

                        lastResponse = response;
                        lastError = response?.Error ?? "unknown error";
                    }
                    catch (OperationCanceledException)
                    {
                        MainThreadDispatcher.Enqueue(() => tcs.TrySetCanceled(ct));
                        return;
                    }
                    catch (Exception e)
                    {
                        lastError = e.Message;
                    }

                    // 还有下一个凭证则继续
                    _logger?.Warning(
                        $"[NPCLife.LlmAccessor] Fallback: credential[{i}] ({credential.ModelName}) failed: {lastError}. " +
                        $"{(i + 1 < credentials.Count ? "Trying next..." : "All credentials exhausted.")}");
                }

                // 全部失败
                var finalResponse = lastResponse ?? LlmResponse.FromError(lastError ?? "All credentials failed");
                MainThreadDispatcher.Enqueue(() =>
                {
                    if (ct.IsCancellationRequested)
                        tcs.TrySetCanceled(ct);
                    else
                        tcs.TrySetResult(finalResponse);
                });
            });

            return tcs.Task;
        }

        /// <summary>
        /// 异步测试连通性（供配置向导使用）。
        /// </summary>
        public Task<bool> TestConnectionAsync(LlmCredential credential, CancellationToken ct = default)
        {
            if (credential == null)
            {
                return Task.FromException<bool>(
                    new ArgumentNullException(nameof(credential)));
            }

            if (!credential.IsValid())
            {
                return Task.FromException<bool>(
                    new ArgumentException("incomplete credential: baseUrl, apiKey and modelName are required"));
            }

            var tcs = new TaskCompletionSource<bool>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var adapter = CreateAdapter(credential);
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
        /// </summary>
        public Task<string[]> ListModelsAsync(LlmCredential credential, CancellationToken ct = default)
        {
            if (credential == null)
            {
                return Task.FromException<string[]>(
                    new ArgumentNullException(nameof(credential)));
            }

            var tcs = new TaskCompletionSource<string[]>();

            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var adapter = CreateAdapter(credential);
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

        /// <summary>
        /// 根据凭证创建适配器。每次调用创建新实例（包括新 HttpClient）。
        /// </summary>
        internal ILlmApiProvider CreateAdapter(LlmCredential credential)
        {
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            switch (credential.ProviderType)
            {
                case LlmProviderType.Anthropic:
                    return new AnthropicAdapter(credential, _logger);
                case LlmProviderType.OpenAI:
                default:
                    return new OpenAiAdapter(credential, _logger);
            }
        }

        /// <summary>
        /// 浅拷贝请求，避免 fallback 重试时修改原请求。
        /// </summary>
        private static LlmRequest CloneRequest(LlmRequest original)
        {
            return new LlmRequest
            {
                Model = original.Model,
                Messages = original.Messages != null
                    ? new List<LlmMessage>(original.Messages)
                    : new List<LlmMessage>(),
                ToolsJson = original.ToolsJson,
                Temperature = original.Temperature
            };
        }

        // ================================================================
        // IDisposable
        // ================================================================

        /// <summary>无状态实现，无需释放资源。</summary>
        public void Dispose()
        {
        }
    }
}
