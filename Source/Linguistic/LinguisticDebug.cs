using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Linguistic 层调试：
    /// 在 DevMode 为已选 Pawn 提供 gizmo，调用 TextGenerator 生成一句/多句台词，
    /// 并将结果以结构化格式打印到日志。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class LinguisticDebug
    {
        static LinguisticDebug()
        {
            // 确保 Harmony patch（下面的 Pawn_GetGizmos_LinguisticDebugPatch）被应用。
            var harmony = new Harmony("RimLife.LinguisticDebug");
            harmony.PatchAll();
        }

        /// <summary>
        /// 为指定 Pawn 提供一个或多个调试用 gizmo。
        /// 当前只提供一个“Linguistic Health Test”按钮。
        /// </summary>
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break; // 仅在 Dev 模式显示

            yield return new Command_Action
            {
                defaultLabel = "Linguistic Health Test",
                defaultDesc = "Run the Linguistic/TextGenerator pipeline for this pawn's health " +
                              "and print the generated sentences to the log (Dev Mode).",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => RunHealthTest(pawn)
            };

            // 将来如果你想测试其它话题（心情 / 环境），可以在这里再加更多按钮。
        }

        private static void RunHealthTest(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                var pawnSnapshot = new PawnPro(pawn);
                var facts = BuildHealthFacts(pawnSnapshot);
                var templates = GetTemplatesForTopic(FactTopic.Health);
                var templateEngine = new TemplateEngine(new SlotRealizer());

                var sentencesByFact = new Dictionary<Fact, List<SentenceDebugInfo>>();
                foreach (var fact in facts)
                {
                    var bucket = new List<SentenceDebugInfo>();
                    sentencesByFact[fact] = bucket;

                    foreach (var template in templates)
                    {
                        if (!TemplateCompatibleWithFact(template, fact))
                            continue;

                        var plan = new SentencePlan(fact, template, 1f);
                        var realizedText = templateEngine.Realize(plan);
                        bucket.Add(new SentenceDebugInfo(template.defName, template.function, template.syntacticType, realizedText));
                    }
                }

                int totalSentences = sentencesByFact.Values.Sum(list => list.Count);
                int initialCapacity = Mathf.Max(1024, totalSentences * 64);
                var sb = new StringBuilder(initialCapacity);
                sb.AppendLine($"[Linguistic Debug] Pawn={pawnSnapshot.FullName} ({pawnSnapshot.ID}) Language={LanguageDatabase.activeLanguage?.LegacyFolderName ?? "unknown"}");
                sb.AppendLine($"Facts={facts.Count}, Templates={templates.Count}, Sentences={totalSentences}");

                if (facts.Count == 0)
                {
                    sb.Append("No health facts were derived, so no sentences could be realized.");
                }
                else
                {
                    for (int i = 0; i < facts.Count; i++)
                    {
                        var fact = facts[i];
                        var tags = fact.Tags?.Tags != null && fact.Tags.Tags.Any()
                            ? string.Join(", ", fact.Tags.Tags.Select(t => t.Value))
                            : "<no tags>";

                        sb.AppendLine($"Fact[{i}] Topic={fact.Topic} Subtopic={fact.Subtopic} Salience={fact.Salience:F2} Tags=[{tags}]");

                        if (!sentencesByFact.TryGetValue(fact, out var realized) || realized.Count == 0)
                        {
                            sb.AppendLine("  • (no matching templates)");
                            continue;
                        }

                        foreach (var sentence in realized)
                        {
                            sb.AppendLine($"  • {sentence.Template} ({sentence.Function}/{sentence.SyntacticType}): {sentence.Text}");
                        }
                    }
                }

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("[Linguistic Debug] Health test failed: " + ex);
            }
        }

        private static List<Fact> BuildHealthFacts(PawnPro pawn)
        {
            var result = new List<Fact>();
            if (pawn == null)
                return result;

            var healthInfo = pawn.Health;
            if (healthInfo == null)
                return result;

            var narratives = HealthNarrative.From(healthInfo);
            for (int i = 0; i < narratives.Count; i++)
            {
                var narrative = narratives[i];
                if (narrative == null)
                    continue;

                float salience = EstimateHealthSalience(narrative, i);
                result.Add(HealthTagBuilder.BuildHealthFact(pawn, narrative, salience));
            }

            return result;
        }

        private static float EstimateHealthSalience(HealthNarrative narrative, int orderIndex)
        {
            if (narrative == null)
                return 0.1f;

            float severity = narrative.Severity switch
            {
                SeverityAdj.Trivial => 0.2f,
                SeverityAdj.Minor => 0.35f,
                SeverityAdj.Moderate => 0.6f,
                SeverityAdj.Severe => 0.8f,
                SeverityAdj.Critical => 0.95f,
                _ => 0.3f
            };

            if (narrative.IsBleeding) severity += 0.1f;
            if (narrative.IsInfection) severity += 0.05f;
            if (narrative.IsPermanent) severity += 0.05f;

            severity += narrative.Tending switch
            {
                TendingAdj.WellTended => -0.05f,
                TendingAdj.SkillfullyTended => -0.08f,
                TendingAdj.PerfectlyTended => -0.1f,
                _ => 0f
            };

            severity -= orderIndex * 0.03f;
            return Mathf.Clamp01(severity);
        }

        private static List<NarrativeTemplateDef> GetTemplatesForTopic(FactTopic topic)
        {
            string topicName = topic.ToString();
            return DefDatabase<NarrativeTemplateDef>.AllDefsListForReading
                .Where(def => string.IsNullOrWhiteSpace(def.topic) ||
                              string.Equals(def.topic, topicName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(def => def.defName)
                .ToList();
        }

        private static bool TemplateCompatibleWithFact(NarrativeTemplateDef template, Fact fact)
        {
            if (template == null || fact == null)
                return false;

            if (!string.IsNullOrWhiteSpace(template.topic))
            {
                var topicName = fact.Topic.ToString();
                if (!string.Equals(template.topic, topicName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (template.requiredTags != null && template.requiredTags.Count > 0)
            {
                foreach (var tagStr in template.requiredTags)
                {
                    if (string.IsNullOrWhiteSpace(tagStr))
                        continue;

                    if (!fact.Tags.Contains(new TagId(tagStr)))
                        return false;
                }
            }

            return true;
        }

        private readonly struct SentenceDebugInfo
        {
            public SentenceDebugInfo(string template, DiscourseFunction function, SyntacticType syntacticType, string text)
            {
                Template = template;
                Function = function;
                SyntacticType = syntacticType;
                Text = text;
            }

            public string Template { get; }
            public DiscourseFunction Function { get; }
            public SyntacticType SyntacticType { get; }
            public string Text { get; }
        }

        private static void LogFacts(IReadOnlyList<Fact> facts)
        {
            Log.Message($"[Linguistic Debug] Derived {facts.Count} health facts.");
            for (int i = 0; i < Math.Min(5, facts.Count); i++)
            {
                var fact = facts[i];
                var tags = string.Join(", ", fact.Tags.Tags.Select(t => t.Value));
                var intensity = fact.Semantics?.GetAxis(SemanticAxisId.Intensity);
                var risk = fact.Semantics?.GetAxis(SemanticAxisId.Risk);

                Log.Message(
                    $"[Linguistic Debug] Fact[{i}] Topic={fact.Topic} Subtopic={fact.Subtopic} Salience={fact.Salience:F2} Tags=[{tags}] Intensity={intensity?.ToString("F2") ?? "-"} Risk={risk?.ToString("F2") ?? "-"}");
            }
        }

        private static void LogConstraintDiagnostics(
            SentenceConstraint constraint,
            IReadOnlyList<Fact> facts,
            IReadOnlyList<NarrativeTemplateDef> templates)
        {
            var requiredTags = constraint.RequiredFactTags.Count > 0
                ? string.Join(", ", constraint.RequiredFactTags)
                : "<none>";

            Log.Message(
                $"[Linguistic Debug] Constraint -> Topic={constraint.Topic}, FunctionHint={constraint.FunctionHint}, SyntacticTypeHint={constraint.SyntacticTypeHint}, RequiredTags=[{requiredTags}]");

            var relevantTemplates = templates
                .Where(t => string.Equals(t.topic, constraint.Topic.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (relevantTemplates.Count == 0)
            {
                Log.Message("[Linguistic Debug] No templates share the requested topic.");
                return;
            }

            Log.Message($"[Linguistic Debug] Found {relevantTemplates.Count} templates for topic {constraint.Topic}.");
            foreach (var template in relevantTemplates)
            {
                var req = template.requiredTags != null && template.requiredTags.Count > 0
                    ? string.Join(", ", template.requiredTags)
                    : "<none>";
                Log.Message(
                    $"[Linguistic Debug] Template {template.defName}: function={template.function}, syntacticType={template.syntacticType}, requiredTags=[{req}] baseWeight={template.baseWeight:F2}");
            }
        }
    }

    /// <summary>
    /// Harmony patch：在 Pawn.GetGizmos 的结果里插入 Linguistic 的调试 gizmo。
    /// 模式与 PawnProDebug / NarratiiveDebug 保持一致。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_LinguisticDebugPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(LinguisticDebug.GetDebugGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning("[Linguistic Debug] Gizmo injection failed: " + e.Message);
            }
        }
    }
}
