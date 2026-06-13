using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimLife.Cards;
using RimLife.Core;
using RimLife.Infrastructure;
using RimLife.Mappers;
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
                var card = CharacterCardMapper.CreateBasic(pawn);
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[CharacterCard Dump] {card.FullName} ({card.ID}) | Type={card.PawnType} Faction={card.FactionLabel}");
                sb.AppendLine($"Age={card.AgeBiologicalYears:0.0} Gender={card.Gender} Dead={card.IsDead} Downed={card.IsDowned} Awake={card.IsAwake}");
                sb.AppendLine();

                var pp = RimLifeCore.PromptProvider;
                if (pp == null) { sb.AppendLine("(PromptProvider not available)"); Log.Message(sb.ToString()); return; }

                AppendSectionPrompt(pp, pawn, "health", "Health", sb);
                AppendSectionPrompt(pp, pawn, "mood", "Mood", sb);
                AppendSectionPrompt(pp, pawn, "needs", "Needs", sb);
                AppendSectionPrompt(pp, pawn, "activity", "Activity", sb);
                AppendSectionPrompt(pp, pawn, "skills", "Skills", sb);
                AppendSectionPrompt(pp, pawn, "gear", "Gear", sb);
                AppendSectionPrompt(pp, pawn, "backstory", "Backstory", sb);
                AppendSectionPrompt(pp, pawn, "perspective", "Perspective", sb);
                AppendSectionPrompt(pp, pawn, "social", "Social", sb);
                AppendSectionPrompt(pp, pawn, "psychology", "Psychology", sb);
                AppendSectionPrompt(pp, pawn, "memory", "Memory", sb);

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnPro Debug] Failed to dump pawn: {ex}");
            }
        }

        private static void AppendSectionPrompt(IPawnPromptProvider pp, Pawn pawn,
            string sectionName, string label, StringBuilder sb)
        {
            var text = pp.GetSectionPrompt(pawn, sectionName);
            sb.AppendLine($"== {label} ==");
            sb.AppendLine(string.IsNullOrEmpty(text) ? "(not collected)" : text);
            sb.AppendLine();
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
