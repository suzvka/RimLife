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
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false), // 复用一个现成图标即可
            };

            // 将来如果你想测试其它话题（心情 / 环境），可以在这里再加更多按钮。
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
