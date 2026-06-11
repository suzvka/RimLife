using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimLife.Core;
using RimLife.Infrastructure;
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

            // SocialInfo dump
            yield return new Command_Action
            {
                defaultLabel = "SocialInfo Dump",
                defaultDesc = "Print social relations for this pawn.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpSocialInfo(pawn)
            };

            // ColonySnapshot dump (any pawn triggers it)
            yield return new Command_Action
            {
                defaultLabel = "ColonySnapshot Dump",
                defaultDesc = "Print colony-level snapshot.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpColonySnapshot()
            };

            // TimeContext dump
            yield return new Command_Action
            {
                defaultLabel = "TimeContext Dump",
                defaultDesc = "Print current time context.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpTimeContext()
            };

            // EventLog dump
            yield return new Command_Action
            {
                defaultLabel = "EventLog Dump",
                defaultDesc = "Print recent events from log.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpEventLog()
            };

            // QuestInfo dump
            yield return new Command_Action
            {
                defaultLabel = "QuestInfo Dump",
                defaultDesc = "Print active quests.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => DumpQuestInfo()
            };
        }

        // --- SocialInfo ---
        private static void DumpSocialInfo(Pawn pawn)
        {
            try
            {
                var si = SocialInfo.CreateFrom(pawn);
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[SocialInfo Dump] {pawn.Name?.ToStringShort ?? pawn.LabelShortCap}");
                sb.AppendLine($"ColonyOpinionAvg={si.ColonyOpinionAverage:F1}");

                if (si.Relations != null && si.Relations.Count > 0)
                {
                    sb.AppendLine("== Relations ==");
                    foreach (var r in si.Relations)
                    {
                        sb.AppendLine($"  {r.OtherName} ({r.OtherID}): type={r.RelationType} opinion={r.Opinion:F0}({r.OpinionTier}) reciprocal={r.IsReciprocal}");
                    }
                }
                else sb.AppendLine("Relations: (none)");

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] SocialInfo dump failed: {e}");
            }
        }

        // --- ColonySnapshot ---
        private static void DumpColonySnapshot()
        {
            try
            {
                var cs = ColonySnapshot.Create();
                if (cs == null) { Log.Message("[ColonySnapshot] null (no map?)"); return; }

                var sb = new StringBuilder(2048);
                sb.AppendLine("[ColonySnapshot Dump]");
                sb.AppendLine($"Time: tick={cs.Time.CurrentTick} {cs.Time.Season} {cs.Time.TimeOfDay} {cs.Time.Quadrum} Y{cs.Time.Year} D{cs.Time.DayOfQuadrum} H{cs.Time.Hour}");
                sb.AppendLine($"Pop: alive={cs.PopulationAlive} downed={cs.PopulationDowned} mental={cs.PopulationMentalBreak}");
                sb.AppendLine($"Wealth={cs.WealthTotal:F0} Food={cs.FoodStatus} Power={cs.PowerStatus}");
                sb.AppendLine($"Morale: avg={cs.MoraleAverage:F2} tier={cs.MoraleTier}");

                if (cs.Colonists != null && cs.Colonists.Count > 0)
                {
                    sb.AppendLine("== Colonists ==");
                    foreach (var c in cs.Colonists)
                    {
                        string flags = "";
                        if (c.IsDowned) flags += " [DOWNED]";
                        if (c.IsDead) flags += " [DEAD]";
                        sb.AppendLine($"  {c.Name} ({c.ID}): job={c.CurrentJob} mood={c.MoodTier} pain={c.PainTier}{flags}");
                    }
                }

                if (cs.FactionRelations != null && cs.FactionRelations.Count > 0)
                {
                    sb.AppendLine("== FactionRelations ==");
                    foreach (var f in cs.FactionRelations)
                        sb.AppendLine($"  {f.FactionName}: goodwill={f.Goodwill:F0} ({f.RelationLabel})");
                }

                if (cs.ActiveThreats != null && cs.ActiveThreats.Count > 0)
                {
                    sb.AppendLine("== Threats ==");
                    foreach (var t in cs.ActiveThreats)
                        sb.AppendLine($"  {t}");
                }

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] ColonySnapshot dump failed: {e}");
            }
        }

        // --- TimeContext ---
        private static void DumpTimeContext()
        {
            try
            {
                var tc = TimeContext.Current();
                var sb = new StringBuilder(256);
                sb.AppendLine("[TimeContext Dump]");
                sb.AppendLine($"Tick={tc.CurrentTick} Season={tc.Season} TimeOfDay={tc.TimeOfDay}");
                sb.AppendLine($"Quadrum={tc.Quadrum} Year={tc.Year} DayOfQuadrum={tc.DayOfQuadrum} Hour={tc.Hour}");
                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] TimeContext dump failed: {e}");
            }
        }

        // --- EventLog ---
        private static void DumpEventLog()
        {
            try
            {
                var eventLog = RimLifeCore.EventLog;
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
                        sb.AppendLine($"  [{evt.Category}] {evt.DefName} tick={evt.Tick} sev={evt.Severity}");
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

        // --- QuestInfo ---
        private static void DumpQuestInfo()
        {
            try
            {
                var quests = QuestInfo.GetActive();
                var sb = new StringBuilder(1024);
                sb.AppendLine($"[QuestInfo Dump] active={quests.Count}");

                foreach (var q in quests)
                {
                    sb.AppendLine($"  [{q.Status}] {q.Title} (ID={q.QuestID})");
                    if (!string.IsNullOrEmpty(q.Description))
                        sb.AppendLine($"    desc: {q.Description}");
                    if (q.TimeLimitTick.HasValue)
                        sb.AppendLine($"    timeLimit: {q.TimeLimitTick.Value} ticks remaining");
                    if (q.Parts != null && q.Parts.Count > 0)
                    {
                        foreach (var p in q.Parts)
                            sb.AppendLine($"    part: {p.PartLabel} completed={p.IsCompleted}");
                    }
                }

                Log.Message(sb.ToString());
            }
            catch (Exception e)
            {
                Log.Error($"[ColonyDebug] QuestInfo dump failed: {e}");
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
