using NPCLife.Framework.Llm;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Core
{
    /// <summary>
    /// 凭证注册表接口。管理"模型代号 → API 凭证三元组"的映射。
    /// 
    /// 职责：
    /// 1. 别名管理：SetAlias / RemoveAlias / TryGetCredential / GetAllAliases
    /// 2. 激活顺序：SetActiveAliases / GetActiveAliases（决定 fallback 链路）
    /// 3. 模型发现：DiscoverModelsAsync / AddManualModel
    /// 4. 持久化：Load / Save（由实现方决定存储后端）
    ///
    /// 框架通过此接口为 Agent 提供凭证，Agent 通过代号引用模型。
    /// 连接器（ILlmService）只认 LlmCredential 三元组，不感知代号。
    /// </summary>
    public interface ICredentialRegistry
    {
        // ================================================================
        // 别名管理
        // ================================================================

        /// <summary>
        /// 设置或覆盖一个代号对应的凭证。
        /// </summary>
        /// <param name="alias">模型代号（如 "primary", "fast", "claude"）。</param>
        /// <param name="credential">API 凭证三元组。</param>
        void SetAlias(string alias, LlmCredential credential);

        /// <summary>
        /// 移除指定代号及其凭证。
        /// </summary>
        void RemoveAlias(string alias);

        /// <summary>
        /// 按代号查找凭证。
        /// </summary>
        /// <returns>找到返回 true。</returns>
        bool TryGetCredential(string alias, out LlmCredential credential);

        /// <summary>
        /// 获取所有已配置的代号列表（无序）。
        /// </summary>
        IReadOnlyList<string> GetAllAliases();

        /// <summary>
        /// 是否有任何可用凭证。
        /// </summary>
        bool HasAnyCredential { get; }

        // ================================================================
        // 激活顺序（fallback 链路）
        // ================================================================

        /// <summary>
        /// 设置激活的代号顺序列表。
        /// 运行时按此顺序尝试，失败自动切换下一个。
        /// </summary>
        /// <param name="aliases">代号顺序列表，如 ["primary", "fast", "claude"]。</param>
        void SetActiveAliases(IReadOnlyList<string> aliases);

        /// <summary>
        /// 获取当前激活的代号顺序列表。
        /// </summary>
        IReadOnlyList<string> GetActiveAliases();

        /// <summary>
        /// 获取当前激活顺序对应的凭证列表。
        /// 过滤掉不存在或无效的代号。返回空列表表示无可用凭证。
        /// </summary>
        IReadOnlyList<LlmCredential> GetActiveCredentials();

        // ================================================================
        // 模型发现
        // ================================================================

        /// <summary>
        /// 异步发现所有已配置凭证的可用模型列表。
        /// 对每个代号对应的凭证依次调用 llmService.ListModelsAsync。
        /// 结果用于 UI 展示，不自动设置别名。
        /// </summary>
        /// <param name="llmService">LLM 服务（用于无状态查询）。</param>
        /// <param name="progressCallback">进度回调：(current, total, alias, modelCount)，modelCount=-1 表示失败。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>发现的模型列表，key=来源代号，value=模型名列表。</returns>
        Task<IReadOnlyDictionary<string, string[]>> DiscoverModelsAsync(
            ILlmService llmService,
            System.Action<int, int, string, int> progressCallback = null,
            CancellationToken ct = default);

        /// <summary>
        /// 手动注册一个模型到指定代号下（用于 Anthropic 等不支持列表查询的 API）。
        /// </summary>
        /// <param name="alias">目标代号。</param>
        /// <param name="modelName">模型名称。</param>
        void AddManualModel(string alias, string modelName);
    }
}
