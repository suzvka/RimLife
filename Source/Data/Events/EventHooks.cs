using HarmonyLib;
using NPCLife.Cards;
using RimLife.Infrastructure;
using RimLife.Mappers;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimLife
{
    // ================================================================
    // 统一信封 Hook（替代逐个事件 Hook，覆盖所有 RimWorld 信封事件）
    // Letter 自带叙事文案（label / text），天然适配编剧 agent 消费。
    // ================================================================
    
    // 补丁重载 1：完整参数版本
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
        new Type[] { typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(LookTargets), typeof(Faction), typeof(Quest), typeof(List<ThingDef>), typeof(string), typeof(int), typeof(bool) })]
    internal static class Patch_LetterStack_ReceiveLetter_Full
    {
        static void Postfix(LetterStack __instance, TaggedString label, TaggedString text,
                             LetterDef textLetterDef, LookTargets lookTargets, Faction relatedFaction)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromLetter(textLetterDef, label, text, lookTargets, relatedFaction));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Letter hook (full) failed: {e.Message}");
            }
        }
    }

    // 补丁重载 2：简化参数版本（无 LookTargets）
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
        new Type[] { typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(string), typeof(int), typeof(bool) })]
    internal static class Patch_LetterStack_ReceiveLetter_Simple
    {
        static void Postfix(LetterStack __instance, TaggedString label, TaggedString text, LetterDef textLetterDef)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromLetter(textLetterDef, label, text, default, null));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Letter hook (simple) failed: {e.Message}");
            }
        }
    }

    // 补丁重载 3：Letter 对象版本
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter),
        new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
    internal static class Patch_LetterStack_ReceiveLetter_Letter
    {
        static void Postfix(LetterStack __instance, Letter let)
        {
            try
            {
                if (let == null) return;
                // Letter 的属性可能不同，使用 def 和默认值
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromLetter(let.def, let.def?.label ?? "Letter", "", default, null));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Letter hook (letter) failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 消息通知 Hook（左上角 Messages.Message）
    // 覆盖 Hediff 阶段变化、任务目标更新、商人到达、驯服结果等
    // 不生成 Letter 的叙事性短通知，全量接入。
    // ================================================================
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message),
        new Type[] { typeof(string), typeof(MessageTypeDef), typeof(LookTargets), typeof(bool) })]
    internal static class Patch_Messages_Message
    {
        static void Postfix(string text, MessageTypeDef msgType, LookTargets lookTargets)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromMessage(text, msgType, lookTargets));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Messages.Message hook failed: {e.Message}");
            }
        }
    }

    // 消息通知 TaggedString 重载
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message),
        new Type[] { typeof(TaggedString), typeof(MessageTypeDef), typeof(LookTargets), typeof(bool) })]
    internal static class Patch_Messages_Message_Tagged
    {
        static void Postfix(TaggedString text, MessageTypeDef msgType, LookTargets lookTargets)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromMessage(text.ToString(), msgType, lookTargets));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Messages.Message (tagged) hook failed: {e.Message}");
            }
        }
    }

    // 消息通知 string 无 LookTargets 重载
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message),
        new Type[] { typeof(string), typeof(MessageTypeDef), typeof(bool) })]
    internal static class Patch_Messages_Message_Simple
    {
        static void Postfix(string text, MessageTypeDef msgType)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromMessage(text, msgType, null));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Messages.Message (simple) hook failed: {e.Message}");
            }
        }
    }

    // 消息通知 TaggedString 无 LookTargets 重载
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message),
        new Type[] { typeof(TaggedString), typeof(MessageTypeDef), typeof(bool) })]
    internal static class Patch_Messages_Message_TaggedSimple
    {
        static void Postfix(TaggedString text, MessageTypeDef msgType)
        {
            try
            {
                RimLifeCore.EventBuffer?.Append(
                    EventCardMapper.FromMessage(text.ToString(), msgType, null));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Messages.Message (tagged simple) hook failed: {e.Message}");
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

                // 不写入 EventPool —— 社交互动频率极高（每次闲聊/侮辱/安慰均触发），
                // 属于流水账式记录。互动数据已通过 InteractionHistoryStore 持久化，
                // 并由 MCP 工具 get_interaction_history 按需拉取，无需主动推送。

                // 写入 InteractionHistoryStore（流水记录）
                RimLifeCore.InteractionStore?.Append(new NPCLife.Cards.InteractionRecord
                {
                    Tick = Find.TickManager?.TicksGame ?? 0,
                    InitiatorID = initiator.ThingID ?? "?",
                    RecipientID = recipient.ThingID ?? "?",
                    InteractionDef = intDef.defName ?? "Unknown",
                    Outcome = intDef.label ?? ""
                });

                // 写入双方 Pawn 的短期记忆
                AppendPawnMemory(initiator, recipient, intDef);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] SocialInteract hook failed: {e.Message}");
            }
        }

        /// <summary>
        /// 向互动双方的 PawnProMemory 追加短期记忆。
        /// </summary>
        private static void AppendPawnMemory(Pawn initiator, Pawn recipient, InteractionDef intDef)
        {
            try
            {
                int tick = Find.TickManager?.TicksGame ?? 0;

                // 发起者的记忆
                AppendMemoryToPawn(initiator, tick, "Interaction",
                    $"与 {recipient.Name?.ToStringShort ?? recipient.LabelShortCap} 进行了{intDef.label ?? "互动"}",
                    recipient.ThingID);

                // 接受者的记忆
                AppendMemoryToPawn(recipient, tick, "Interaction",
                    $"{initiator.Name?.ToStringShort ?? initiator.LabelShortCap} 与你进行了{intDef.label ?? "互动"}",
                    initiator.ThingID);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] AppendPawnMemory failed: {e.Message}");
            }
        }

        private static void AppendMemoryToPawn(Pawn pawn, int tick, string type, string summary, string relatedPawnId)
        {
            if (pawn?.health?.hediffSet == null) return;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory");
            if (hediffDef == null) return;

            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff == null) return;

            var comp = hediff.TryGetComp<HediffComp_PawnMemory>();
            if (comp == null) return;

            comp.AddShortTerm(new ShortTermMemory(tick, type, summary, relatedPawnId));
        }
    }

}
