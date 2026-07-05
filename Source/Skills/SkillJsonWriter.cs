using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NPCLife.Framework;

namespace RimLife.Skills
{
    /// <summary>
    /// 轻量 JSON 对象写入器，供 Skill 工具方法使用。
    /// 复刻 NPCLife.JsonWriter 的同名 API，零外部依赖（第三方无需引用 NPCLife）。
    /// </summary>
    public struct SkillJsonWriter
    {
        private readonly StringBuilder _sb;
        private bool _first;

        public SkillJsonWriter(int capacity = 256)
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

        public SkillJsonWriter Prop(string name, string value)
        {
            if (value == null) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":\"").Append(JsonHelper.Escape(value)).Append('"');
            return this;
        }

        public SkillJsonWriter Prop(string name, bool value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value ? "true" : "false");
            return this;
        }

        public SkillJsonWriter Prop(string name, int value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public SkillJsonWriter Prop(string name, long value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public SkillJsonWriter Prop(string name, float value, string format = null)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":");
            _sb.Append(format == null ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
            return this;
        }

        public SkillJsonWriter Prop(string name, double value)
        {
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>写入原始 JSON 值（不转义）。用于嵌套对象/数组。</summary>
        public SkillJsonWriter PropRaw(string name, string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return this;
            CommaIfNeeded();
            _sb.Append('"').Append(JsonHelper.Escape(name)).Append("\":").Append(rawJson);
            return this;
        }

        public SkillJsonWriter Array(string name, IEnumerable<string> values)
        {
            if (values == null) return this;
            var list = values as IList<string> ?? new List<string>(values);
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
