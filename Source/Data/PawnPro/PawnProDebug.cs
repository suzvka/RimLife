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
                var card = CharacterCardMapper.CreateFull(pawn);
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[CharacterCard Dump] {card.FullName} ({card.ID}) | Type={card.PawnType} Faction={card.FactionLabel}");
                sb.AppendLine($"Age={card.AgeBiologicalYears:0.0} Gender={card.Gender} Dead={card.IsDead} Downed={card.IsDowned} Awake={card.IsAwake}");
                sb.AppendLine();

                // --- Health ---
                AppendHealth(card, sb);
                // --- Needs ---
                AppendNeeds(card, sb);
                // --- Mood (only if humanlike) ---
                AppendMood(card, sb);
                // --- Activity ---
                AppendActivity(card, sb);
                // --- Perspective ---
                AppendPerspective(card, sb);
                // --- Skills ---
                AppendSkills(card, sb);
                // --- Gear ---
                AppendGear(card, sb);
                // --- Backstory ---
                AppendBackstory(card, sb);

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnPro Debug] Failed to dump pawn: {ex}");
            }
        }

        private static void AppendHealth(CharacterCard card, StringBuilder sb)
        {
            var h = card.Health;
            if (h == null) { sb.AppendLine("== Health == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Health ==");
            sb.AppendLine($"Pain={h.SummaryPain:0.00} BleedRate={h.SummaryBleedRate:0.000}");
            if (h.Capacities != null && h.Capacities.Count > 0)
            {
                sb.AppendLine("Capacities:" + string.Join("; ", h.Capacities.Select(c => $"{c.Key}:{c.Value:0.00}")));
            }
            if (h.Injuries != null && h.Injuries.Count > 0)
            {
                var grouped = h.Injuries.Select(i => $"{(i.Label)}({(i.Part)}:{i.Severity:0.00}{(i.IsBleeding ? "*" : "")}{(i.IsPermanent ? "!" : "")})")
                .ToList();
                sb.AppendLine("Injuries:" + string.Join(", ", grouped));
            }
            else sb.AppendLine("Injuries: (none)");
            sb.AppendLine();
        }

        private static void AppendNeeds(CharacterCard card, StringBuilder sb)
        {
            var n = card.Needs;
            if (n == null) { sb.AppendLine("== Needs == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Needs ==");
            if (n.AllNeeds != null && n.AllNeeds.Count > 0)
            {
                var ordered = n.AllNeeds.OrderBy(x => x.CurLevel)
                .Select(x => $"{(x.Label)}:{x.CurLevel:0.00}{(x.IsCritical ? "!" : "")}");
                sb.AppendLine(string.Join(" | ", ordered));
            }
            else sb.AppendLine("(none)");
            sb.AppendLine();
        }

        private static void AppendMood(CharacterCard card, StringBuilder sb)
        {
            var m = card.Mood; // null for non-humanlike
            sb.AppendLine("== Mood ==");
            if (m == null)
            {
                sb.AppendLine("(not applicable)");
                sb.AppendLine();
                return;
            }
            sb.AppendLine($"Mood={m.MoodLevel:0.00} MentalState={(m.MentalStateLabel ?? "Normal")}");
            if (m.Traits != null && m.Traits.Count > 0)
            {
                sb.AppendLine("Traits:" + string.Join("; ", m.Traits.Select(t => $"{(t.Label)}({t.Degree})")));
            }
            if (m.ActiveThoughts != null && m.ActiveThoughts.Count > 0)
            {
                var topThoughts = m.ActiveThoughts.OrderByDescending(t => Mathf.Abs(t.MoodOffset))
                .Select(t => $"{(t.Label)}:{t.MoodOffset:+0.0;-0.0}({t.DurationRatio:0.00})");
                sb.AppendLine("Thoughts:" + string.Join(" | ", topThoughts));
            }
            sb.AppendLine();
        }

        private static void AppendActivity(CharacterCard card, StringBuilder sb)
        {
            var a = card.Activity;
            if (a == null) { sb.AppendLine("== Activity == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Activity ==");
            sb.AppendLine($"Posture={a.Posture ?? "-"}");
            if (a.Activities != null && a.Activities.Count > 0)
            {
                var acts = a.Activities.Select(x => $"{x.JobDefName}:{(x.JobReport)}");
                sb.AppendLine(string.Join(" | ", acts));
            }
            else sb.AppendLine("(no queued jobs)");
            sb.AppendLine();
        }

        private static void AppendPerspective(CharacterCard card, StringBuilder sb)
        {
            var p = card.Perspective;
            if (p == null) { sb.AppendLine("== Perspective == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Perspective ==");
            if (p.VisiblePawnSnapshots != null && p.VisiblePawnSnapshots.Count > 0)
            {
                const float recognizableRange = 13f;
                var recognizableIds = p.VisiblePawnSnapshots
                    .Where(ps => ps.Distance <= recognizableRange)
                    .Select(ps => ps.ID);
                var unrecognizable = p.VisiblePawnSnapshots
                    .Where(ps => ps.Distance > recognizableRange)
                    .GroupBy(ps => ps.DefName)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}:{g.Count()}");

                sb.AppendLine("Recognizable:" + (recognizableIds.Any() ? string.Join(", ", recognizableIds) : "(none)"));
                sb.AppendLine("Unrecognizable:" + (unrecognizable.Any() ? string.Join(", ", unrecognizable) : "(none)"));
            }
            else sb.AppendLine("(no visible pawns)");
            sb.AppendLine();
        }

        private static void AppendSkills(CharacterCard card, StringBuilder sb)
        {
            var s = card.Skills;
            if (s == null) { sb.AppendLine("== Skills == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Skills ==");
            if (s.AllSkills != null && s.AllSkills.Count > 0)
            {
                var skills = s.AllSkills.Select(x => $"{(x.Label)}:{x.Level}({x.Passion}{(x.TotallyDisabled ? ",Disabled" : "")})");
                sb.AppendLine(string.Join(" | ", skills));
            }
            else sb.AppendLine("(none)");
            sb.AppendLine();
        }

        private static void AppendGear(CharacterCard card, StringBuilder sb)
        {
            var g = card.Gear;
            if (g == null) { sb.AppendLine("== Gear == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Gear ==");
            if (g.WornGear != null && g.WornGear.Count > 0)
            {
                var worn = g.WornGear.Select(x => $"{(x.Name)}:Q={x.Quality} D={x.Durability:0.00} C={x.Count}");
                sb.AppendLine("Worn:" + string.Join(" | ", worn));
            }
            else sb.AppendLine("Worn: (none)");
            if (g.Inventory != null && g.Inventory.Count > 0)
            {
                var inv = g.Inventory.Select(x => $"{(x.Name)}:Q={x.Quality} D={x.Durability:0.00} C={x.Count}");
                sb.AppendLine("Inventory:" + string.Join(" | ", inv));
            }
            else sb.AppendLine("Inventory: (none)");
            sb.AppendLine();
        }

        private static void AppendBackstory(CharacterCard card, StringBuilder sb)
        {
            var b = card.Backstory;
            if (b == null) { sb.AppendLine("== Backstory == (not collected)"); sb.AppendLine(); return; }

            sb.AppendLine("== Backstory ==");
            if (b.Childhood.HasValue)
            {
                sb.AppendLine($"Childhood: {(b.Childhood.Value.Title)} - {(b.Childhood.Value.Description)}");
            }
            else
            {
                sb.AppendLine("Childhood: (none)");
            }
            if (b.Adulthood.HasValue)
            {
                sb.AppendLine($"Adulthood: {(b.Adulthood.Value.Title)} - {(b.Adulthood.Value.Description)}");
            }
            else
            {
                sb.AppendLine("Adulthood: (none)");
            }
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
