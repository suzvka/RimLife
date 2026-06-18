using NPCLife.Framework.Llm;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Core
{
    /// <summary>
    /// LLM 服务的统一异步契约。完全无状态——所有方法接受显式凭证参数。
    /// 
    /// ChatAsync 接受凭证列表，内部按顺序尝试（内置 fallback），
    /// 全部失败才返回错误，调用方无需处理模型切换逻辑。
    /// 
    /// 所有方法在工作线程中执行 HTTP 调用，Task 完成时已通过
    /// MainThreadDispatcher 回主线程，不阻塞 UI。
    /// </summary>
    public interface ILlmService
    {
        /// <summary>
        /// 异步发送对话请求，支持多凭证 fallback。
        /// 按 credentials 顺序依次尝试，单个失败自动切换下一个。
        /// 全部失败时返回最后一个错误。
        /// </summary>
        /// <param name="request">内部统一格式的请求。</param>
        /// <param name="credentials">按优先级排序的凭证列表。服务内部按顺序尝试。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>成功响应或最后一个失败的错误信息。</returns>
        Task<LlmResponse> ChatAsync(
            LlmRequest request,
            IReadOnlyList<LlmCredential> credentials,
            CancellationToken ct = default);

        /// <summary>
        /// 异步测试单个凭证的 API 连通性。用于配置向导中的连接测试。
        /// </summary>
        /// <param name="credential">待测试的凭证。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>true 表示连接成功。</returns>
        Task<bool> TestConnectionAsync(LlmCredential credential, CancellationToken ct = default);

        /// <summary>
        /// 异步列出 API 端可用的模型列表。
        /// 部分 API 不支持此功能（如 Anthropic），返回空数组。
        /// </summary>
        /// <param name="credential">用于查询的凭证。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>模型 ID 列表。</returns>
        Task<string[]> ListModelsAsync(LlmCredential credential, CancellationToken ct = default);
    }
}
