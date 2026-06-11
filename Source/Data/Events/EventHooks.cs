using HarmonyLib;
using RimWorld;
using System;
using RimLife.Infrastructure;
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
                var def = __instance.def;
                RimLifeCore.EventLog?.Append(new IncidentGameEvent(def, parms));
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
                RimLifeCore.EventLog?.Append(new DeathGameEvent(__instance, dinfo));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Death hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 精神崩溃 Hook
    // Pawn.TryStartMentalState (RimWorld 1.6 API)
    // ================================================================
    [HarmonyPatch(typeof(Pawn), "TryStartMentalState")]
    internal static class Patch_Pawn_TryStartMentalState
    {
        static void Postfix(Pawn __instance, MentalStateDef def, bool __result)
        {
            if (!__result) return;
            try
            {
                var mentalState = __instance?.MentalState;
                RimLifeCore.EventLog?.Append(new MentalBreakGameEvent(__instance, mentalState));
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
                RimLifeCore.EventLog?.Append(new SocialInteractionGameEvent(initiator, recipient, intDef));
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
                RimLifeCore.EventLog?.Append(new QuestGameEvent(__instance, outcome.ToString()));
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
                RimLifeCore.EventLog?.Append(new FactionChangeGameEvent(__instance, newFaction));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] SetFaction hook failed: {e.Message}");
            }
        }
    }
}
