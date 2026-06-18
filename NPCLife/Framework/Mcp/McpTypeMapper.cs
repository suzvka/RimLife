using System;
using System.Collections;
using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// C# 类型到 JSON Schema 类型字符串的映射器。纯静态，零依赖。
    /// </summary>
    public static class McpTypeMapper
    {
        /// <summary>
        /// 获取类型的 JSON Schema 类型字符串。
        /// 对可空类型和 Task&lt;T&gt; 自动解包。
        /// </summary>
        public static string GetSchemaType(Type type)
        {
            if (type == null) return "string";

            type = UnwrapIfNullable(type);

            // 基础值类型优先
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";

            if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
                type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort))
                return "integer";

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return "number";

            // 枚举 → string
            if (type.IsEnum) return "string";

            // 数组 / 集合 → array
            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
                return "array";

            // 其他 → object
            return "object";
        }

        /// <summary>
        /// 获取数组/集合的元素类型。非集合类型返回 null。
        /// </summary>
        public static Type GetElementType(Type type)
        {
            if (type == null || type == typeof(string)) return null;

            // T[]
            if (type.IsArray)
                return type.GetElementType();

            // List<T>, IList<T>, IEnumerable<T> ...
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            // 自身即是泛型集合（如 List<T>）
            if (type.IsGenericType && typeof(IEnumerable<>)
                .MakeGenericType(type.GetGenericArguments()[0]).IsAssignableFrom(type))
            {
                return type.GetGenericArguments()[0];
            }

            return null;
        }

        /// <summary>
        /// 若类型是 Nullable&lt;T&gt;，返回 T；否则返回原类型。
        /// </summary>
        public static Type UnwrapIfNullable(Type type)
        {
            if (type == null) return null;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                return type.GetGenericArguments()[0];
            return type;
        }
    }
}
