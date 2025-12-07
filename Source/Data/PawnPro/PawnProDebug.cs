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
    /// Debug helper: adds a dev-mode gizmo on selected Pawn to dump a formatted PawnPro snapshot to the log.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnProDebug
    {
        static PawnProDebug()
        {
            // Ensure Harmony patch for gizmo injection is applied.
            var harmony = new Harmony("RimLife.PawnProDebug");
            harmony.PatchAll();
        }

        /// <summary>
        /// Returns gizmos (dev mode only) for dumping PawnPro data.
        /// </summary>
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break; // Only show in developer mode.

            yield return new Command_Action
            {
                defaultLabel = "PawnPro Dump",
                defaultDesc = "Print a structured PawnPro snapshot for this pawn to the game log (Dev Mode).",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false), // Reuse a vanilla icon; optional.
                action = () => DumpPawnPro(pawn)
            };
        }

        private static void DumpPawnPro(Pawn pawn)
        {
            try
            {
                var pp = new PawnPro(pawn);
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[PawnPro Dump] {pp.FullName} ({pp.ID}) | Type={pp.PawnType} Faction={pp.FactionLabel}");
                sb.AppendLine($"Age={pp.AgeBiologicalYears:0.0} Gender={pp.Gender} Dead={pp.IsDead} Downed={pp.IsDowned} Awake={pp.IsAwake}");
                sb.AppendLine();

                // --- Health ---
                AppendHealth(pp, sb);
                // --- Needs ---
                AppendNeeds(pp, sb);
                // --- Mood (only if humanlike) ---
                AppendMood(pp, sb);
                // --- Activity ---
                AppendActivity(pp, sb);
                // --- Perspective ---
                AppendPerspective(pp, sb);
                // --- Skills ---
                AppendSkills(pp, sb);
                // --- Gear ---
                AppendGear(pp, sb);
                // --- Backstory ---
                AppendBackstory(pp, sb);

                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnPro Debug] Failed to dump pawn: {ex}");
            }
        }

        private static void AppendHealth(PawnPro pp, StringBuilder sb)
        {
            var h = pp.Health;
            sb.AppendLine("== Health ==");
            sb.AppendLine($"Pain={h.SummaryPain:0.00} BleedRate={h.SummaryBleedRate:0.000}");
            if (h.Capacities != null && h.Capacities.Count > 0)
            {
                sb.AppendLine("Capacities:" + string.Join("; ", h.Capacities.Select(c => $"{c.Key}:{c.Value:0.00}")));
            }
            if (h.Injuries != null && h.Injuries.Count > 0)
            {
                // Group by GroupTag for readability, show top8 entries overall.
                var grouped = h.Injuries.GroupBy(i => i.GroupTag)
                .Select(g => $"{g.Key}[{g.Count()}]=" + string.Join(", ", g.Select(i => $"{(i.Label)}({(i.Part)}:{i.Severity:0.00}{(i.IsBleeding ? "*" : "")}{(i.IsPermanent ? "!" : "")})")))
                .ToList();
                sb.AppendLine("Injuries:" + string.Join(" | ", grouped));
            }
            else sb.AppendLine("Injuries: (none)");
            sb.AppendLine();
        }

        private static void AppendNeeds(PawnPro pp, StringBuilder sb)
        {
            var n = pp.Needs;
            sb.AppendLine("== Needs ==");
            if (n.AllNeeds != null && n.AllNeeds.Count > 0)
            {
                // Sort by level ascending; show top critical first.
                var ordered = n.AllNeeds.OrderBy(x => x.CurLevel)
                .Select(x => $"{(x.Label)}:{x.CurLevel:0.00}{(x.IsCritical ? "!" : "")}");
                sb.AppendLine(string.Join(" | ", ordered));
            }
            else sb.AppendLine("(none)");
            sb.AppendLine();
        }

        private static void AppendMood(PawnPro pp, StringBuilder sb)
        {
            var m = pp.Mood; // may be null for non-humanlike
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

        private static void AppendActivity(PawnPro pp, StringBuilder sb)
        {
            var a = pp.Activity;
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

        private static void AppendPerspective(PawnPro pp, StringBuilder sb)
        {
            var p = pp.Perspective;
            sb.AppendLine("== Perspective ==");
            if (p.VisiblePawnSnapshots != null && p.VisiblePawnSnapshots.Count > 0)
            {
                var recognizables = p.GetRecognizablePawnIDs();
                var unrec = p.GetUnrecognizableSpeciesCounts();
                sb.AppendLine("Recognizable:" + (recognizables.Any() ? string.Join(", ", recognizables) : "(none)"));
                sb.AppendLine("Unrecognizable:" + (unrec.Any() ? string.Join(", ", unrec) : "(none)"));
            }
            else sb.AppendLine("(no visible pawns)");
            sb.AppendLine();
        }

        private static void AppendSkills(PawnPro pp, StringBuilder sb)
        {
            var s = pp.Skills;
            sb.AppendLine("== Skills ==");
            if (s.AllSkills != null && s.AllSkills.Count > 0)
            {
                var skills = s.AllSkills.Select(x => $"{(x.Label)}:{x.Level}({x.Passion}{(x.TotallyDisabled ? ",Disabled" : "")})");
                sb.AppendLine(string.Join(" | ", skills));
            }
            else sb.AppendLine("(none)");
            sb.AppendLine();
        }

        private static void AppendGear(PawnPro pp, StringBuilder sb)
        {
            var g = pp.Gear;
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

        private static void AppendBackstory(PawnPro pp, StringBuilder sb)
        {
            var b = pp.Backstory;
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

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > 28 ? s.Substring(0, 25) + "..." : s;
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
                // Only for selected pawns (avoid clutter) and dev mode.
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
