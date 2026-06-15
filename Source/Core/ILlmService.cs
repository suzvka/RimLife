using RimLife.Framework.Llm;
using System.Threading;
using System.Threading.Tasks;

namespace RimLife.Core
{
    /// <summary>
    /// LLM 服务的统一异步契约。
    /// 所有方法在工作线程中执行，不阻塞主线程。
    /// 对话适配器 (ILlmApiProvider) 降级为 infrastructure 内部实现细节。
    /// </summary>
    public interface ILlmService
    {
        /// <summary>
        /// 异步发送对话请求。
        /// 在后台工作线程中执行 HTTP 调用，内部通过 MainThreadDispatcher 确保
        /// Task 完成后的回调在主线程执行。
        /// </summary>
        /// <param name="request">内部统一格式的请求。</param>
        /// <param name="overrideConfig">临时覆盖配置。非 null 时使用此配置创建临时 adapter，
        /// 不影响全局配置。用于多模型 fallback 场景。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>内部统一格式的响应。</returns>
        Task<LlmResponse> ChatAsync(LlmRequest request, LlmConfig overrideConfig = null, CancellationToken ct = default);

        /// <summary>
        /// 异步测试 API 连通性。用于配置向导中的连接测试。
        /// </summary>
        /// <param name="config">待测试的配置。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>true 表示连接成功。</returns>
        Task<bool> TestConnectionAsync(LlmConfig config, CancellationToken ct = default);

        /// <summary>
        /// 异步列出 API 端可用的模型列表。
        /// 部分 API 不支持此功能（如 Anthropic），返回空数组。
        /// </summary>
        /// <param name="overrideConfig">临时覆盖配置。非 null 时使用此配置查询模型列表，
        /// 不影响全局配置。用于配置面板无状态查询多端点模型。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>模型 ID 列表。</returns>
        Task<string[]> ListModelsAsync(LlmConfig overrideConfig = null, CancellationToken ct = default);
    }
}
