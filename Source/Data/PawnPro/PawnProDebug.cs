using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimLife.Cards;
using RimLife.Core;
using RimLife.Infrastructure;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Debug helper: adds a dev-mode gizmo on selected Pawn to dump a formatted CharacterCard snapshot to the log.
    /// </summary>
    public static class PawnProDebug
    {
        /// <summary>
        /// Returns gizmos (dev mode only) for dumping CharacterCard data.
        /// </summary>
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "PawnPro Dump",
                defaultDesc = "Print a structured CharacterCard snapshot for this pawn to the game log (Dev Mode).",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpCharacterCard(pawn)
            };
        }

        private static void DumpCharacterCard(Pawn pawn)
        {
            try
            {
                var card = Infrastructure.Mcp.PawnQueryHelper.BuildCharacterCard(pawn, null);
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[CharacterCard Dump] {card.FullName} ({card.ID}) | Type={card.PawnType} Faction={card.FactionLabel}");
                sb.AppendLine($"Age={card.AgeBiologicalYears:0.0} Gender={card.Gender} Dead={card.IsDead} Downed={card.IsDowned} Awake={card.IsAwake}");
                sb.AppendLine();

                var pp = RimLifeCore.PromptProvider;
                if (pp == null) { sb.AppendLine("(PromptProvider not available)"); Log.Message(sb.ToString()); return; }

                var fullPrompt = pp.GetCharacterPrompt(pawn.ThingID, "full");
                if (!string.IsNullOrEmpty(fullPrompt))
                {
                    // 按【】分隔符拆分，逐节打印
                    var sections = fullPrompt.Split('\n');
                    string currentSection = "";
                    foreach (var line in sections)
                    {
                        if (line.StartsWith("【"))
                        {
                            // 找到下一个【时，输出上一节
                            if (currentSection.Length > 0) sb.AppendLine(currentSection);
                            currentSection = line;
                        }
                        else
                        {
                            currentSection += " " + line;
                        }
                    }
                    if (currentSection.Length > 0) sb.AppendLine(currentSection);
                }
                else
                {
                    sb.AppendLine("(no prompt data)");
                }

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnPro Debug] Failed to dump pawn: {ex}");
            }
        }
    }

    /// <summary>
    /// Harmony patch injecting the debug gizmo into Pawn.GetGizmos.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_PawnProDebugPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(PawnProDebug.GetDebugGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning($"[PawnPro Debug] Gizmo injection failed: {e.Message}");
            }
        }
    }
}
