using System;
using System.Collections.Generic;
using RimLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IPersistentStore 的存档文件实现。
    /// 通过 WorldComponent 将数据嵌入 RimWorld 存档文件。
    /// 仅实现 Store/Retrieve/Contains/Remove（权威存储）；
    /// Cache 系列方法抛出 NotSupportedException。
    /// </summary>
    public class RimWorldSaveStore : WorldComponent, IPersistentStore
    {
        private Dictionary<string, string> _data = new Dictionary<string, string>();
        private const string SaveIdKey = "__rimlife_save_guid";

        public RimWorldSaveStore(World world) : base(world)
        {
        }

        // ================================================================
        // IPersistentStore - 权威存储
        // ================================================================

        public void Store<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string json = SerializeValue(value);
            _data[key] = json;
        }

        public T Retrieve<T>(string key, T fallback = default)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            if (_data.TryGetValue(key, out string json))
            {
                try
                {
                    return DeserializeValue<T>(json);
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimLife.SaveStore] Failed to deserialize key '{key}': {e.Message}");
                }
            }
            return fallback;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _data.ContainsKey(key);
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
                _data.Remove(key);
        }

        // ================================================================
        // IPersistentStore - 缓存（不支持）
        // ================================================================

        public void Cache<T>(string key, T value) =>
            throw new NotSupportedException("RimWorldSaveStore does not support cache operations. Use LocalFileStore.");

        public T FetchCache<T>(string key, T fallback = default) =>
            throw new NotSupportedException("RimWorldSaveStore does not support cache operations. Use LocalFileStore.");

        public bool TryFetchCache<T>(string key, out T value) =>
            throw new NotSupportedException("RimWorldSaveStore does not support cache operations. Use LocalFileStore.");

        public T FetchOrRebuild<T>(string key, Func<T> factory) =>
            throw new NotSupportedException("RimWorldSaveStore does not support cache operations. Use LocalFileStore.");

        public void ClearCache(string key) =>
            throw new NotSupportedException("RimWorldSaveStore does not support cache operations. Use LocalFileStore.");

        // ================================================================
        // WorldComponent 序列化
        // ================================================================

        public override void ExposeData()
        {
            base.ExposeData();

            // 存档 GUID：首次生成，后续持久化
            string saveId = _data.ContainsKey(SaveIdKey) ? _data[SaveIdKey] : null;
            if (Scribe.mode == LoadSaveMode.Saving && string.IsNullOrEmpty(saveId))
            {
                saveId = Guid.NewGuid().ToString("D");
                _data[SaveIdKey] = saveId;
            }

            // 序列化整个字典为 JSON 字符串
            string serialized = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                serialized = SerializeDict(_data);
            }
            Scribe_Values.Look(ref serialized, "rimLifeData", null);

            if (Scribe.mode == LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(serialized))
            {
                _data = DeserializeDict(serialized) ?? new Dictionary<string, string>();
            }

            // 通知 SaveIdResolver
            if (_data.TryGetValue(SaveIdKey, out string resolvedId))
            {
                SaveIdResolver.SetSaveId(resolvedId);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 确保 SaveIdResolver 在加载后已设置
            if (_data.TryGetValue(SaveIdKey, out string resolvedId))
            {
                SaveIdResolver.SetSaveId(resolvedId);
            }
            // 注册到核心服务定位器
            RimLifeCore.SaveStore = this;
        }

        // ================================================================
        // JSON 序列化辅助
        // ================================================================

        private static string SerializeDict(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";

            var writer = new Tool.JsonWriter(dict.Count * 64);
            foreach (var kv in dict)
            {
                writer.Prop(kv.Key, kv.Value ?? "");
            }
            return writer.Close();
        }

        private static Dictionary<string, string> DeserializeDict(string json)
        {
            // 简单 JSON 解析：{"key1":"val1","key2":"val2"}
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json) || json == "{}") return result;

            int pos = 1; // skip '{'
            int len = json.Length;

            while (pos < len)
            {
                // 跳过空白
                while (pos < len && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t')) pos++;
                if (pos >= len || json[pos] == '}') break;

                // 读 key
                if (json[pos] != '"') break;
                int keyStart = ++pos;
                while (pos < len && json[pos] != '"')
                {
                    if (json[pos] == '\\') pos++; // skip escaped char
                    pos++;
                }
                string key = UnescapeJson(json.Substring(keyStart, pos - keyStart));
                pos++; // skip closing '"'

                // 跳过 ':'
                while (pos < len && (json[pos] == ' ' || json[pos] == ':')) pos++;

                // 读 value
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
                    pos++; // skip closing '"'
                }
                result[key] = value;

                // 跳过 ',' 或 '}'
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
                else
                {
                    sb.Append(s[i]);
                }
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

            // 回退：ToString
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
