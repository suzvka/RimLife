using System;
using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM API 提供者类型枚举。
    /// </summary>
    public enum LlmProviderType
    {
        /// <summary>OpenAI 及兼容 API（Ollama、vLLM、中转代理等）。</summary>
        OpenAI,

        /// <summary>Anthropic Messages API。</summary>
        Anthropic
    }

    /// <summary>
    /// LLM API 访问配置。
    /// 包含 baseUrl + apiKey + modelName 三元组，以及提供商类型和扩展头。
    /// 纯数据类，零外部依赖。通过 CacheStore 持久化。
    /// </summary>
    public class LlmConfig
    {
        /// <summary>API 基础 URL，默认 OpenAI。</summary>
        public string BaseUrl { get; set; } = "https://api.openai.com";

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>模型名称。</summary>
        public string ModelName { get; set; } = "";

        /// <summary>提供商类型，决定使用哪个适配器。</summary>
        public LlmProviderType ProviderType { get; set; } = LlmProviderType.OpenAI;

        /// <summary>扩展 HTTP 头，用于需要自定义 header 的代理场景。</summary>
        public Dictionary<string, string> ExtraHeaders { get; set; }

        /// <summary>HTTP 请求超时（秒），默认 120。</summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// 验证配置是否有效（至少包含 url + key + model）。
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(BaseUrl)
                && !string.IsNullOrEmpty(ApiKey)
                && !string.IsNullOrEmpty(ModelName);
        }

        /// <summary>
        /// 创建默认配置（OpenAI）。
        /// </summary>
        public static LlmConfig CreateDefault()
        {
            return new LlmConfig
            {
                BaseUrl = "https://api.openai.com",
                ApiKey = "",
                ModelName = "",
                ProviderType = LlmProviderType.OpenAI,
                TimeoutSeconds = 120
            };
        }
    }
}
