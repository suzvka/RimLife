using HarmonyLib;
using RimLife.Cards;
using RimLife.Infrastructure;
using RimLife.Mappers;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace RimLife
{
    // ================================================================
    // Incident (袭击/事件) Hook
    // ================================================================
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    internal static class Patch_IncidentWorker_TryExecute
    {
        static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            if (!__result) return;
            try
            {
                RimLifeCore.EventLog?.Append(EventCardMapper.FromIncident(__instance.def, parms));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Incident hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // Pawn 死亡 Hook
    // ================================================================
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_Pawn_Kill
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance == null) return;
            try
            {
                RimLifeCore.EventLog?.Append(EventCardMapper.FromDeath(__instance, dinfo));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Death hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 精神崩溃 Hook (RimWorld 1.6: MentalBreaker API)
    // ================================================================
    [HarmonyPatch(typeof(MentalBreaker), "TryDoMentalBreak")]
    internal static class Patch_MentalBreaker_TryDoMentalBreak
    {
        static void Postfix(MentalBreaker __instance, string reason, MentalBreakDef breakDef, bool __result)
        {
            if (!__result || breakDef == null) return;
            try
            {
                var pawn = AccessTools.Field(typeof(MentalBreaker), "pawn")?.GetValue(__instance) as Pawn;
                if (pawn == null)
                {
                    Log.Warning("[RimLife:EventHooks] MentalBreak: unable to resolve pawn from MentalBreaker");
                    return;
                }
                RimLifeCore.EventLog?.Append(EventCardMapper.FromMentalBreak(pawn, reason, breakDef));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] MentalBreak hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 社交互动 Hook
    // Hook Pawn_InteractionsTracker.TryInteractWith 以获取 InteractionDef
    // 双写：EventLog（事件卡）+ InteractionHistoryStore（流水记录）
    // ================================================================
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), "TryInteractWith")]
    internal static class Patch_InteractionsTracker_TryInteractWith
    {
        static void Postfix(Pawn_InteractionsTracker __instance, Pawn recipient, InteractionDef intDef)
        {
            if (__instance == null || recipient == null || intDef == null) return;
            try
            {
                var initiator = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (initiator == null) return;

                // 写入 EventLog（事件卡）
                RimLifeCore.EventLog?.Append(EventCardMapper.FromSocialInteraction(initiator, recipient, intDef));

                // 写入 InteractionHistoryStore（流水记录）
                RimLifeCore.InteractionStore?.Append(new Cards.InteractionRecord
                {
                    Tick = Find.TickManager?.TicksGame ?? 0,
                    InitiatorID = initiator.ThingID ?? "?",
                    RecipientID = recipient.ThingID ?? "?",
                    InteractionDef = intDef.defName ?? "Unknown",
                    Outcome = intDef.label ?? ""
                });
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] SocialInteract hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // Quest 状态变化 Hook
    // ================================================================
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    internal static class Patch_Quest_End
    {
        static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            if (__instance == null) return;
            try
            {
                RimLifeCore.EventLog?.Append(EventCardMapper.FromQuest(__instance, outcome.ToString()));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Quest end hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // Pawn 派系变更 Hook (殖民者加入/叛逃/被俘)
    // ================================================================
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class Patch_Pawn_SetFaction
    {
        static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (__instance == null) return;
            try
            {
                RimLifeCore.EventLog?.Append(EventCardMapper.FromFactionChange(__instance, newFaction));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] SetFaction hook failed: {e.Message}");
            }
        }
    }
}
