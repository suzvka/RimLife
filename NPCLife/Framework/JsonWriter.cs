using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NPCLife.Framework
{
    /// <summary>
    /// 轻量 JSON 对象写入器，最小化内存分配。零外部依赖。
    /// </summary>
    public struct JsonWriter
    {
        private readonly StringBuilder _sb;
        private bool _first;

        public JsonWriter(int capacity = 256)
        {
            _sb = new StringBuilder(capacity);
            _first = true;
            _sb.Append('{');
        }

        private void CommaIfNeeded()
        {
            if (!_first) _sb.Append(',');
            else _first = false;
        }

        public JsonWriter Prop(string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":\"").Append(JsonHelper.Escape(value)).Append('"');
            return this;
        }

        public JsonWriter Prop(string name, bool value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter Prop(string name, int value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Prop(string name, long value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Prop(string name, float value, string format = null)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":");
            _sb.Append(format == null ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Prop(string name, double value, string format = null)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":");
            _sb.Append(format == null ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>
        /// 写入原始 JSON 值（对象/数组/数字/布尔/null），不做转义。
        /// </summary>
        public JsonWriter PropRaw(string name, string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(rawJson);
            return this;
        }

        public JsonWriter Array(string name, IEnumerable<string> values)
        {
            if (values == null) return this;
            var list = values as IList<string> ?? values.ToList();
            if (list.Count == 0) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":[");
            bool first = true;
            foreach (var v in list)
            {
                if (!first) _sb.Append(',');
                first = false;
                _sb.Append('"').Append(JsonHelper.Escape(v ?? string.Empty)).Append('"');
            }
            _sb.Append(']');
            return this;
        }

        /// <summary>
        /// 写入原始 JSON 数组元素（已编码的 JSON 值）。
        /// </summary>
        public JsonWriter ArrayRaw(string name, IEnumerable<string> rawValues)
        {
            if (rawValues == null) return this;
            var list = rawValues as IList<string> ?? rawValues.ToList();
            if (list.Count == 0) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":[");
            bool first = true;
            foreach (var rv in list)
            {
                if (!first) _sb.Append(',');
                first = false;
                _sb.Append(rv ?? "null");
            }
            _sb.Append(']');
            return this;
        }

        public string Close()
        {
            _sb.Append('}');
            return _sb.ToString();
        }

        public override string ToString()
        {
            return _sb.ToString() + "}";
        }
    }
}
