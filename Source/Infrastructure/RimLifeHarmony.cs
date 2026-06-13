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
            // 创建 RimWorld 日志适配器，注入到框架层和核心
            var logger = new RimWorldLogger();
            RimLifeCore.Logger = logger;
            MainThreadDispatcher.Logger = logger;

            // 注册 Pawn 语义提示词提供者
            RimLifeCore.PromptProvider = new PawnPromptProvider();

            Instance = new Harmony("RimLife.Core");
            Instance.PatchAll();
            logger.Message("[RimLife.Infrastructure] Harmony patches registered.");

            // 初始化 MCP Skill 注册表（扫描所有工具类，建立 Skill → Tool 映射）
            RimLifeCore.EnsureSkillRegistryInitialized();
        }
    }
}
