using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife
{
 /// <summary>
 /// 人格叙事调试：在 DevMode 为已选 Pawn 提供 gizmo，打印人格五维聚合明细。
 /// </summary>
 [StaticConstructorOnStartup]
 internal static class NarratiiveDebug
 {
 static NarratiiveDebug()
 {
 var harmony = new Harmony("RimLife.NarratiiveDebug");
 harmony.PatchAll();
 }

 /// <summary>
 /// 返回用于人格调试的 Gizmos（仅开发者模式）。
 /// </summary>
 public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
 {
 if (pawn == null) yield break;
 if (!Prefs.DevMode) yield break;

 yield return new Command_Action
 {
 defaultLabel = "Personality Dump",
 defaultDesc = "Dump Big Five personality aggregation (traits + externals).",
 icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
 action = () => DumpPersonality(pawn)
 };
 }

 private static void DumpPersonality(Pawn pawn)
 {
 try
 {
 var pp = new PawnPro(pawn);
 var narrative = new PersonalityNarrative(pp);
 var sb = new StringBuilder(2048);
 sb.AppendLine($"[Personality Dump] {pp.FullName} ({pp.ID})");
 sb.AppendLine();

 // 基础向量
 sb.AppendLine($"BaseVector: {narrative.BaseVector}");
 // 外部向量（目前可能为空）
 if (narrative.ExternalVectors.Count >0)
 {
 sb.AppendLine("ExternalVectors:" + string.Join(" | ", narrative.ExternalVectors.Select(kv => kv.Key + ":" + kv.Value.ToString())));
 }
 else sb.AppendLine("ExternalVectors: (none)");

 var total = narrative.GetTotalVector();
 sb.AppendLine($"TotalVector: {total}");
 sb.AppendLine();

 sb.AppendLine("Axis Scores:" +
 $" O={narrative.OpennessScore}({narrative.Openness})" +
 $" C={narrative.ConscientiousnessScore}({narrative.Conscientiousness})" +
 $" E={narrative.ExtraversionScore}({narrative.Extraversion})" +
 $" A={narrative.AgreeablenessScore}({narrative.Agreeableness})" +
 $" N={narrative.NeuroticismScore}({narrative.Neuroticism})");
 sb.AppendLine();

 // Trait贡献明细
 if (narrative.TraitContributions.Count >0)
 {
 sb.AppendLine("Trait Contributions:");
 foreach (var c in narrative.TraitContributions)
 {
 sb.AppendLine(" - " + c);
 }
 }
 else sb.AppendLine("Trait Contributions: (none)");

 Log.Message(sb.ToString());
 }
 catch (Exception ex)
 {
 Log.Error("[Narrative Debug] Failed to dump personality: " + ex);
 }
 }
 }

 /// <summary>
 /// Harmony Patch：向 Pawn.GetGizmos 注入人格调试按钮。
 /// </summary>
 [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
 internal static class Pawn_GetGizmos_NarratiiveDebugPatch
 {
 static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
 {
 try
 {
 if (__instance == null) return;
 if (!Prefs.DevMode) return;
 if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

 var list = __result.ToList();
 list.AddRange(NarratiiveDebug.GetDebugGizmos(__instance));
 __result = list;
 }
 catch (Exception e)
 {
 Log.Warning("[Narrative Debug] Gizmo injection failed: " + e.Message);
 }
 }
 }
}
