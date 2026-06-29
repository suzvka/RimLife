using HarmonyLib;
using NPCLife.Cards;
using NPCLife.Core;
using RimLife.Infrastructure;
using RimLife.Mappers;
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
    /// Debug helper: adds dev-mode gizmos for dumping new module data.
    /// </summary>
    internal static class ColonyDebug
    {
        public static IEnumerable<Gizmo> GetDebugGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            // SocialInfo dump (via PawnPro)
            yield return new Command_Action
            {
                defaultLabel = "SocialInfo Dump",
                defaultDesc = "Print social relations for this pawn.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpSocialInfo(pawn)
            };

            // ColonyContext dump (any pawn triggers it)
            yield return new Command_Action
            {
                defaultLabel = "ColonyContext Dump",
                defaultDesc = "Print colony-level context snapshot.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpColonyContext()
            };

            // EventLog dump
            yield return new Command_Action
            {
                defaultLabel = "EventLog Dump",
                defaultDesc = "Print recent events from log.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpEventLog()
            };

            // ObjectiveCard dump
            yield return new Command_Action
            {
                defaultLabel = "ObjectiveCard Dump",
                defaultDesc = "Print active objectives.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpObjectives()
            };
        }

        // --- SocialInfo ---
        private static void DumpSocialInfo(Pawn pawn)
        {
            try
            {
                var socialProvider = RimLifeCore.ContentProviders
                    .Find(p => p.SectionName == "social");
                var prompt = socialProvider?.GetContent(pawn.ThingID, "static");
                if (string.IsNullOrEmpty(prompt))
                {
                    Log.Message("[SocialInfo] (not collected)");
                    return;
                }

                var sb = new StringBuilder(256);
                sb.AppendLine($"[SocialInfo Dump] {pawn.Name?.ToStringShort ?? pawn.LabelShortCap}");
                sb.AppendLine(prompt);
                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] SocialInfo dump failed: {e}");
            }
        }

        // --- ColonyContext ---
        private static void DumpColonyContext()
        {
            try
            {
                var ctx = ColonyContextMapper.Create();
                if (ctx == null) { Log.Message("[ColonyContext] null (no map?)"); return; }

                var sb = new StringBuilder(2048);
                sb.AppendLine("[ColonyContext Dump]");
                sb.AppendLine($"Time: tick={ctx.CurrentTick} {ctx.Season} {ctx.TimeOfDay} Y{ctx.Year} H{ctx.Hour}");
                sb.AppendLine($"Pop: alive={ctx.PopulationAlive}");
                sb.AppendLine($"Food={ctx.FoodStatus} Power={ctx.PowerStatus}");
                sb.AppendLine($"Morale: avg={ctx.MoraleAverage:F2} tier={ctx.MoraleTier}");

                if (ctx.Colonists != null && ctx.Colonists.Count > 0)
                {
                    sb.AppendLine("== Colonists ==");
                    foreach (var c in ctx.Colonists)
                    {
                        string flags = "";
                        if (c.IsDead) flags += " [DEAD]";
                        sb.AppendLine($"  {c.Name} ({c.ID}): job={c.CurrentJob} mood={c.MoodTier} pain={c.PainTier}{flags}");
                    }
                }

                if (ctx.FactionRelations != null && ctx.FactionRelations.Count > 0)
                {
                    sb.AppendLine("== FactionRelations ==");
                    foreach (var f in ctx.FactionRelations)
                        sb.AppendLine($"  {f.FactionName}: goodwill={f.Goodwill:F0} ({f.RelationLabel})");
                }

                if (ctx.ActiveThreats != null && ctx.ActiveThreats.Count > 0)
                {
                    sb.AppendLine("== Threats ==");
                    foreach (var t in ctx.ActiveThreats)
                        sb.AppendLine($"  {t}");
                }

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] ColonyContext dump failed: {e}");
            }
        }

        // --- EventLog ---
        private static void DumpEventLog()
        {
            try
            {
                var eventLog = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (eventLog == null)
                {
                    Log.Message("[EventLog Dump] (no event log available - game not loaded?)");
                    return;
                }

                var events = eventLog.Query(EventQuery.All);
                var sb = new StringBuilder(2048);
                sb.AppendLine($"[EventLog Dump] count={events.Count} total={eventLog.TotalAppended}");

                if (events.Count == 0)
                {
                    sb.AppendLine("(no events)");
                }
                else
                {
                    foreach (var evt in events)
                    {
                        sb.AppendLine($"  {evt.DefName} imp={evt.Importance:F1}");
                        if (evt.Actors != null)
                        {
                            foreach (var a in evt.Actors)
                                sb.AppendLine($"    actor: {a.Name}({a.ID}) role={a.Role} type={a.RefType}");
                        }
                        if (evt.Payload != null && evt.Payload.Count > 0)
                        {
                            var pairs = evt.Payload.Select(kv => $"{kv.Key}={kv.Value}");
                            sb.AppendLine($"    payload: {string.Join(", ", pairs)}");
                        }
                    }
                }

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] EventLog dump failed: {e}");
            }
        }

        // --- Objectives ---
        private static void DumpObjectives()
        {
            try
            {
                var objectives = ObjectiveCardMapper.GetActive();
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[ObjectiveCard Dump] active={objectives.Count}");

                foreach (var o in objectives)
                {
                    sb.AppendLine($"  [{o.Status}] {o.Title} (ID={o.ID}) source={o.Source}");
                    if (!string.IsNullOrEmpty(o.Description))
                        sb.AppendLine($"    desc: {o.Description}");
                    if (o.Steps != null && o.Steps.Count > 0)
                    {
                        foreach (var s in o.Steps)
                            sb.AppendLine($"    step: {s.Label} completed={s.IsCompleted}");
                    }
                }

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] ObjectiveCard dump failed: {e}");
            }
        }
    }

    /// <summary>
    /// Harmony patch injecting colony debug gizmos into Pawn.GetGizmos.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_ColonyDebugPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(ColonyDebug.GetDebugGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning($"[ColonyDebug] Gizmo injection failed: {e.Message}");
            }
        }
    }
}
