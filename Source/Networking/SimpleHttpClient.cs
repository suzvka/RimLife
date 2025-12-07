using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Verse;

namespace RimLife.Networking
{
    public class SimpleHttpClient : IAIClient
    {
        private readonly string _apiKey;
        private const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb"; // This might need to be specific to RimLife
        private const string BaseUrl = "https://api.player2.game/v1";

        public SimpleHttpClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<string> SendStringAsync(string text)
        {
            var messages = new List<Message> { new Message { Role = "user", Content = text } };
            var request = new Player2Request { Messages = messages, Stream = false };
            string jsonContent = JsonUtil.SerializeToJson(request);

            try
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

                using var webRequest = new UnityWebRequest($"{BaseUrl}/chat/completions", "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

                var asyncOperation = webRequest.SendWebRequest();

                while (!asyncOperation.isDone)
                {
                    if (Current.Game == null) return null;
                    await Task.Delay(100);
                }

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    Log.Error($"[RimLife.Networking] Error: {webRequest.responseCode} - {webRequest.error}");
                    return null;
                }

                var response = JsonUtil.DeserializeFromJson<Player2Response>(webRequest.downloadHandler.text);
                return response?.Choices?.FirstOrDefault()?.Message?.Content;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimLife.Networking] Exception: {ex.Message}");
                return null;
            }
        }

        public async Task<string> SendStringStreamingAsync(string text, Action<string> onChunkReceived)
        {
            var messages = new List<Message> { new Message { Role = "user", Content = text } };
            var request = new Player2Request { Messages = messages, Stream = true };
            string jsonContent = JsonUtil.SerializeToJson(request);

            var streamingHandler = new SimpleStreamHandler(chunk =>
            {
                MainThreadDispatcher.Enqueue(() => onChunkReceived?.Invoke(chunk));
            });

            try
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

                using var webRequest = new UnityWebRequest($"{BaseUrl}/chat/completions", "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = streamingHandler;
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

                var asyncOperation = webRequest.SendWebRequest();

                while (!asyncOperation.isDone)
                {
                    if (Current.Game == null) return null;
                    await Task.Delay(100);
                }

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    Log.Error($"[RimLife.Networking] Streaming Error: {webRequest.responseCode} - {webRequest.error}");
                    return null;
                }

                return streamingHandler.GetFullText();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimLife.Networking] Streaming Exception: {ex.Message}");
                return null;
            }
        }
    }
}
