using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Verse;

namespace RimLife.Networking
{
    public static class JsonUtil
    {
        public static string SerializeToJson<T>(T obj)
        {
            using var stream = new MemoryStream();
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, obj);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static T DeserializeFromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimLife.Networking] Json deserialization failed for {typeof(T).Name}\n{json}\nError: {ex.Message}");
                throw;
            }
        }
    }
}
