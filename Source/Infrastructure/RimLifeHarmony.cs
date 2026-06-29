using HarmonyLib;
using NPCLife.Framework;
using RimLife.UI;
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
            // RimLifeLogger 直接写 LogBuffer，无外部依赖，从第一行就可用
            RimLifeLogger.Message("[RimLife.DIAG] ===== Static constructor: START =====");

            // ---- Step 1: 清理临时缓存 ----
            try
            {
                LocalFileStore.CleanupTempCache();
                RimLifeLogger.Message("[RimLife.DIAG] Step 1 OK: CleanupTempCache");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 1 FAILED: CleanupTempCache: {e}");
            }

            // ---- Step 2: 注入 Logger ----
            try
            {
                var logger = new UiLoggerAdapter();
                RimLifeCore.Logger = logger;
                MainThreadDispatcher.Logger = logger;

                // 注入时间提供者：基于 RimWorld 游戏时间和季节
                RimLifeCore.TimeProvider = () =>
                {
                    int tick = Find.TickManager?.TicksGame ?? 0;
                    int year = 5500 + tick / 3600000;
                    int day = (tick / 60000) % 60;
                    int hour = (tick / 2500) % 24;
                    return $"第{year}年·第{day + 1}天·{hour:D2}时";
                };

                RimLifeLogger.Message("[RimLife.DIAG] Step 2 OK: Logger injected");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 2 FAILED: Logger injection: {e}");
            }

            // ---- Step 3: 注册 ContentProviders ----
            try
            {
                RimLifeCore.RegisterContentProvider(new HealthContentProvider());
                RimLifeCore.RegisterContentProvider(new MoodContentProvider());
                RimLifeCore.RegisterContentProvider(new SkillsContentProvider());
                RimLifeCore.RegisterContentProvider(new NeedsContentProvider());
                RimLifeCore.RegisterContentProvider(new ActivityContentProvider());
                RimLifeCore.RegisterContentProvider(new GearContentProvider());
                RimLifeCore.RegisterContentProvider(new BackstoryContentProvider());
                RimLifeCore.RegisterContentProvider(new SocialContentProvider());
                RimLifeCore.RegisterContentProvider(new PsychologyContentProvider());
                RimLifeCore.RegisterContentProvider(new PerspectiveContentProvider());
                RimLifeCore.RegisterContentProvider(new MemoryContentProvider());
                RimLifeLogger.Message("[RimLife.DIAG] Step 3 OK: 11 ContentProviders registered");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 3 FAILED: ContentProviders: {e}");
            }

            // ---- Step 4: Harmony PatchAll ----
            try
            {
                Instance = new Harmony("RimLife.Core");
                Instance.PatchAll();
                RimLifeLogger.Message("[RimLife.DIAG] Step 4 OK: Harmony PatchAll");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 4 FAILED: Harmony PatchAll: {e}");
            }

            // ---- Step 5: MCP Skill 注册表（关键步骤）----
            try
            {
                RimLifeLogger.Message("[RimLife.DIAG] Step 5: calling EnsureSkillRegistryInitialized...");
                RimLifeCore.EnsureSkillRegistryInitialized();
                RimLifeLogger.Message("[RimLife.DIAG] Step 5 OK: EnsureSkillRegistryInitialized returned");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 5 FAILED: EnsureSkillRegistryInitialized: {e}");
                if (e.InnerException != null)
                    RimLifeLogger.Error($"[RimLife.DIAG] Step 5 Inner: {e.InnerException}");
            }

            // ---- Step 6: 凭证管理器 ----
            try
            {
                var _ = RimLifeCore.CredentialManager;
                RimLifeLogger.Message("[RimLife.DIAG] Step 6 OK: CredentialManager loaded");
            }
            catch (System.Exception e)
            {
                RimLifeLogger.Error($"[RimLife.DIAG] Step 6 FAILED: CredentialManager: {e}");
            }

            RimLifeLogger.Message("[RimLife.DIAG] ===== Static constructor: END =====");
        }
    }
}
