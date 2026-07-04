using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NPCLife.Core;
using RimWorld;
using Verse;

namespace RimLife.Infrastructure.Knowledge
{
    /// <summary>
    /// GameDef 知识源（只读外部源）。通过注册制从任意 RimWorld Def 数据库查询官方定义。
    /// 核心 static 构造注册原版 Def 类型，DLC 可通过 Register&lt;T&gt;() 在 Data 层追加。
    /// 接入 KnowledgeService 的外部源列表，作为 GameDef 只读知识来源。
    /// </summary>
    public class GameDefKnowledgeBase : IExternalKnowledgeSource
    {
        /// <summary>解析器注册表。核心注册原版类型，DLC 通过 Register&lt;T&gt; 追加。</summary>
        internal static readonly List<DefResolver> Resolvers = new List<DefResolver>();

        /// <summary>
        /// 反向索引：被引用 defName → 所有引用它的 defName 集合。
        /// 索引标签即 RimWorld description 中的 [DefName] 超链接。
        /// </summary>
        private static Dictionary<string, HashSet<string>> _reverseIndex;
        private static readonly object _indexLock = new object();

        /// <summary>中文预览截断长度。</summary>
        private const int PreviewChars = 30;
        
        /// <summary>模糊匹配的最小包含度阈值（0-1）。</summary>
        private const float FuzzyMatchThreshold = 0.6f;

        static GameDefKnowledgeBase()
        {
            // ================================================================
            // 原版 Def 类型注册 — 各 DLC 通过 Register<T>() 追加，无需修改此文件
            // ================================================================

            // ThingDef: 物品/建筑/武器/服装/食物等
            Register<ThingDef>();

            // PawnKindDef: 生物/人形/动物种类
            Register<PawnKindDef>();

            // XenotypeDef: 异种人定义（尼人种/魔人/高角人等，Biotech DLC）
            Register<XenotypeDef>();

            // GeneDef: 基因定义（Biotech DLC）
            Register<GeneDef>();

            // HediffDef: 健康状态（疾病/伤口/植入物/成瘾等）
            Register<HediffDef>();

            // BodyPartDef: 身体部位（躯干/腿/手臂等）
            Register<BodyPartDef>();

            // DamageDef: 伤害类型（割伤/烧伤/瘀伤等）
            Register<DamageDef>();

            // TraitDef: 性格特征
            Register<TraitDef>();

            // MentalStateDef: 精神状态（暴怒/悲伤徘徊/纵火狂等）
            Register<MentalStateDef>();

            // ThoughtDef: 想法/记忆定义（心情影响因素，如"吃了没有桌子的饭"）
            Register<ThoughtDef>();

            // NeedDef: 需求定义（心情/饮食/休息/娱乐等）
            Register<NeedDef>();

            // RecipeDef: 配方/工作台工序
            Register<RecipeDef>();

            // ResearchProjectDef: 科技研究项目
            Register<ResearchProjectDef>();

            // FactionDef: 派系定义
            Register<FactionDef>();

            // IncidentDef: 事件定义（袭击/寒流/心灵冲击等）
            Register<IncidentDef>();

            // WeatherDef: 天气定义
            Register<WeatherDef>();

            // BiomeDef: 生态群系定义
            Register<BiomeDef>();
        }

        /// <summary>
        /// 注册一种 Def 类型到查询链。DLC 在 Data 层通过 [StaticConstructorOnStartup] 调用此方法追加。
        /// 重复注册同一类型名会被静默忽略。
        /// </summary>
        /// <typeparam name="T">Def 子类型，必须是 Verse.Def 的子类</typeparam>
        public static void Register<T>() where T : Def
        {
            var typeName = typeof(T).Name;

            // 防重复注册
            if (Resolvers.Any(r => r.TypeName == typeName))
                return;

            Resolvers.Add(new DefResolver
            {
                TypeName = typeName,
                Lookup = term => DefDatabase<T>.GetNamedSilentFail(term),
                AllDefs = () => DefDatabase<T>.AllDefsListForReading.Cast<Def>()
            });

            Log.Message($"[RimLife.Knowledge] Registered Def resolver: {typeName}");
        }

