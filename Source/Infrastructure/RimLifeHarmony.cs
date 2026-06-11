using HarmonyLib;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 集中管理所有 Harmony patch 注册。
    /// 程序集中所有 [HarmonyPatch] 类由 PatchAll() 自动发现。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimLifeHarmony
    {
        private static readonly Harmony Instance;

        static RimLifeHarmony()
        {
            Instance = new Harmony("RimLife.Core");
            Instance.PatchAll();
            Log.Message("[RimLife.Infrastructure] Harmony patches registered.");
        }
    }
}
