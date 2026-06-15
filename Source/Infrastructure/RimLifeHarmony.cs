using HarmonyLib;
using RimLife.Framework;
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
            try
            {
                // 创建 UI 日志适配器，将框架日志重定向到调试窗口
                var logger = new UiLoggerAdapter();
                RimLifeCore.Logger = logger;
                MainThreadDispatcher.Logger = logger;

                // 注册人物卡维度内容提供者（钩子模式）
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

                Instance = new Harmony("RimLife.Core");
                Instance.PatchAll();
                logger.Message("[RimLife.Infrastructure] Harmony patches registered.");

                // 初始化 MCP Skill 注册表（扫描所有工具类，建立 Skill → Tool 映射）
                RimLifeCore.EnsureSkillRegistryInitialized();
                logger.Message("[RimLife.Infrastructure] Startup complete.");
            }
            catch (System.Exception e)
            {
                Log.Error($"[RimLife.Infrastructure] FATAL: Static constructor failed: {e}");
                if (e.InnerException != null)
                    Log.Error($"[RimLife.Infrastructure] Inner: {e.InnerException}");
                throw;
            }
        }
    }
}
