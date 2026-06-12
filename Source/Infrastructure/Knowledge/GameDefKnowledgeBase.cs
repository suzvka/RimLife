using System;
using System.Collections.Generic;
using System.Linq;
using RimLife.Core;
using RimWorld;
using Verse;

namespace RimLife.Infrastructure.Knowledge
{
    /// <summary>
    /// GameDef 知识库（L2 层）。通过注册制从任意 RimWorld Def 数据库查询官方定义。
    /// 核心 static 构造注册原版 Def 类型，DLC 可通过 Register&lt;T&gt;() 在 Data 层追加。
    /// 只读实现：Store/Delete 为 no-op，ListByPrefix/ListAll 不枚举全部 Def（量太大）。
    /// </summary>
    public class GameDefKnowledgeBase : IKnowledgeBase
    {
        /// <summary>解析器注册表。核心注册原版类型，DLC 通过 Register&lt;T&gt; 追加。</summary>
        internal static readonly List<DefResolver> Resolvers = new List<DefResolver>();

        static GameDefKnowledgeBase()
        {
            // ================================================================
            // 原版 Def 类型注册 — 各 DLC 通过 Register<T>() 追加，无需修改此文件
            // ================================================================

            // ThingDef: 物品/建筑/武器/服装/食物等，附带丰富语义标签
            Register<ThingDef>(BuildThingDefTags);

            // PawnKindDef: 生物/人形/动物种类
            Register<PawnKindDef>();

            // HediffDef: 健康状态（疾病/伤口/植入物/成瘾等）
            Register<HediffDef>();

            // TraitDef: 性格特征
            Register<TraitDef>();

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
        /// <param name="tagBuilder">可选语义标签构建器。为 null 时仅使用类型名（如 "PawnKindDef"）作为标签。</param>
        public static void Register<T>(Func<Def, List<string>> tagBuilder = null) where T : Def
        {
            var typeName = typeof(T).Name;

            // 防重复注册
            if (Resolvers.Any(r => r.TypeName == typeName))
                return;

            Resolvers.Add(new DefResolver
            {
                TypeName = typeName,
                Lookup = term => DefDatabase<T>.GetNamedSilentFail(term),
                AllDefs = () => DefDatabase<T>.AllDefsListForReading.Cast<Def>(),
                TagBuilder = tagBuilder
            });

            Log.Message($"[RimLife.Knowledge] Registered Def resolver: {typeName}");
        }

        // ================================================================
        // IKnowledgeBase
        // ================================================================

        /// <summary>
        /// 遍历所有已注册的 Def 类型解析器查询词条。
        /// 每个解析器内匹配优先级：defName 精确 → label 精确 → label 包含 → description 包含。
        /// </summary>
        public bool TryLookup(string term, out KnowledgeEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(term)) return false;

            foreach (var resolver in Resolvers)
            {
                // 1. defName 精确匹配（DefDatabase O(1) 查找）
                var def = resolver.Lookup(term);
                if (def != null)
                {
                    entry = BuildEntry(def, resolver);
                    if (!string.IsNullOrEmpty(entry.Definition))
                        return true;
                }

                var allDefs = resolver.AllDefs();
                if (allDefs == null) continue;

                // 收集 defName set 用于跳过已精确匹配的
                var matchedDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (def != null)
                    matchedDefNames.Add(def.defName);

                // 2. label 精确匹配
                foreach (var d in allDefs)
                {
                    if (d == null) continue;
                    if (matchedDefNames.Contains(d.defName)) continue;

                    string labelStr = (d.label != null) ? d.label.ToString() : null;
                    if (labelStr != null && string.Equals(labelStr, term, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = BuildEntry(d, resolver);
                        if (!string.IsNullOrEmpty(entry.Definition))
                            return true;

                        matchedDefNames.Add(d.defName);
                    }
                }

                // 3. label 包含匹配
                foreach (var d in allDefs)
                {
                    if (d == null) continue;
                    if (matchedDefNames.Contains(d.defName)) continue;

                    string labelStr = (d.label != null) ? d.label.ToString() : null;
                    if (labelStr != null && labelStr.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        entry = BuildEntry(d, resolver);
                        if (!string.IsNullOrEmpty(entry.Definition))
                            return true;

                        matchedDefNames.Add(d.defName);
                    }
                }

                // 4. description 包含匹配（兜底，最宽松）
                foreach (var d in allDefs)
                {
                    if (d == null) continue;
                    if (matchedDefNames.Contains(d.defName)) continue;

                    if (d.description != null
                        && d.description.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        entry = BuildEntry(d, resolver);
                        if (!string.IsNullOrEmpty(entry.Definition))
                            return true;

                        matchedDefNames.Add(d.defName);
                    }
                }
            }

            return false;
        }

        // ================================================================
        // 只读方法（no-op）
        // ================================================================

        public void Store(KnowledgeEntry entry) { /* no-op: 只读层 */ }
        public void Delete(string term) { /* no-op: 只读层 */ }
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
            => new List<KnowledgeEntry>();
        public IReadOnlyList<KnowledgeEntry> ListAll()
            => new List<KnowledgeEntry>();
        public int Count => 0;

        // ================================================================
        // 内部：词条构建
        // ================================================================

        private static KnowledgeEntry BuildEntry(Def def, DefResolver resolver)
        {
            string defLabel = (def.label != null) ? def.label.ToString() : string.Empty;
            string defDescription = def.description ?? "";

            List<string> tags;
            if (resolver.TagBuilder != null)
                tags = resolver.TagBuilder(def);
            else
                tags = new List<string> { resolver.TypeName };

            return new KnowledgeEntry
            {
                Term = def.defName ?? defLabel,
                Definition = defDescription,
                Source = KnowledgeSource.GameDef,
                Confidence = 1.0f,
                ContextTags = tags,
                CreatedSeq = 0,
                LastAccessedSeq = 0,
                AccessCount = 0
            };
        }

        /// <summary>
        /// ThingDef 专用语义标签构建器。从物品类别/用途推断标签。
        /// </summary>
        private static List<string> BuildThingDefTags(Def def)
        {
            var tags = new List<string> { "ThingDef" };
            var td = def as ThingDef;
            if (td == null) return tags;

            if (td.IsWeapon) tags.Add("Weapon");
            if (td.IsApparel) tags.Add("Apparel");
            if (td.IsMedicine) tags.Add("Medicine");
            if (td.IsDrug) tags.Add("Drug");
            if (td.IsCorpse) tags.Add("Corpse");
            if (td.IsRangedWeapon) tags.Add("Ranged");
            if (td.IsMeleeWeapon) tags.Add("Melee");

            if (td.IsIngestible && td.ingestible?.HumanEdible == true)
                tags.Add("Food");

            if (td.thingClass != null && typeof(Building).IsAssignableFrom(td.thingClass))
                tags.Add("Building");

            bool isBuilding = td.thingClass != null && typeof(Building).IsAssignableFrom(td.thingClass);
            bool isHumanlike = td.race?.Humanlike == true;
            if (!isBuilding && !td.IsCorpse && !isHumanlike)
                tags.Add("Item");

            return tags;
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

            /// <summary>可选的语义标签构建器。</summary>
            public Func<Def, List<string>> TagBuilder;
        }
    }
}
