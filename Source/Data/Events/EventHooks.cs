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
    // 统一信封 Hook（替代逐个事件 Hook，覆盖所有 RimWorld 信封事件）
    // Letter 自带叙事文案（label / text），天然适配编剧 agent 消费。
    // ================================================================
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter))]
    internal static class Patch_LetterStack_ReceiveLetter
    {
        static void Postfix(LetterStack __instance, TaggedString label, TaggedString text,
                             LetterDef textLetterDef, LookTargets lookTargets, Faction relatedFaction)
        {
            try
            {
                RimLifeCore.EventLog?.Append(
                    EventCardMapper.FromLetter(textLetterDef, label, text, lookTargets, relatedFaction));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Letter hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 社交互动 Hook（不弹信，需独立 Hook）
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
