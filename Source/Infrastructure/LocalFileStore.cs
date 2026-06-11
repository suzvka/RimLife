using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using RimLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IPersistentStore 的本地文件实现。
    /// 将缓存数据写入 mod 配置目录下的独立 JSON 文件，按存档 GUID 命名。
    /// 仅实现 Cache/FetchCache/TryFetchCache/FetchOrRebuild/ClearCache（缓存）；
    /// Store 系列方法抛出 NotSupportedException。
    /// </summary>
    public class LocalFileStore : IPersistentStore
    {
        private Dictionary<string, string> _cache = new Dictionary<string, string>();
        private string _filePath;
        private bool _loaded;

        private static string CacheDirectory =>
            Path.Combine(GenFilePaths.ConfigFolderPath, "RimLife", "Cache");

        public LocalFileStore()
        {
            EnsureLoaded();
        }

        // ================================================================
        // IPersistentStore - 缓存
        // ================================================================

        public void Cache<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            _cache[key] = SerializeValue(value);
            SaveToDisk();
        }

        public T FetchCache<T>(string key, T fallback = default)
        {
            if (TryFetchCache(key, out T value)) return value;
            return fallback;
        }

        public bool TryFetchCache<T>(string key, out T value)
        {
            value = default;
            if (string.IsNullOrEmpty(key)) return false;
            EnsureLoaded();

            if (_cache.TryGetValue(key, out string json))
            {
                try
                {
                    value = DeserializeValue<T>(json);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimLife.LocalFileStore] Failed to deserialize cache key '{key}': {e.Message}");
                }
            }
            return false;
        }

        public T FetchOrRebuild<T>(string key, Func<T> factory)
        {
            if (TryFetchCache(key, out T cached)) return cached;

            T result = factory();
            Cache(key, result);
            return result;
        }

        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            if (_cache.Remove(key))
                SaveToDisk();
        }

        // ================================================================
        // IPersistentStore - 权威存储（不支持）
        // ================================================================

        public void Store<T>(string key, T value) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public T Retrieve<T>(string key, T fallback = default) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public bool Contains(string key) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public void Remove(string key) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        // ================================================================
        // 文件 I/O
        // ================================================================

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            string saveId = SaveIdResolver.CurrentSaveId;
            if (string.IsNullOrEmpty(saveId))
            {
                _cache = new Dictionary<string, string>();
                return;
            }

            _filePath = Path.Combine(CacheDirectory, $"{saveId}.json");

            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _cache = DeserializeDict(json) ?? new Dictionary<string, string>();
                }
                else
                {
                    _cache = new Dictionary<string, string>();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.LocalFileStore] Failed to load cache file '{_filePath}': {e.Message}");
                _cache = new Dictionary<string, string>();
            }
        }

        private void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string json = SerializeDict(_cache);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.LocalFileStore] Failed to save cache file '{_filePath}': {e.Message}");
            }
        }

        // ================================================================
        // JSON 序列化辅助（与 RimWorldSaveStore 共用相同格式）
        // ================================================================

        private static string SerializeDict(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";

            var writer = new Framework.JsonWriter(dict.Count * 64);
            foreach (var kv in dict)
            {
                writer.Prop(kv.Key, kv.Value ?? "");
            }
            return writer.Close();
        }

        private static Dictionary<string, string> DeserializeDict(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json) || json == "{}") return result;

            int pos = 1;
            int len = json.Length;

            while (pos < len)
            {
                while (pos < len && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t')) pos++;
                if (pos >= len || json[pos] == '}') break;

                if (json[pos] != '"') break;
                int keyStart = ++pos;
                while (pos < len && json[pos] != '"')
                {
                    if (json[pos] == '\\') pos++;
                    pos++;
                }
                string key = UnescapeJson(json.Substring(keyStart, pos - keyStart));
                pos++;

                while (pos < len && (json[pos] == ' ' || json[pos] == ':')) pos++;

                if (pos >= len) break;
                string value = "";
                if (json[pos] == '"')
                {
                    int valStart = ++pos;
                    while (pos < len && json[pos] != '"')
                    {
                        if (json[pos] == '\\') pos++;
                        pos++;
                    }
                    value = UnescapeJson(json.Substring(valStart, pos - valStart));
                    pos++;
                }
                result[key] = value;

                while (pos < len && (json[pos] == ' ' || json[pos] == ',')) pos++;
            }

            return result;
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    switch (s[i + 1])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(s[i + 1]); break;
                    }
                    i++;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string SerializeValue<T>(T value)
        {
            if (value == null) return "null";
            if (value is string s) return s;
            if (value is int i) return i.ToString();
            if (value is long l) return l.ToString();
            if (value is float f) return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value is bool b) return b ? "true" : "false";
            return value.ToString();
        }

        private static T DeserializeValue<T>(string json)
        {
            if (json == null || json == "null") return default;
            Type t = typeof(T);
            if (t == typeof(string)) return (T)(object)json;
            if (t == typeof(int)) return (T)(object)int.Parse(json);
            if (t == typeof(long)) return (T)(object)long.Parse(json);
            if (t == typeof(float)) return (T)(object)float.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
            if (t == typeof(double)) return (T)(object)double.Parse(json, System.Globalization.CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return (T)(object)bool.Parse(json);
            return default;
        }
    }
}
