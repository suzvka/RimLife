using System.Text;

namespace NPCLife.Framework
{
    /// <summary>
    /// 轻量 JSON 字符串转义与引用工具。零外部依赖。
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// 按 JSON 标准转义字符串中的特殊字符。
        /// </summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 将字符串包裹在双引号中并转义内容。
        /// </summary>
        public static string Quote(string s)
        {
            return '"' + Escape(s ?? string.Empty) + '"';
        }
    }
}
