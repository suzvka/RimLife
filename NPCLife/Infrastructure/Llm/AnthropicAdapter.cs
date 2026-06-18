using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NPCLife.Infrastructure.Llm
{
    /// <summary>
    /// Anthropic Messages API 适配器（internal）。
    /// 将内部统一格式转换为 Anthropic Messages 请求/响应格式。
    /// 在工作线程中同步调用，由上层 LlmAccessor 管理线程。
    /// 仅 LlmAccessor 内部使用。对外暴露使用 ILlmService。
    ///
    /// 关键差异：
    /// - system prompt 是顶层字段而非 message
    /// - tool messages 使用特殊的 user content 块
    /// - 响应中 tool_use 在 content 数组中，而非独立的 tool_calls 字段
    /// </summary>
    internal class AnthropicAdapter : ILlmApiProvider
    {
        private readonly LlmCredential _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        /// <summary>Anthropic API 版本 header。</summary>
        private const string AnthropicVersion = "2023-06-01";

        public AnthropicAdapter(LlmCredential config, ILogger logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
            _httpClient = CreateHttpClient(config);
        }

        // ================================================================
        // ILlmApiProvider
        // ================================================================

        public LlmResponse Chat(LlmRequest request)
        {
            if (request == null)
                return LlmResponse.FromError("request is null");
            if (!request.IsValid())
                return LlmResponse.FromError("invalid request: model and messages required");

            try
            {
                string requestJson = BuildChatRequest(request);
                string responseJson = SendHttpRequest("/v1/messages", requestJson);
                return ParseChatResponse(responseJson);
            }
            catch (HttpRequestException e)
            {
                return LlmResponse.FromError($"HTTP error: {e.Message}");
            }
            catch (TaskCanceledException)
            {
                return LlmResponse.FromError("Request timed out");
            }
            catch (Exception e)
            {
                return LlmResponse.FromError(e.Message);
            }
        }

        public bool TestConnection(out string error)
        {
            error = null;
            try
            {
                // Anthropic 没有 /v1/models，直接发最小请求测试
                var testRequest = LlmRequest.SinglePrompt(
                    _config.ModelName,
                    "Hi.");
                string requestJson = BuildChatRequest(testRequest);
                string responseJson = SendHttpRequest("/v1/messages", requestJson);
                var response = ParseChatResponse(responseJson);
                if (response.IsSuccess)
                    return true;
                error = response.Error;
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Anthropic 不支持列出模型，返回空数组。
        /// </summary>
        public string[] ListModels()
        {
            return new string[0];
        }

        // ================================================================
        // 内部：HTTP
        // ================================================================

        private HttpClient CreateHttpClient(LlmCredential config)
        {
            var handler = new HttpClientHandler();
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 120)
            };

            string baseUrl = (config.BaseUrl ?? "").TrimEnd('/');
            client.BaseAddress = new Uri(baseUrl + "/");

            client.DefaultRequestHeaders.Clear();
            // Anthropic 使用 x-api-key header 而非 Authorization
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (config.ExtraHeaders != null)
            {
                foreach (var kv in config.ExtraHeaders)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                        client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            return client;
        }

        private string SendHttpRequest(string path, string bodyJson, HttpMethod method = null)
        {
            method = method ?? HttpMethod.Post;
            var request = new HttpRequestMessage(method, path);

            if (bodyJson != null)
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        // ================================================================
        // 内部：请求构建（Anthropic 格式）
        // ================================================================

        private string BuildChatRequest(LlmRequest req)
        {
            var w = new JsonWriter(2048);
            w.Prop("model", req.Model);

            // temperature
            if (req.Temperature.HasValue)
                w.Prop("temperature", req.Temperature.Value);

            // system prompt：提取 system 角色的消息作为顶层 system 字段
            string systemPrompt = ExtractSystemPrompt(req);
            if (!string.IsNullOrEmpty(systemPrompt))
                w.Prop("system", systemPrompt);

            // messages：排除 system 消息
            var messages = FilterNonSystemMessages(req);
            if (messages.Count > 0)
            {
                var msgJsons = new List<string>();
                foreach (var msg in messages)
                    msgJsons.Add(BuildMessage(msg));
                w.ArrayRaw("messages", msgJsons);
            }

            // tools：转换为 Anthropic 格式
            if (!string.IsNullOrEmpty(req.ToolsJson))
            {
                string anthropicTools = ConvertToolsToAnthropic(req.ToolsJson);
                if (!string.IsNullOrEmpty(anthropicTools))
                    w.PropRaw("tools", anthropicTools);
            }

            return w.Close();
        }

        private string ExtractSystemPrompt(LlmRequest req)
        {
            if (req.Messages == null) return null;
            foreach (var msg in req.Messages)
            {
                if (msg.Role == "system" && !string.IsNullOrEmpty(msg.Content))
                    return msg.Content;
            }
            return null;
        }

        private List<LlmMessage> FilterNonSystemMessages(LlmRequest req)
        {
            var result = new List<LlmMessage>();
            if (req.Messages == null) return result;
            foreach (var msg in req.Messages)
            {
                if (msg.Role != "system")
                    result.Add(msg);
            }
            return result;
        }

        private string BuildMessage(LlmMessage msg)
        {
            var w = new JsonWriter(512);
            w.Prop("role", MapRole(msg.Role));

            // content：Anthropic 使用 content 数组
            var contentBlocks = new List<string>();

            if (msg.Role == "tool")
            {
                // Anthropic tool_result: user role 含 tool_result content block
                var trWriter = new JsonWriter(256);
                trWriter.Prop("type", "tool_result");
                trWriter.Prop("tool_use_id", msg.ToolCallId ?? "");
                trWriter.Prop("content", msg.Content ?? "");
                contentBlocks.Add(trWriter.Close());
            }
            else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                // assistant 请求工具调用
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    var textWriter = new JsonWriter(128);
                    textWriter.Prop("type", "text");
                    textWriter.Prop("text", msg.Content);
                    contentBlocks.Add(textWriter.Close());
                }
                foreach (var tc in msg.ToolCalls)
                    contentBlocks.Add(BuildToolUseBlock(tc));
            }
            else
            {
                // 普通文本消息
                var textWriter = new JsonWriter(256);
                textWriter.Prop("type", "text");
                textWriter.Prop("text", msg.Content ?? "");
                contentBlocks.Add(textWriter.Close());
            }

            if (contentBlocks.Count == 1)
                w.PropRaw("content", $"[{contentBlocks[0]}]");
            else if (contentBlocks.Count > 1)
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < contentBlocks.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(contentBlocks[i]);
                }
                sb.Append(']');
                w.PropRaw("content", sb.ToString());
            }

            return w.Close();
        }

        private string MapRole(string role)
        {
            // Anthropic roles: user, assistant
            // "tool" → "user"
            if (role == "tool") return "user";
            if (role == "system") return "user"; // system 已在顶层
            return role ?? "user";
        }

        private string BuildToolUseBlock(LlmToolCall tc)
        {
            var w = new JsonWriter(256);
            w.Prop("type", "tool_use");
            w.Prop("id", tc.Id ?? "");
            w.Prop("name", tc.Name ?? "");
            w.PropRaw("input", tc.Arguments ?? "{}");
            return w.Close();
        }

        private string ConvertToolsToAnthropic(string openAiToolsJson)
        {
            // 解析 OpenAI 格式的 tools JSON，转换为 Anthropic 格式
            // OpenAI: [{"type":"function","function":{"name":"...","description":"...","parameters":{...}}}]
            // Anthropic: [{"name":"...","description":"...","input_schema":{...}}]
            try
            {
                var toolDicts = JsonParser.ParseObjectArray(openAiToolsJson);
                var anthropicTools = new List<string>();

                foreach (var td in toolDicts)
                {
                    if (!td.TryGetValue("function", out string funcJson)) continue;
                    var funcDict = JsonParser.ParseDict(funcJson);

                    var at = new JsonWriter(256);
                    if (funcDict.TryGetValue("name", out string name))
                        at.Prop("name", name);
                    if (funcDict.TryGetValue("description", out string desc))
                        at.Prop("description", desc);
                    if (funcDict.TryGetValue("parameters", out string paramsJson))
                        at.PropRaw("input_schema", paramsJson);

                    anthropicTools.Add(at.Close());
                }

                if (anthropicTools.Count == 0) return null;

                var sb = new StringBuilder("[");
                for (int i = 0; i < anthropicTools.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(anthropicTools[i]);
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ================================================================
        // 内部：响应解析（Anthropic 格式）
        // ================================================================

        private LlmResponse ParseChatResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return LlmResponse.FromError("Empty response");

            try
            {
                var dict = JsonParser.ParseDict(json);

                // 错误响应
                if (dict.TryGetValue("error", out string errorJson))
                {
                    var errorDict = JsonParser.ParseDict(errorJson);
                    string errorMsg = errorDict.TryGetValue("message", out string em) ? em : "API error";
                    return LlmResponse.FromError(errorMsg);
                }

                var result = new LlmResponse();

                // stop_reason
                if (dict.TryGetValue("stop_reason", out string stopReason))
                    result.FinishReason = MapStopReason(stopReason);

                // content 数组
                if (dict.TryGetValue("content", out string contentJson))
                {
                    var contentBlocks = JsonParser.ParseObjectArray(contentJson);
                    var textParts = new List<string>();
                    var toolCalls = new List<LlmToolCall>();

                    foreach (var block in contentBlocks)
                    {
                        if (block.TryGetValue("type", out string blockType))
                        {
                            if (blockType == "text" && block.TryGetValue("text", out string text))
                            {
                                textParts.Add(text);
                            }
                            else if (blockType == "tool_use")
                            {
                                var tc = new LlmToolCall();
                                if (block.TryGetValue("id", out string tid))
                                    tc.Id = tid;
                                if (block.TryGetValue("name", out string tname))
                                    tc.Name = tname;
                                if (block.TryGetValue("input", out string tinput))
                                    tc.Arguments = tinput;
                                toolCalls.Add(tc);
                            }
                        }
                    }

                    result.Content = textParts.Count > 0 ? string.Join("\n", textParts) : null;
                    if (toolCalls.Count > 0)
                        result.ToolCalls = toolCalls;
                }

                // usage
                if (dict.TryGetValue("usage", out string usageJson))
                {
                    var usageDict = JsonParser.ParseDict(usageJson);
                    if (usageDict.TryGetValue("input_tokens", out string itStr) &&
                        usageDict.TryGetValue("output_tokens", out string otStr) &&
                        int.TryParse(itStr, out int it) &&
                        int.TryParse(otStr, out int ot))
                    {
                        result.UsageTotalTokens = it + ot;
                        result.UsageInputTokens = it;
                        result.UsageOutputTokens = ot;
                    }

                    // cache_read_input_tokens: Anthropic prompt caching
                    if (usageDict.TryGetValue("cache_read_input_tokens", out string crStr)
                        && int.TryParse(crStr, out int cr))
                        result.UsageCacheReadTokens = cr;
                }

                // model
                if (dict.TryGetValue("model", out string modelName))
                    result.Model = modelName;

                return result;
            }
            catch (Exception e)
            {
                return LlmResponse.FromError($"Parse error: {e.Message}");
            }
        }

        private string MapStopReason(string anthropicReason)
        {
            switch (anthropicReason)
            {
                case "end_turn": return "stop";
                case "tool_use": return "tool_calls";
                case "max_tokens": return "length";
                case "stop_sequence": return "stop";
                default: return anthropicReason ?? "stop";
            }
        }
    }
}