        // ================================================================
        // IExternalKnowledgeSource
        // ================================================================

        /// <summary>知识来源名称，用于标注查询结果的出处。</summary>
        public string SourceName => "GameDef";

        /// <summary>
        /// 分层渐进搜索策略：
        ///   1. defName 精确匹配 → 返回完整详情
        ///   2. label 精确匹配 → 返回完整详情
        ///   3. label 模糊匹配（包含度 >= 60%）→ 返回完整详情
        ///   4. 索引标签命中（[DefName] 超链接反向引用）→ 每个词条截取前 30 字符预览
        ///   5. description 包含（仅在前 4 层均无结果时触发）→ 仅列词条名，不提供详情
        /// 
        /// 模糊匹配说明：Layer 3 使用基于最大连续字符数的包含度算法，阈值 60%，
        /// 相比简单的字符串包含匹配，能容忍部分拼写错误或缩写。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> QueryExact(string term)
        {
            if (string.IsNullOrEmpty(term))
                return Array.Empty<KnowledgeEntry>();

            var results = new List<KnowledgeEntry>();
            var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // === Layer 1-3: defName/label 匹配 → 完整详情 ===
            foreach (var resolver in Resolvers)
            {
                // Layer 1: defName 精确匹配 — O(1) DefDatabase 查找
                var def = resolver.Lookup(term);
                if (def != null)
                {
                    var entry = BuildEntry(def);
                    if (!string.IsNullOrEmpty(entry.Definition) && collected.Add(entry.Term))
                        results.Add(entry);
                }

                var allDefs = resolver.AllDefs();
                if (allDefs == null) continue;

                // Layer 2: label 精确匹配
                foreach (var d in allDefs)
                {
                    if (d == null || collected.Contains(d.defName)) continue;
                    string labelStr = d.label?.ToString();
                    if (labelStr != null && string.Equals(labelStr, term, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = BuildEntry(d);
                        if (!string.IsNullOrEmpty(entry.Definition) && collected.Add(entry.Term))
                            results.Add(entry);
                    }
                }

                // Layer 3: label 模糊匹配（扩大搜索范围）
                foreach (var d in allDefs)
                {
                    if (d == null || collected.Contains(d.defName)) continue;
                    string labelStr = d.label?.ToString();
                    if (labelStr != null && FuzzyMatch(labelStr, term))
                    {
                        var entry = BuildEntry(d);
                        if (!string.IsNullOrEmpty(entry.Definition) && collected.Add(entry.Term))
                            results.Add(entry);
                    }
                }
            }

            // === Layer 4: 索引标签命中 → 30 字符预览 ===
            EnsureReverseIndexBuilt();
            if (_reverseIndex != null && _reverseIndex.TryGetValue(term, out var referencers))
            {
                foreach (var defName in referencers)
                {
                    if (collected.Contains(defName)) continue;
                    string preview = ResolvePreview(defName);
                    if (preview == null) continue;
                    collected.Add(defName);
                    results.Add(new KnowledgeEntry
                    {
                        Term = ResolveLabel(defName) ?? defName,
                        Definition = preview.Length > PreviewChars ? preview.Substring(0, PreviewChars) : preview,
                        Source = SourceName
                    });
                }
            }

            // === Layer 5: description 包含（仅当前 4 层均无结果时触发）→ 仅列名 ===
            if (results.Count == 0)
            {
                foreach (var resolver in Resolvers)
                {
                    var allDefs = resolver.AllDefs();
                    if (allDefs == null) continue;

                    foreach (var d in allDefs)
                    {
                        if (d == null || collected.Contains(d.defName)) continue;
                        if (d.description == null) continue;
                        if (d.description.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        collected.Add(d.defName);
                        results.Add(new KnowledgeEntry
                        {
                            Term = DefLabel(d),
                            Definition = null,  // 仅列名，不提供详情
                            Source = SourceName
                        });
                    }
                }
            }

            return results;
        }

        // ================================================================
        // 内部：词条构建
        // ================================================================

        /// <summary>计算模糊匹配包含度。返回最大连续匹配字符数与搜索词长度的比值。</summary>
        private static bool FuzzyMatch(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return false;
            
            string textLower = text.ToLowerInvariant();
            string queryLower = query.ToLowerInvariant();
            
            // 如果包含完整字符串，直接匹配
            if (textLower.IndexOf(queryLower) >= 0) return true;
            
            // 计算最大连续匹配字符数
            int maxMatchLength = 0;
            for (int i = 0; i < textLower.Length; i++)
            {
                int matchLen = 0;
                for (int j = 0; j < queryLower.Length && i + j < textLower.Length; j++)
                {
                    if (textLower[i + j] == queryLower[j])
                        matchLen++;
                    else
                        break;
                }
                if (matchLen > maxMatchLength) maxMatchLength = matchLen;
            }
            
            // 计算包含度
            float containmentRatio = (float)maxMatchLength / queryLower.Length;
            return containmentRatio >= FuzzyMatchThreshold;
        }

        /// <summary>获取 Def 的本地化显示名，回落 defName。</summary>
        private static string DefLabel(Def def)
        {
            if (def == null) return "?";
            return (def.label?.ToString()) ?? def.defName ?? "?";
        }

        /// <summary>通过 defName 反查 Def 的本地化标签，失败返回 null。</summary>
        private static string ResolveLabel(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            foreach (var resolver in Resolvers)
            {
                var def = resolver.Lookup(defName);
                if (def != null)
                {
                    string label = def.label?.ToString();
                    if (!string.IsNullOrEmpty(label)) return label;
                    return defName;
                }
            }
            return null;
        }

        private static KnowledgeEntry BuildEntry(Def def)
        {
            string defDescription = def.description ?? "";

            return new KnowledgeEntry
            {
                Term = DefLabel(def),
                Definition = defDescription,
                Source = "GameDef"
            };
        }

        // ================================================================
        // 内部：反向索引（基于 description 中的 [DefName] 超链接）
        // ================================================================

        /// <summary>线程安全地构建反向索引。</summary>
        private static void EnsureReverseIndexBuilt()
        {
            if (_reverseIndex != null) return;
            lock (_indexLock)
            {
                if (_reverseIndex != null) return;
                _reverseIndex = BuildReverseIndex();
            }
        }

        private static Dictionary<string, HashSet<string>> BuildReverseIndex()
        {
            var index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var hyperlinkRegex = new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);

            foreach (var resolver in Resolvers)
            {
                var allDefs = resolver.AllDefs();
                if (allDefs == null) continue;

                foreach (var def in allDefs)
                {
                    if (def?.description == null) continue;

                    var matches = hyperlinkRegex.Matches(def.description);
                    foreach (Match match in matches)
                    {
                        var referenced = match.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(referenced)) continue;

                        // 将英文 defName 转为本地化标签作为索引 key，与中文搜索词匹配
                        string localizedKey = ResolveLabel(referenced) ?? referenced;

                        if (!index.TryGetValue(localizedKey, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            index[localizedKey] = set;
                        }
                        set.Add(def.defName);
                    }
                }
            }

            return index;
        }

        /// <summary>通过 defName 查找 Def 并返回其 description，供索引标签预览用。</summary>
        private static string ResolvePreview(string defName)
        {
            foreach (var resolver in Resolvers)
            {
                var def = resolver.Lookup(defName);
                if (def?.description != null)
                    return def.description;
            }
            return null;
        }

        // ================================================================
        // 解析器定义
        // ================================================================

        internal class DefResolver
        {
            /// <summary>Def 类型名，如 "ThingDef"、"PawnKindDef"。</summary>
            public string TypeName;

            /// <summary>defName 精确查找函数（O(1)）。</summary>
            public Func<string, Def> Lookup;

            /// <summary>该类型全部 Def 枚举（用于 label/description 模糊匹配）。</summary>
            public Func<IEnumerable<Def>> AllDefs;
        }
    }
}
