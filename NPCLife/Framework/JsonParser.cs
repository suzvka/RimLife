using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NPCLife.Framework
{
    /// <summary>
    /// 轻量 JSON 解析与序列化工具。零外部依赖。
    /// 将 LocalFileStore / RimWorldSaveStore / RimWorldEventLog 中的
    /// 重复 JSON 代码统一收口于此。
    /// </summary>
    public static class JsonParser
    {
        // ================================================================
        // 解析
        // ================================================================

        /// <summary>
        /// 解析 JSON 对象为字典。字符串值自动反转义；
        /// 嵌套对象/数组保留为原始 JSON 字符串。
        /// </summary>
        public static Dictionary<string, string> ParseDict(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json) || json == "{}") return result;

            int pos = 1; // skip '{'
            int len = json.Length;

            while (pos < len)
            {
                // 跳过空白
                while (pos < len && IsWhitespace(json[pos])) pos++;
                if (pos >= len || json[pos] == '}') break;

                // 读 key
                if (json[pos] != '"') break;
                int keyStart = ++pos;
                while (pos < len && json[pos] != '"')
                {
                    if (json[pos] == '\\') pos++;
                    pos++;
                }
                string key = UnescapeJson(json.Substring(keyStart, pos - keyStart));
                pos++; // skip closing '"'

                // 跳过 ':'
                while (pos < len && (json[pos] == ' ' || json[pos] == ':')) pos++;
                if (pos >= len) break;

                // 读 value
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
                else if (json[pos] == '{' || json[pos] == '[')
                {
                    // 嵌套对象/数组：整体截取为原始 JSON
                    int depth = 1;
                    int valStart = pos;
                    pos++;
                    while (pos < len && depth > 0)
                    {
                        if (json[pos] == '{' || json[pos] == '[') depth++;
                        else if (json[pos] == '}' || json[pos] == ']') depth--;
                        pos++;
                    }
                    value = json.Substring(valStart, pos - valStart);
                }
                else
                {
                    // 裸值（数字、布尔）
                    int valStart = pos;
                    while (pos < len && json[pos] != ',' && json[pos] != '}'
                        && !IsWhitespace(json[pos]))
                        pos++;
                    value = json.Substring(valStart, pos - valStart);
                }
                result[key] = value;

                // 跳过 ','
                while (pos < len && (json[pos] == ' ' || json[pos] == ',')) pos++;
            }

            return result;
        }

        /// <summary>
        /// 解析 JSON 对象数组。每个元素通过 ParseDict 解析。
        /// </summary>
        public static List<Dictionary<string, string>> ParseObjectArray(string json)
        {
            var result = new List<Dictionary<string, string>>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            int pos = 1; // skip '['
            while (pos < json.Length)
            {
                while (pos < json.Length && (json[pos] == ' ' || json[pos] == ',' || json[pos] == '\n')) pos++;
                if (pos >= json.Length || json[pos] == ']') break;
                if (json[pos] == '{')
                {
                    int depth = 1;
                    int start = pos;
                    pos++;
                    while (pos < json.Length && depth > 0)
                    {
                        if (json[pos] == '{') depth++;
                        else if (json[pos] == '}') depth--;
                        pos++;
                    }
                    string objJson = json.Substring(start, pos - start);
                    result.Add(ParseDict(objJson));
                }
                else pos++;
            }
            return result;
        }

        /// <summary>
        /// 按 JSON 标准反转义字符串中的特殊字符。
        /// </summary>
        public static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
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
                        case 'u':
                            // \uXXXX Unicode 转义
                            if (i + 5 < s.Length)
                            {
                                var hex = s.Substring(i + 2, 4);
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    sb.Append((char)code);
                                    i += 4; // 外层 i++ 会再跳 1，共跳过 5 个字符 (\uXXXX)
                                }
                            }
                            break;
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

        /// <summary>
        /// 解析 JSON 字符串数组。期望格式 ["a","b",...]。
        /// 返回反转义后的字符串列表。
        /// </summary>
        public static List<string> ParseStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            int pos = 1; // skip '['
            int len = json.Length;
            while (pos < len)
            {
                while (pos < len && (json[pos] == ' ' || json[pos] == ',' || json[pos] == '\n')) pos++;
                if (pos >= len || json[pos] == ']') break;

                if (json[pos] == '"')
                {
                    int start = ++pos;
                    while (pos < len && json[pos] != '"')
                    {
                        if (json[pos] == '\\') pos++;
                        pos++;
                    }
                    result.Add(UnescapeJson(json.Substring(start, pos - start)));
                    pos++; // skip closing '"'
                }
                else
                {
                    // 裸值（数字等），跳过
                    while (pos < len && json[pos] != ',' && json[pos] != ']') pos++;
                }
            }
            return result;
        }

        // ================================================================
        // 序列化辅助
        // ================================================================

        /// <summary>
        /// 将 string→string 字典序列化为 JSON 对象字符串。
        /// </summary>
        public static string SerializeDict(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return "{}";

            var writer = new JsonWriter(dict.Count * 64);
            foreach (var kv in dict)
            {
                writer.Prop(kv.Key, kv.Value ?? "");
            }
            return writer.Close();
        }

        /// <summary>
        /// 将常见基础类型序列化为 JSON 值字符串。
        /// 不支持的类型回退到 ToString()。
        /// </summary>
        public static string SerializeValue<T>(T value)
        {
            if (value == null) return "null";
            if (value is string s) return s;
            if (value is int i) return i.ToString();
            if (value is long l) return l.ToString();
            if (value is float f) return f.ToString(CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (value is bool b) return b ? "true" : "false";
            return value.ToString();
        }

        /// <summary>
        /// 将 JSON 值字符串反序列化为指定基础类型。
        /// 不支持的类型返回 default。
        /// </summary>
        public static T DeserializeValue<T>(string json)
        {
            if (json == null || json == "null") return default;
            Type t = typeof(T);
            if (t == typeof(string)) return (T)(object)json;
            if (t == typeof(int)) return (T)(object)int.Parse(json);
            if (t == typeof(long)) return (T)(object)long.Parse(json);
            if (t == typeof(float)) return (T)(object)float.Parse(json, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return (T)(object)double.Parse(json, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return (T)(object)bool.Parse(json);
            return default;
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\n' || c == '\r' || c == '\t';
        }
    }
}
