using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace RimLife
{
    /// <summary>
    /// Harmony patches 将 RimWorld 游戏事件注入 EventBuffer。
    /// 沿用项目现有模式：[StaticConstructorOnStartup] + Harmony 初始化。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class EventHooks
    {
        static EventHooks()
        {
            var harmony = new Harmony("RimLife.EventHooks");
            harmony.PatchAll();
        }
    }

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
                EventBuffer.Instance.Push(new IncidentGameEvent(def, parms));
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
                EventBuffer.Instance.Push(new DeathGameEvent(__instance, dinfo));
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
                EventBuffer.Instance.Push(new MentalBreakGameEvent(__instance, mentalState));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] MentalBreak hook failed: {e.Message}");
            }
        }
    }

    // ================================================================
    // 社交互动 Hook
    // 使用 Pawn.SocialInteract 作为 hook 点 (RimWorld 1.6)
    // ================================================================
    [HarmonyPatch(typeof(Pawn), "SocialInteract")]
    internal static class Patch_Pawn_SocialInteract
    {
        static void Postfix(Pawn __instance, Pawn other)
        {
            if (__instance == null || other == null) return;
            try
            {
                // SocialInteract 不传入 InteractionDef，此处仅记录发生事件
                // 具体互动类型由 InteractionLog 跟踪（后续版本实现）
                EventBuffer.Instance.Push(new SocialInteractionGameEvent(__instance, other, null));
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
                EventBuffer.Instance.Push(new QuestGameEvent(__instance, outcome.ToString()));
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife:EventHooks] Quest end hook failed: {e.Message}");
            }
        }
    }
}
