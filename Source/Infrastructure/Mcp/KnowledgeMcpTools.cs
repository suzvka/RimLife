using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 内置知识库的 MCP 工具集。提供词条查询、学习、列举和删除能力。
    /// 同时保留旧版 set_kv_cache / get_kv_cache / list_kv_cache / delete_kv_cache
    /// 作为兼容包装（内部转换为 KnowledgeEntry）。
    /// </summary>
    [McpSkill("knowledge_management")]
    public static class KnowledgeMcpTools
    {
        // ================================================================
        // 新版：知识库工具
        // ================================================================

        /// <summary>
        /// 分层查询词条释义。先查本地缓存，未命中可扩展至 GameDef / 外部源。
        /// </summary>
        [McpTool(Name = "lookup_term",
                 Description = "查询词条释义。先查内置知识库(L1)，未命中时可扩展至 GameDef(L2)或外部源(L3)。")]
        public static string LookupTerm(
            [McpParam(Description = "要查询的词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeBase unavailable\"}";

                if (kb.TryLookup(term, out var entry))
                {
                    return SerializeLookupHit(entry);
                }

                return MakeMiss(term);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] lookup_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 主动学习一个词条的释义并存储到知识库。
        /// </summary>
        [McpTool(Name = "learn_term",
                 Description = "将一个词条及其释义存储到内置知识库。若已存在则智能合并（保留更高信心度）。")]
        public static string LearnTerm(
            [McpParam(Description = "词条名")] string term,
            [McpParam(Description = "释义文本")] string definition,
            [McpParam(Description = "信心度 0.0~1.0，默认 0.8")] float confidence = 0.8f,
            [McpParam(Description = "来源：LLM / GameDef / AgentDeduction / Wiki，默认 LLM",
                      Required = McpRequired.False)] string source = "LLM",
            [McpParam(Description = "关联语义标签，逗号分隔，如 Combat,Faction,Lore",
                      Required = McpRequired.False)] string tags = null)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeBase unavailable\"}";

                KnowledgeSource src;
                if (!Enum.TryParse<KnowledgeSource>(source, true, out src))
                    src = KnowledgeSource.LLM;

                var entry = new KnowledgeEntry
                {
                    Term = term.Trim(),
                    Definition = definition ?? "",
                    Source = src,
                    Confidence = Math.Clamp(confidence, 0f, 1f),
                    ContextTags = ParseTagList(tags)
                };

                kb.Store(entry);

                // 回读确认
                if (kb.TryLookup(term, out var stored))
                    return SerializeLookupHit(stored);

                return "{\"hit\":false,\"error\":\"store failed\"}";
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] learn_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 列出已知词条，支持前缀和标签过滤。
        /// </summary>
        [McpTool(Name = "list_known_terms",
                 Description = "列出内置知识库中的已知词条摘要。支持前缀过滤和语义标签过滤。")]
        public static string ListKnownTerms(
            [McpParam(Description = "前缀过滤，如 '心灵'。留空=全部",
                      Required = McpRequired.False)] string prefix = null,
            [McpParam(Description = "语义标签过滤，逗号分隔，如 Combat,Lore。留空=不限",
                      Required = McpRequired.False)] string tags = null,
            [McpParam(Description = "最大返回数，默认 30")] int limit = 30)
        {
            try
            {
                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null) return "[]";

                IReadOnlyList<KnowledgeEntry> entries;

                if (!string.IsNullOrEmpty(tags))
                {
                    var tagList = ParseTagList(tags);
                    entries = kb.ListByTags(tagList);
                }
                else if (!string.IsNullOrEmpty(prefix))
                {
                    entries = kb.ListByPrefix(prefix);
                }
                else
                {
                    entries = kb.ListAll();
                }

                if (entries.Count > limit)
                    entries = entries.Take(limit).ToList();

                return SerializeTermSummaryList(entries);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] list_known_terms failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 删除指定词条。
        /// </summary>
        [McpTool(Name = "forget_term",
                 Description = "从内置知识库中删除指定词条。不存在时静默返回。")]
        public static string ForgetTerm(
            [McpParam(Description = "要删除的词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeBase unavailable\"}";

                kb.Delete(term);

                var w = new JsonWriter(64);
                w.Prop("hit", true);
                w.Prop("term", term);
                return w.Close();
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] forget_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 获取词条的元数据统计（信心度、访问次数、来源等）。
        /// </summary>
        [McpTool(Name = "get_term_stats",
                 Description = "获取指定词条的元数据：信心度、来源、访问次数、创建/上次访问时间。")]
        public static string GetTermStats(
            [McpParam(Description = "词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeBase unavailable\"}";

                if (kb.TryLookup(term, out var entry))
                {
                    return SerializeTermStats(entry);
                }

                return MakeMiss(term);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] get_term_stats({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 兼容包装：旧版 CacheMcpTools 工具（内部转为 KnowledgeEntry）
        // ================================================================

        /// <summary>
        /// [兼容] 写入字符串键值对到知识库。
        /// </summary>
        [McpTool(Name = "set_kv_cache",
                 Description = "[兼容] 写入字符串键值对到知识库。覆盖同名 key。建议用 learn_term 代替。")]
        public static string SetKvCache(
            [McpParam(Description = "缓存键。建议命名空间:值，如 lookup:心灵冲击")] string key,
            [McpParam(Description = "缓存值（字符串）")] string value)
        {
            return LearnTerm(key, value, 1.0f, "LegacyCache", null);
        }

        /// <summary>
        /// [兼容] 从知识库读取值。先精确匹配 key，未命中则降级分词匹配。
        /// </summary>
        [McpTool(Name = "get_kv_cache",
                 Description = "[兼容] 从知识库读取值。先精确匹配，未命中则分词匹配。建议用 lookup_term 代替。")]
        public static string GetKvCache(
            [McpParam(Description = "查询内容。先精确匹配，未命中则分词后查找包含所有 token 的 key")] string query)
        {
            try
            {
                if (string.IsNullOrEmpty(query))
                    return "{\"hit\":false,\"error\":\"query is required\"}";

                var kb = RimLifeCore.KnowledgeBase;
                if (kb == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeBase unavailable\"}";

                // 1. 精确匹配
                if (kb.TryLookup(query, out var entry))
                    return MakeHit(query, entry.Definition ?? "");

                // 2. 降级：分词匹配
                return TryGetToken(kb, query);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.KnowledgeMcp] get_kv_cache({query}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        private static string TryGetToken(IKnowledgeBase kb, string query)
        {
            var tokens = query.Split(new char[] { ' ', ',', '，' })
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (tokens.Count == 0) return MakeMiss(query);

            foreach (var entry in kb.ListAll())
            {
                if (tokens.All(t => entry.Term.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return MakeHit(entry.Term, entry.Definition ?? "");
                }
            }
            return MakeMiss(query);
        }

        /// <summary>
        /// [兼容] 列出所有缓存 key 及值摘要。
        /// </summary>
        [McpTool(Name = "list_kv_cache",
                 Description = "[兼容] 列出所有缓存 key 及值摘要。建议用 list_known_terms 代替。")]
        public static string ListKvCache(
            [McpParam(Description = "key 前缀过滤，如 'lookup:'。留空=全部",
                      Required = McpRequired.False)] string prefix = null)
        {
            return ListKnownTerms(prefix, null, 50);
        }

        /// <summary>
        /// [兼容] 删除指定 key 的缓存项。
        /// </summary>
        [McpTool(Name = "delete_kv_cache",
                 Description = "[兼容] 删除指定 key 的缓存项。建议用 forget_term 代替。")]
        public static string DeleteKvCache(
            [McpParam(Description = "要删除的缓存 key")] string key)
        {
            return ForgetTerm(key);
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeLookupHit(KnowledgeEntry entry)
        {
            var w = new JsonWriter(1024);
            w.Prop("hit", true);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definition", entry.Definition ?? "");
            w.Prop("source", entry.Source.ToString());
            w.Prop("confidence", entry.Confidence, "F2");
            w.Prop("accessCount", entry.AccessCount);
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
            return w.Close();
        }

        private static string SerializeTermSummaryList(IReadOnlyList<KnowledgeEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeTermSummary(entries[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string SerializeTermSummary(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definitionPreview", Truncate(entry.Definition ?? "", 120));
            w.Prop("source", entry.Source.ToString());
            w.Prop("confidence", entry.Confidence, "F2");
            w.Prop("accessCount", entry.AccessCount);
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
            return w.Close();
        }

        private static string SerializeTermStats(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("hit", true);
            w.Prop("term", entry.Term ?? "");
            w.Prop("source", entry.Source.ToString());
            w.Prop("confidence", entry.Confidence, "F3");
            w.Prop("createdSeq", entry.CreatedSeq);
            w.Prop("lastAccessedSeq", entry.LastAccessedSeq);
            w.Prop("accessCount", entry.AccessCount);
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
            return w.Close();
        }

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

        // ================================================================
        // 辅助
        // ================================================================

        private static List<string> ParseTagList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
        }
    }
}
