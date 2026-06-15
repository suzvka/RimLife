using System;
using System.Collections.Generic;
using RimLife.Framework.Llm;

namespace RimLife.UI.Models
{
    /// <summary>
    /// 单张 API 凭证卡片。存储 baseUrl + apiKey 二元组。
    /// 多张卡片可通过多选激活，运行时 fallback 遍历。
    /// </summary>
    public class ApiCredentialCard
    {
        /// <summary>卡片唯一 ID。</summary>
        public string Id { get; set; }

        /// <summary>用户自定义标签（用于区分多张卡）。</summary>
        public string Label { get; set; }

        /// <summary>API 基础 URL。</summary>
        public string BaseUrl { get; set; }

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; set; }

        /// <summary>提供商类型。</summary>
        public LlmProviderType ProviderType { get; set; } = LlmProviderType.OpenAI;

        /// <summary>是否当前激活。</summary>
        public bool IsActive { get; set; }

        /// <summary>创建时间戳。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 验证卡片必填字段是否完整。
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Id)
                && !string.IsNullOrEmpty(BaseUrl)
                && !string.IsNullOrEmpty(ApiKey);
        }

        /// <summary>
        /// 创建当前卡片的 LlmConfig 快照（不含模型名，用于无状态查询和运行时覆盖）。
        /// </summary>
        public LlmConfig ToLlmConfig(string modelName = null)
        {
            return new LlmConfig
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                ModelName = modelName ?? "gpt-4o",
                ProviderType = ProviderType
            };
        }

        /// <summary>
        /// 创建新卡片，自动生成 ID。
        /// </summary>
        public static ApiCredentialCard Create(string label, string baseUrl, string apiKey, LlmProviderType providerType = LlmProviderType.OpenAI)
        {
            return new ApiCredentialCard
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label ?? "未命名",
                BaseUrl = baseUrl ?? "",
                ApiKey = apiKey ?? "",
                ProviderType = providerType,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 发现的模型条目。记录模型名称及其来源卡片。
    /// </summary>
    public class ModelEntry
    {
        /// <summary>模型名称/ID。</summary>
        public string ModelName { get; set; }

        /// <summary>来源卡片 ID。</summary>
        public string SourceCardId { get; set; }

        /// <summary>用户是否勾选启用此模型。</summary>
        public bool IsSelected { get; set; }

        /// <summary>发现时间戳。</summary>
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// LLM 凭证完整持久化状态。
    /// 通过 ICacheStore 以 JSON 格式存取。
    /// </summary>
    public class LlmCredentialState
    {
        /// <summary>所有凭证卡片。</summary>
        public List<ApiCredentialCard> Cards { get; set; } = new List<ApiCredentialCard>();

        /// <summary>所有已发现的模型条目（含来源卡片映射）。</summary>
        public List<ModelEntry> DiscoveredModels { get; set; } = new List<ModelEntry>();

        /// <summary>
        /// 用户选定的模型使用顺序（ModelName 列表）。
        /// 运行时从第一个开始，失败则切换到下一个，允许循环。
        /// </summary>
        public List<string> ActiveModelOrder { get; set; } = new List<string>();
    }
}
