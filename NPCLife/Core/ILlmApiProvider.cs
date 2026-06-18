using NPCLife.Framework.Llm;
using System;

namespace NPCLife.Core
{
    /// <summary>
    /// [internal] LLM API 提供者统一接口。
    /// 每种 API 格式（OpenAI / Anthropic / 本地兼容）实现此接口。
    /// 所有方法在工作线程中同步调用，不阻塞主线程。
    /// 仅 LlmAccessor 内部使用。对外暴露使用 <see cref="ILlmService"/>。
    /// </summary>
    internal interface ILlmApiProvider
    {
        /// <summary>
        /// 发送对话请求并返回 LLM 响应。
        /// 在工作线程中同步调用，由上层 LlmAccessor 管理线程调度。
        /// </summary>
        /// <param name="request">内部统一格式的请求。</param>
        /// <returns>内部统一格式的响应。</returns>
        LlmResponse Chat(LlmRequest request);

        /// <summary>
        /// 测试 API 连通性。用于配置向导中的连接测试。
        /// 工作线程中同步调用。
        /// </summary>
        /// <param name="error">出错时的错误描述。</param>
        /// <returns>true 表示连接成功。</returns>
        bool TestConnection(out string error);

        /// <summary>
        /// 列出 API 端可用的模型列表。
        /// 部分 API 不支持此功能（如 Anthropic），返回空数组。
        /// </summary>
        string[] ListModels();
    }
}
