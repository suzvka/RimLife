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
    /// OpenAI 及兼容 API（Ollama / vLLM / 中转代理）适配器（internal）。
    /// 将内部统一格式转换为 OpenAI Chat Completions 请求/响应格式。
    /// 在工作线程中同步调用，由上层 LlmAccessor 管理线程。
    /// 仅 LlmAccessor 内部使用。对外暴露使用 ILlmService。
    /// </summary>
    internal class OpenAiAdapter : ILlmApiProvider
    {
        private readonly LlmCredential _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public OpenAiAdapter(LlmCredential config, ILogger logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger;
            _httpClient = CreateHttpClient(config);
        }

        // ================================================================
        // ILlmApiProvider
        // ================================================================

        /// <summary>
        /// 发送对话请求。同步调用，在后台工作线程中执行。
        /// </summary>
        public LlmResponse Chat(LlmRequest request)
        {
            if (request == null)
                return LlmResponse.FromError("request is null");
            if (!request.IsValid())
                return LlmResponse.FromError($"invalid request: model='{request.Model}', messages={request.Messages?.Count ?? 0}");

            try
            {
                string requestJson = BuildChatRequest(request);
                // 调试：记录请求 JSON
                _logger?.Message($"[NPCLife.OpenAiAdapter] Request JSON: {TruncateJson(requestJson)}");
                
                string responseJson = SendHttpRequest("/v1/chat/completions", requestJson);
                var response = ParseChatResponse(responseJson);
                
                // 调试：记录响应状态
                if (!response.IsSuccess)
                    _logger?.Warning($"[NPCLife.OpenAiAdapter] Response error: {response.Error}");
                else
                    _logger?.Message($"[NPCLife.OpenAiAdapter] Success, content length: {response.Content?.Length ?? 0}");
                
                return response;
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

        /// <summary>
        /// 测试连通性：发一条最小请求验证 API 可用。
        /// </summary>
        public bool TestConnection(out string error)
        {
            error = null;
            try
            {
                // 先用 /v1/models 测试连通性
                string modelsJson = SendHttpRequest("/v1/models", null, HttpMethod.Get);
                if (!string.IsNullOrEmpty(modelsJson))
                    return true;
            }
            catch
            {
                // /v1/models 失败，尝试发一条最小聊天请求
            }

            try
            {
                var testRequest = LlmRequest.SinglePrompt(
                    _config.ModelName,
                    "Hi. Respond with just 'ok'.");
                string requestJson = BuildChatRequest(testRequest);
                string responseJson = SendHttpRequest("/v1/chat/completions", requestJson);
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
        /// 列出可用模型。
        /// </summary>
        public string[] ListModels()
        {
            try
            {
                string json = SendHttpRequest("/v1/models", null, HttpMethod.Get);
                if (string.IsNullOrEmpty(json))
                    return new string[0];

                var dict = JsonParser.ParseDict(json);
                if (dict.TryGetValue("data", out string dataJson))
                {
                    var models = JsonParser.ParseObjectArray(dataJson);
                    var ids = new List<string>();
                    foreach (var m in models)
                    {
                        if (m.TryGetValue("id", out string id) && !string.IsNullOrEmpty(id))
                            ids.Add(id);
                    }
                    return ids.ToArray();
                }
            }
            catch
            {
                // 忽略错误
            }
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

            // 清理 baseUrl：去掉尾部斜杠
            string baseUrl = (config.BaseUrl ?? "").TrimEnd('/');
            client.BaseAddress = new Uri(baseUrl + "/");

            // 默认 headers
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            // 扩展 headers
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

            // 同步发送（在后台工作线程中，阻塞是合理的）
            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
            
            // 如果状态码不是成功，读取响应体获取详细错误信息
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {TruncateJson(errorBody, 300)}"
                );
            }
            
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        // ================================================================
        // 内部：请求构建
        // ================================================================

        private string BuildChatRequest(LlmRequest req)
        {
            var w = new JsonWriter(2048);
            w.Prop("model", req.Model);

            // messages
            if (req.Messages != null && req.Messages.Count > 0)
            {
                var msgJsons = new List<string>();
                foreach (var msg in req.Messages)
                    msgJsons.Add(BuildMessage(msg));
                w.ArrayRaw("messages", msgJsons);
            }

            // temperature
            if (req.Temperature.HasValue)
                w.Prop("temperature", req.Temperature.Value);

            // tools
            if (!string.IsNullOrEmpty(req.ToolsJson))
                w.PropRaw("tools", req.ToolsJson);

            return w.Close();
        }

        private string BuildMessage(LlmMessage msg)
        {
            var w = new JsonWriter(512);
            w.Prop("role", msg.Role ?? "user");

            if (msg.Content != null)
                w.Prop("content", msg.Content);

            // tool call id（tool 角色）
            if (!string.IsNullOrEmpty(msg.ToolCallId))
                w.Prop("tool_call_id", msg.ToolCallId);

            // tool_calls（assistant 请求工具调用）
            if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                var tcJsons = new List<string>();
                foreach (var tc in msg.ToolCalls)
                    tcJsons.Add(BuildToolCall(tc));
                w.ArrayRaw("tool_calls", tcJsons);
            }

            return w.Close();
        }

        private string BuildToolCall(LlmToolCall tc)
        {
            var w = new JsonWriter(256);
            w.Prop("id", tc.Id ?? "");
            w.Prop("type", "function");

            var funcWriter = new JsonWriter(128);
            funcWriter.Prop("name", tc.Name ?? "");
            funcWriter.Prop("arguments", tc.Arguments ?? "{}");
            w.PropRaw("function", funcWriter.Close());

            return w.Close();
        }

        // ================================================================
        // 内部：响应解析
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

                // choices[0]
                if (dict.TryGetValue("choices", out string choicesJson))
                {
                    var choices = JsonParser.ParseObjectArray(choicesJson);
                    if (choices.Count > 0)
                    {
                        var choice = choices[0];

                        // finish_reason
                        if (choice.TryGetValue("finish_reason", out string finishReason))
                            result.FinishReason = finishReason;

                        // message
                        if (choice.TryGetValue("message", out string messageJson))
                        {
                            var msgDict = JsonParser.ParseDict(messageJson);

                            // content
                            if (msgDict.TryGetValue("content", out string content))
                                result.Content = content;

                            // tool_calls
                            if (msgDict.TryGetValue("tool_calls", out string toolCallsJson))
                                result.ToolCalls = ParseToolCalls(toolCallsJson);
                        }
                    }
                }

                // usage
                if (dict.TryGetValue("usage", out string usageJson))
                {
                    var usageDict = JsonParser.ParseDict(usageJson);
                    if (usageDict.TryGetValue("total_tokens", out string totalStr)
                        && int.TryParse(totalStr, out int total))
                        result.UsageTotalTokens = total;
                    if (usageDict.TryGetValue("prompt_tokens", out string ptStr)
                        && int.TryParse(ptStr, out int pt))
                        result.UsageInputTokens = pt;
                    if (usageDict.TryGetValue("completion_tokens", out string ctStr)
                        && int.TryParse(ctStr, out int ct))
                        result.UsageOutputTokens = ct;

                    // cached_tokens: prompt_tokens_details.cached_tokens
                    if (usageDict.TryGetValue("prompt_tokens_details", out string ptdJson))
                    {
                        var ptdDict = JsonParser.ParseDict(ptdJson);
                        if (ptdDict.TryGetValue("cached_tokens", out string cachedStr)
                            && int.TryParse(cachedStr, out int cached))
                            result.UsageCacheReadTokens = cached;
                    }
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

        private List<LlmToolCall> ParseToolCalls(string json)
        {
            var result = new List<LlmToolCall>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            var toolCallDicts = JsonParser.ParseObjectArray(json);
            foreach (var tcDict in toolCallDicts)
            {
                var tc = new LlmToolCall();

                if (tcDict.TryGetValue("id", out string id))
                    tc.Id = id;

                // type: "function"
                if (tcDict.TryGetValue("function", out string funcJson))
                {
                    var funcDict = JsonParser.ParseDict(funcJson);
                    if (funcDict.TryGetValue("name", out string name))
                        tc.Name = name;
                    if (funcDict.TryGetValue("arguments", out string args))
                        tc.Arguments = args;
                }

                result.Add(tc);
            }

            return result;
        }

        private static string TruncateJson(string json, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(json)) return "(empty)";
            return json.Length > maxLength ? json.Substring(0, maxLength) + "..." : json;
        }
    }
}
