using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimLife.Cards;
using RimLife.Mappers;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Debug helper: adds a dev-mode gizmo on selected Pawn to dump a formatted EnvironmentCard snapshot to the log.
    /// </summary>
    internal static class EnvironmentProDebug
    {
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "EnvironmentPro Dump",
                defaultDesc = "Print a structured EnvironmentCard snapshot for this pawn to the game log (Dev Mode).",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpEnvironmentCard(pawn)
            };
        }

        private static void DumpEnvironmentCard(Pawn pawn)
        {
            try
            {
                var ec = EnvironmentCardMapper.CreateFrom(pawn);
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[EnvironmentCard Dump] Pawn={pawn.LabelShortCap} ID={pawn.ThingID}");
                sb.AppendLine($"Type={ec.Type} Temp={ec.Temperature:0.0} Light={ec.LightLevel:0.00}");

                // Weather
                if (ec.Weather.Label != null || ec.Weather.Description != null)
                {
                    sb.AppendLine("== Weather ==");
                    sb.AppendLine($"{ec.Weather.Label} | {ec.Weather.Description}");
                    sb.AppendLine($"Rain={ec.Weather.IsRain} Snow={ec.Weather.IsSnow} Wind={ec.Weather.WindSpeed:0.00}");
                }

                // ThingSummary
                sb.AppendLine("== ThingSummary ==");
                if (ec.ThingSummary != null && ec.ThingSummary.Count > 0)
                {
                    foreach (var kvp in ec.ThingSummary)
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
