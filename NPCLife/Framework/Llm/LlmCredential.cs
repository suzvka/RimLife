using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM API 凭证。无状态数据类，包含 baseUrl + apiKey + modelName 三元组。
    /// 零外部依赖，不持有任何持久化状态。
    /// 
    /// 与 LlmConfig 的区别：LlmConfig 持有默认值和全局状态假设，
    /// LlmCredential 是纯数据传递对象，所有字段由调用方显式提供。
    /// </summary>
    public class LlmCredential
    {
        /// <summary>API 基础 URL。</summary>
        public string BaseUrl { get; set; }

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; set; }

        /// <summary>模型名称。</summary>
        public string ModelName { get; set; }

        /// <summary>提供商类型，决定使用哪个适配器。</summary>
        public LlmProviderType ProviderType { get; set; } = LlmProviderType.OpenAI;

        /// <summary>扩展 HTTP 头，用于需要自定义 header 的代理场景。</summary>
        public Dictionary<string, string> ExtraHeaders { get; set; }

        /// <summary>HTTP 请求超时（秒），默认 120。</summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// 验证凭证是否完整（baseUrl + apiKey + modelName 均非空）。
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(BaseUrl)
                && !string.IsNullOrEmpty(ApiKey)
                && !string.IsNullOrEmpty(ModelName);
        }

        /// <summary>
        /// 创建凭证快照副本（浅拷贝字符串和集合引用）。
        /// </summary>
        public LlmCredential Clone()
        {
            return new LlmCredential
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                ModelName = ModelName,
                ProviderType = ProviderType,
                ExtraHeaders = ExtraHeaders != null
                    ? new Dictionary<string, string>(ExtraHeaders)
                    : null,
                TimeoutSeconds = TimeoutSeconds
            };
        }

        public override string ToString()
        {
            return $"LlmCredential({ProviderType} {ModelName} @ {BaseUrl})";
        }
    }
}
