using System;
using System.Text;
using UnityEngine.Networking;
using Verse;

namespace RimLife.Networking
{
    public class SimpleStreamHandler : DownloadHandlerScript
    {
        private readonly Action<string> _onContentReceived;
        private readonly StringBuilder _fullText = new StringBuilder();

        public SimpleStreamHandler(Action<string> onContentReceived)
        {
            _onContentReceived = onContentReceived;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return false;

            string chunkStr = Encoding.UTF8.GetString(data, 0, dataLength);
            
            string[] lines = chunkStr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    string jsonData = line.Substring(6);
                    if (jsonData.Trim() == "[DONE]") continue;

                    try
                    {
                        var chunk = JsonUtil.DeserializeFromJson<Player2StreamChunk>(jsonData);
                        if (chunk?.Choices != null && chunk.Choices.Count > 0)
                        {
                            var content = chunk.Choices[0]?.Delta?.Content;
                            if (!string.IsNullOrEmpty(content))
                            {
                                _fullText.Append(content);
                                _onContentReceived?.Invoke(content);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimLife.Networking] Failed to parse stream chunk: {ex.Message}\nJSON: {jsonData}");
                    }
                }
            }
            return true;
        }

        public string GetFullText() => _fullText.ToString();
    }
}
