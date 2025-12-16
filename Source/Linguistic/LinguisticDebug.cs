using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Debug helper: 在选中 Pawn 上添加一个 Dev gizmo，
    /// 扫描所有 NarrativeTemplateDef，并用固定测试 Fact 填充一次，输出到日志。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimLifeTemplateDebug
    {
        private static readonly Regex SlotRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);

        static RimLifeTemplateDebug()
        {
            var harmony = new Harmony("RimLife.TemplateDebug");
            harmony.PatchAll();
        }

        /// <summary>
        /// 返回用于模板测试的 Debug Gizmo（仅 DevMode）。
        /// </summary>
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "RimLife: Template test",
                defaultDesc = "扫描当前所有 RimLife 模板，并用一个测试 Fact 填充一次，输出到日志。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => RunTemplateSmokeTest(pawn)
            };
        }

        private static void RunTemplateSmokeTest(Pawn pawn)
        {
            try
            {
                var pp = new PawnPro(pawn);
                var templates = DefDatabase<NarrativeTemplateDef>.AllDefsListForReading;
                var slotRealizer = new SlotRealizer();

                // 为测试构造一组 Fact 池（目前可以只重点测试 Health）
                var facts = BuildTestFacts(pp);

                var sb = new StringBuilder(4096);
                sb.AppendLine($"[RimLife Template Test] Pawn={pp.FullName} ({pp.ID})");
                sb.AppendLine($"Templates found: {templates.Count}");
                sb.AppendLine($"Facts in pool: {facts.Count}");
                sb.AppendLine(new string('-', 72));

                foreach (var def in templates)
                {
                    var fact = PickFactForTemplate(def, facts);
                    if (fact == null)
                    {
                        sb.AppendLine($"[Skip] Template={def.defName} (no matching Fact)");
                        sb.AppendLine();
                        continue;
                    }

                    var realized = RealizeTemplate(def, fact, slotRealizer);

                    sb.AppendLine($"Template: {def.defName}");
                    sb.AppendLine($"  topic={def.topic}  func={def.function}  syn={def.syntacticType}");
                    sb.AppendLine($"  fact.Topic={fact.Topic} Subtopic={fact.Subtopic} Tags=[{string.Join(", ", fact.Tags.Tags.Select(t => t.Value))}]");

                    // 下面这一行字段名，请对上你 NarrativeTemplateDef 里存模板字符串的字段
                    string templateText = def.GetTemplate(); // TODO: 如果你叫 line / text / pattern，请改这里

                    sb.AppendLine($"  Raw:    {templateText}");
                    sb.AppendLine($"  Filled: {realized}");
                    sb.AppendLine();
                }

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[RimLife Template Test] Failed: {ex}");
            }
        }

        /// <summary>
        /// 构造一批测试 Fact。
        /// 可以根据项目进度慢慢丰富，目前至少做出一个 Health Fact 就能跑通 Health 模板。
        /// </summary>
        private static List<Fact> BuildTestFacts(PawnPro pp)
        {
            var facts = new List<Fact>();

            // 1) Health：用一个“严重流血”的假伤口做测试
            var healthNarr = BuildFakeHealthNarrativeForTest(pp);
            if (healthNarr != null)
            {
                var healthFact = HealthTagBuilder.BuildHealthFact(
                    pawn: pp,
                    narrative: healthNarr,
                    salience: 1.0f);
                facts.Add(healthFact);
            }

            // 2) Mood / Environment / Relationship 等，可以先造一些极简的假 Fact，占位：
            facts.Add(BuildDummyFact(
                topic: FactTopic.Mood,
                subtopic: "MoodGeneral",
                pp: pp,
                salience: 0.8f,
                tags: new[] { "topic.mood", "polarity.negative", "intensity.mid" }
            ));

            facts.Add(BuildDummyFact(
                topic: FactTopic.Environment,
                subtopic: "RoomQuality",
                pp: pp,
                salience: 0.6f,
                tags: new[] { "topic.environment", "polarity.positive", "intensity.high" }
            ));

            return facts;
        }

        /// <summary>
        /// 构造一个“严重流血”的 HealthNarrative 用来测试。
        /// 这里字段名要对上你自己的 HealthNarrative 定义。
        /// 如果你已经有从 PawnPro.Health 生成 narrative 的辅助函数，也可以直接调用真实逻辑。
        /// </summary>
        private static HealthNarrative BuildFakeHealthNarrativeForTest(PawnPro pp)
        {
            // TODO: 把下面这些字段改成你 HealthNarrative 的实际构造方式
            try
            {
                var n = new HealthNarrative
                {
                    // 示例字段名：根据你的实际类型改
                    Severity = SeverityAdj.Severe,
                    Tending = TendingAdj.Untended,
                    IsBleeding = true,
                    Noun = null, // 交给 ResolveHealthInjuryNoun 去用翻译兜底
                    RelatedNouns = new Dictionary<string, string>
                    {
                        { "OnPart", "left leg" }
                    }
                };
                return n;
            }
            catch
            {
                // 如果 HealthNarrative 不能这样 new，就先返回 null，只测别的模板
                return null;
            }
        }

        /// <summary>
        /// 极简版 Dummy Fact：不依赖具体 Narrative，只用 Tag + 轴 + 核心词库测试模板填充。
        /// </summary>
        private static Fact BuildDummyFact(
            FactTopic topic,
            string subtopic,
            PawnPro pp,
            float salience,
            IEnumerable<string> tags)
        {
            var tagSet = TagSet.FromStrings(tags ?? Enumerable.Empty<string>());
            var axes = new List<NarrativeAxisValue>
            {
                // 简单给一个中等强度轴，方便 AdjIntensity/AdvDegree 取词
                new NarrativeAxisValue(NarrativeAxisId.Intensity, 0.5f)
            };
            var profile = new SemanticTemplate(tagSet, axes);

            // DomainLexemes 可以先空，让模板只依赖 Core Lexicon
            return new Fact(
                topic: topic,
                subtopic: subtopic,
                salience: salience,
                subject: pp,
                target: null,
                semantics: profile,
                domainLexemes: null,
                sourcePayload: null);
        }

        /// <summary>
        /// 为某个模板挑一个最合理的 Fact。
        /// 逻辑基本复用 LinguisticEngine.SelectBestFact 的思路，但这里简单一点。
        /// </summary>
        private static Fact PickFactForTemplate(NarrativeTemplateDef def, IReadOnlyList<Fact> facts)
        {
            if (facts == null || facts.Count == 0) return null;
            if (def == null) return null;

            IEnumerable<Fact> candidates = facts;

            // 1) 按 topic 过滤（NarrativeTemplateDef.topic 是 string，比如 "Health"）
            if (!string.IsNullOrEmpty(def.topic))
            {
                candidates = candidates.Where(f =>
                    string.Equals(def.topic, f.Topic.ToString(), StringComparison.OrdinalIgnoreCase));
            }

            // 2) 按 requiredTags 过滤
            if (def.requiredTags != null && def.requiredTags.Count > 0)
            {
                candidates = candidates.Where(f =>
                {
                    foreach (var tagStr in def.requiredTags)
                    {
                        if (string.IsNullOrWhiteSpace(tagStr)) continue;
                        var tag = new TagId(tagStr);
                        if (!f.Tags.Contains(tag))
                            return false;
                    }
                    return true;
                });
            }

            var list = candidates.ToList();
            if (list.Count == 0)
                return null;

            // 3) 简单按 Salience 排序取最高，也可以加一点随机
            return list.OrderByDescending(f => f.Salience).First();
        }

        /// <summary>
        /// 用 SlotRealizer 填充一个模板。
        /// 假设模板字符串里用 {SlotToken} 形式表示槽位，SlotRequest.TryParse 能从中解析出 SlotType + Name。
        /// </summary>
        private static string RealizeTemplate(
            NarrativeTemplateDef def,
            Fact fact,
            ISlotRealizer slotRealizer)
        {
            if (def == null || fact == null || slotRealizer == null)
                return string.Empty;

            // 这里字段名同样要对上你 NarrativeTemplateDef 的模板内容字段
            string templateText = def.GetTemplate(); // TODO: 如果叫 line/text/pattern，请改这里

            if (string.IsNullOrWhiteSpace(templateText))
                return string.Empty;

            string result = SlotRegex.Replace(templateText, match =>
            {
                var token = match.Groups[1].Value.Trim(); // 去掉花括号内部空白

                if (!Word.TryParse(token, out var slot))
                {
                    // 如果不是合法 Slot，就原样返回
                    return match.Value;
                }

                var value = slotRealizer.RealizeSlot(slot, fact);
                return value ?? string.Empty;
            });

            return result;
        }
    }

    /// <summary>
    /// Harmony patch：在 Pawn.GetGizmos 里注入 RimLife 模板测试 gizmo（仅 DevMode + 选中 pawn）。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_RimLifeTemplateDebugPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(RimLifeTemplateDebug.GetDebugGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife Template Debug] Gizmo injection failed: {e.Message}");
            }
        }
    }
}
