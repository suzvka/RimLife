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
    /// Debug helper: adds a dev-mode gizmo on selected Pawn to dump a formatted EnvironmentPro snapshot to the log.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class EnvironmentProDebug
    {
        static EnvironmentProDebug()
        {
            var harmony = new Harmony("RimLife.EnvironmentProDebug");
            harmony.PatchAll();
        }

        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "EnvironmentPro Dump",
                defaultDesc = "Print a structured EnvironmentPro snapshot for this pawn to the game log (Dev Mode).",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpEnvironmentPro(pawn)
            };
        }

        private static void DumpEnvironmentPro(Pawn pawn)
        {
            try
            {
                var ep = new EnvironmentPro(pawn);
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[EnvironmentPro Dump] Pawn={pawn.LabelShortCap} ID={pawn.ThingID}");
                sb.AppendLine($"Type={ep.Type} Temp={ep.Temperature:0.0} Light={ep.LightLevel:0.00}");

                // Room
                if (ep.Room != null)
                {
                    sb.AppendLine("== Room ==");
                    sb.AppendLine($"Role={ep.Room.RoleLabel}");
                    var rs = ep.Room.BaseStats;
                    sb.AppendLine($"Impressiveness={rs.Impressiveness:0.00} Beauty={rs.Beauty:0.00} Wealth={rs.Wealth:0.00} Space={rs.Space:0.00} Cleanliness={rs.Cleanliness:0.00}");
                }

                // Weather
                if (!string.IsNullOrEmpty(ep.Weather.Label) || !string.IsNullOrEmpty(ep.Weather.Description))
                {
                    sb.AppendLine("== Weather ==");
                    sb.AppendLine($"{ep.Weather.Label} | {ep.Weather.Description}");
                    sb.AppendLine($"Rain={ep.Weather.IsRain} Snow={ep.Weather.IsSnow} Wind={ep.Weather.WindSpeed:0.00}");
                }

                // Key features
                sb.AppendLine("== KeyFeatures ==");
                if (ep.KeyFeatures != null && ep.KeyFeatures.Count > 0)
                {
                    foreach (var f in ep.KeyFeatures)
                    {
                        sb.AppendLine($"[{f.CategoryTag}] {f.Label} ({f.DefName}) {f.Description}");
                    }
                }
                else sb.AppendLine("(none)");

                // ThingSummary
                sb.AppendLine("== ThingSummary ==");
                if (ep.ThingSummary != null && ep.ThingSummary.Count > 0)
                {
                    foreach (var kvp in ep.ThingSummary)
                    {
                        sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                    }
                }
                else sb.AppendLine("(none)");

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[EnvironmentPro Debug] Failed to dump environment: {ex}");
            }
        }
    }

    /// <summary>
    /// Harmony patch injecting the debug gizmo into Pawn.GetGizmos.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_EnvironmentProDebugPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(EnvironmentProDebug.GetDebugGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning($"[EnvironmentPro Debug] Gizmo injection failed: {e.Message}");
            }
        }
    }
}
