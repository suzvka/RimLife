using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// MCP 工具运行时调用器。将 JSON 参数字符串反序列化、反射调用目标方法、并序列化返回值。
    /// 纯静态，零 RimWorld 依赖。
    /// </summary>
    public static class McpToolInvoker
    {
        /// <summary>
        /// 通过反射调用方法。jsonArgs 为 JSON 对象字符串（如 {"tag":"Raid","limit":10}）。
        /// 返回值为 JSON 字符串。
        /// </summary>
        /// <param name="method">目标方法。</param>
        /// <param name="target">实例方法的目标对象，静态方法传 null。</param>
        /// <param name="jsonArgs">JSON 对象格式的参数字符串。</param>
        /// <returns>返回值序列化后的 JSON 字符串。</returns>
        public static string Invoke(MethodInfo method, object target, string jsonArgs)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            var argDict = string.IsNullOrEmpty(jsonArgs)
                ? new Dictionary<string, string>()
                : JsonParser.ParseDict(jsonArgs);

            var parameters = method.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var paramAttr = p.GetCustomAttribute<McpParamAttribute>();
                string paramName = paramAttr?.Name;
                if (string.IsNullOrEmpty(paramName)) paramName = p.Name;

                if (argDict.TryGetValue(paramName, out var rawValue))
                {
                    args[i] = ConvertArg(rawValue, p.ParameterType);
                }
                else if (p.IsOptional)
                {
                    args[i] = p.DefaultValue; // 可能是 Type.Missing 或实际默认值
                }
                else
                {
                    // 必填参数缺失 → 使用类型默认值并记录
                    args[i] = GetDefaultValue(p.ParameterType);
                }
            }

            try
            {
                var result = method.Invoke(target, args);
                return SerializeResult(result, method.ReturnType);
            }
            catch (TargetInvocationException ex)
            {
                // 解包反射异常，保留内部堆栈
                var inner = ex.InnerException ?? ex;
                return "{\"error\":" + JsonHelper.Quote(inner.Message) + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":" + JsonHelper.Quote(ex.Message) + "}";
            }
        }

        /// <summary>
        /// 通过委托调用。便捷方法，自动提取 MethodInfo 和 Target。
        /// </summary>
        public static string InvokeDelegate(Delegate del, string jsonArgs)
        {
            if (del == null) throw new ArgumentNullException(nameof(del));
            return Invoke(del.Method, del.Target, jsonArgs);
        }

        // ================================================================
        // 参数转换
        // ================================================================

        private static object ConvertArg(string raw, Type targetType)
        {
            if (raw == null) return null;

            targetType = McpTypeMapper.UnwrapIfNullable(targetType);

            try
            {
                if (targetType == typeof(string)) return raw;
                if (targetType == typeof(bool)) return ParseBool(raw);
                if (targetType == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(short)) return short.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(byte)) return byte.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(float)) return float.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(double)) return double.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(decimal)) return decimal.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(uint)) return uint.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(ulong)) return ulong.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(ushort)) return ushort.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(sbyte)) return sbyte.Parse(raw, CultureInfo.InvariantCulture);

                // 枚举
                if (targetType.IsEnum)
                    return Enum.Parse(targetType, raw, ignoreCase: true);

                // 数组
                if (targetType.IsArray)
                    return ConvertArray(raw, targetType.GetElementType());

                // 泛型集合
                if (typeof(IList).IsAssignableFrom(targetType) && targetType.IsGenericType)
                {
                    var elemType = targetType.GetGenericArguments()[0];
                    return ConvertList(raw, targetType, elemType);
                }

                // 无法转换，返回原始字符串
                return raw;
            }
            catch
            {
                // 类型转换失败，返回默认值
                return GetDefaultValue(targetType);
            }
        }

        private static bool ParseBool(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (bool.TryParse(raw, out var b)) return b;
            // 宽松匹配
            var lower = raw.Trim().ToLowerInvariant();
            if (lower == "1" || lower == "yes" || lower == "on") return true;
            if (lower == "0" || lower == "no" || lower == "off") return false;
            return false;
        }

        private static Array ConvertArray(string raw, Type elemType)
        {
            var items = JsonParser.ParseStringArray(raw);
            if (items == null || items.Count == 0)
                return Array.CreateInstance(elemType, 0);

            var arr = Array.CreateInstance(elemType, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                arr.SetValue(ConvertArg(items[i], elemType), i);
            }
            return arr;
        }

        private static object ConvertList(string raw, Type listType, Type elemType)
        {
            var items = JsonParser.ParseStringArray(raw);
            var list = (IList)Activator.CreateInstance(listType);
            if (items != null)
            {
                foreach (var item in items)
                {
                    list.Add(ConvertArg(item, elemType));
                }
            }
            return list;
        }

        // ================================================================
        // 返回值序列化
        // ================================================================

        private static string SerializeResult(object result, Type returnType)
        {
            if (result == null || returnType == typeof(void))
                return "null";

            var underlying = McpTypeMapper.UnwrapIfNullable(returnType);

            // 基础类型
            if (result is string s)
                return JsonHelper.Quote(s);

            if (underlying == typeof(bool))
                return ((bool)result) ? "true" : "false";

            if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) ||
                underlying == typeof(byte) || underlying == typeof(sbyte) ||
                underlying == typeof(uint) || underlying == typeof(ulong) || underlying == typeof(ushort))
                return ((IFormattable)result).ToString(null, CultureInfo.InvariantCulture);

            if (underlying == typeof(float))
                return ((float)result).ToString("R", CultureInfo.InvariantCulture);

            if (underlying == typeof(double))
                return ((double)result).ToString("R", CultureInfo.InvariantCulture);

            if (underlying == typeof(decimal))
                return ((decimal)result).ToString(CultureInfo.InvariantCulture);

            // 枚举 → 字符串
            if (underlying.IsEnum)
                return JsonHelper.Quote(result.ToString());

            // 集合 → JSON 数组
            if (result is IEnumerable enumerable && !(result is string))
            {
                var sb = new StringBuilder("[");
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(SerializeResult(item, item?.GetType() ?? typeof(object)));
                }
                sb.Append(']');
                return sb.ToString();
            }

            // 复杂对象 → 引用类型字符串表示
            return JsonHelper.Quote(result.ToString());
        }

        private static object GetDefaultValue(Type type)
        {
            if (type == null) return null;
            if (type == typeof(string)) return string.Empty;
            if (type == typeof(bool)) return false;
            if (type.IsValueType) return Activator.CreateInstance(type);
            return null;
        }
    }
}
