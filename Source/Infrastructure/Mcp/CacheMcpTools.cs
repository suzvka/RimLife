using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 共享 KV 缓存的 MCP 工具集。供导演/编剧等 Agent 在环境中共享信息。
    /// 底层存储使用 RimLifeCore.CacheStore（LocalFileStore），按存档维度隔离。
    /// </summary>
    public static class CacheMcpTools
    {
        // ================================================================
        // 写入
        // ================================================================

        /// <summary>
        /// 写入字符串键值对到共享缓存。值会覆盖同名 key。
        /// </summary>
        [McpTool(Name = "set_kv_cache",
                 Description = "写入字符串键值对到共享缓存。覆盖同名 key。建议 key 用冒号命名空间，如 'lookup:心灵冲击'。")]
        public static string SetKvCache(
            [McpParam(Description = "缓存键。建议命名空间:值，如 lookup:心灵冲击")] string key,
            [McpParam(Description = "缓存值（字符串）")] string value)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return "{\"hit\":false,\"error\":\"key is required\"}";

                var store = RimLifeCore.CacheStore;
                if (store == null)
                    return "{\"hit\":false,\"error\":\"CacheStore unavailable\"}";

                store.Cache(key, value);

                var w = new JsonWriter(128);
                w.Prop("hit", true);
                w.Prop("key", key);
                return w.Close();
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.CacheMcp] set_kv_cache({key}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 读取
        // ================================================================

        /// <summary>
        /// 从共享缓存读取值。先精确匹配 key，未命中则自动降级为分词匹配。
        /// </summary>
        [McpTool(Name = "get_kv_cache",
                 Description = "从共享缓存读取值。先精确匹配 key，未命中则自动降级分词匹配（空格/逗号分词，要求 key 同时包含所有 token）。")]
        public static string GetKvCache(
            [McpParam(Description = "查询内容。先精确匹配，未命中则分词后查找包含所有 token 的 key")] string query)
        {
            try
            {
                if (string.IsNullOrEmpty(query))
                    return "{\"hit\":false,\"error\":\"query is required\"}";

                var store = RimLifeCore.CacheStore;
                if (store == null)
                    return "{\"hit\":false,\"error\":\"CacheStore unavailable\"}";

                // 1. 精确匹配
                if (store.TryFetchCache(query, out string value))
                    return MakeHit(query, value);

                // 2. 降级：分词匹配
                return TryGetToken(store, query);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.CacheMcp] get_kv_cache({query}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        private static string TryGetToken(Core.IPersistentStore store, string query)
        {
            // 按空格和逗号分词
            var tokens = query.Split(new char[] { ' ', ',', '，' })
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (tokens.Count == 0) return MakeMiss(query);

            foreach (var key in store.ListCacheKeys())
            {
                if (tokens.All(t => key.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (store.TryFetchCache(key, out string value))
                        return MakeHit(key, value);
                }
            }
            return MakeMiss(query);
        }

        // ================================================================
        // 列表
        // ================================================================

        /// <summary>
        /// 列出所有缓存 key 及值摘要。支持前缀过滤。
        /// </summary>
        [McpTool(Name = "list_kv_cache",
                 Description = "列出所有缓存 key 及值摘要（截断至 120 字符）。可选按前缀过滤。")]
        public static string ListKvCache(
            [McpParam(Description = "key 前缀过滤，如 'lookup:'。留空=全部",
                      Required = McpRequired.False)] string prefix = null)
        {
            try
            {
                var store = RimLifeCore.CacheStore;
                if (store == null) return "[]";

                var keys = store.ListCacheKeys();
                if (!string.IsNullOrEmpty(prefix))
                    keys = keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                var sb = new System.Text.StringBuilder("[");
                bool first = true;
                foreach (var key in keys)
                {
                    if (store.TryFetchCache(key, out string value))
                    {
                        if (!first) sb.Append(',');
                        first = false;

                        var w = new JsonWriter(256);
                        w.Prop("key", key);
                        w.Prop("valuePreview", Truncate(value ?? "", 120));
                        sb.Append(w.Close());
                    }
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.CacheMcp] list_kv_cache failed: {e.Message}");
                return "[]";
            }
        }

        // ================================================================
        // 删除
        // ================================================================

        /// <summary>
        /// 删除指定 key 的缓存项。
        /// </summary>
        [McpTool(Name = "delete_kv_cache",
                 Description = "删除指定 key 的缓存项。无论 key 是否存在均返回成功。")]
        public static string DeleteKvCache(
            [McpParam(Description = "要删除的缓存 key")] string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return "{\"hit\":false,\"error\":\"key is required\"}";

                var store = RimLifeCore.CacheStore;
                if (store == null)
                    return "{\"hit\":false,\"error\":\"CacheStore unavailable\"}";

                store.ClearCache(key);

                var w = new JsonWriter(64);
                w.Prop("hit", true);
                w.Prop("key", key);
                return w.Close();
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.CacheMcp] delete_kv_cache({key}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static string MakeHit(string key, string value)
        {
            var w = new JsonWriter(1024);
            w.Prop("hit", true);
            w.Prop("key", key);
            w.Prop("value", value ?? "");
            return w.Close();
        }

        private static string MakeMiss(string query)
        {
            var w = new JsonWriter(128);
            w.Prop("hit", false);
            w.Prop("query", query ?? "");
            return w.Close();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
        }
    }
}
