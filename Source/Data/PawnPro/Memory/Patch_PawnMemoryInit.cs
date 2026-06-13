using HarmonyLib;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Pawn Spawn 时自动附加 PawnProMemory 隐藏 Hediff（如果不存在）。
    /// 确保每个 Pawn 都有记忆系统。
    /// 通过 RimLifeHarmony.PatchAll() 自动发现。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class Patch_Pawn_SpawnSetup_MemoryInit
    {
        static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance?.health?.hediffSet == null) return;

                // 检查是否已存在
                var def = DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory");
                if (def == null) return;

                if (__instance.health.hediffSet.HasHediff(def)) return;

                // 附加隐藏 Hediff
                var hediff = HediffMaker.MakeHediff(def, __instance);
                __instance.health.AddHediff(hediff);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[RimLife.PawnMemory] Failed to attach memory hediff to pawn {__instance?.ThingID}: {e.Message}");
            }
        }
    }
}
