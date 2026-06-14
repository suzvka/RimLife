using RimLife.Framework.Mcp;
using RimLife.Mappers;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 环境感知 Skill 的 Hook Provider。
    /// 提供角色当前所处环境信息查询。
    /// </summary>
    public class EnvironmentQueryProvider : IMcpHookProvider
    {
        public string HookId => "environment_query";
        public string HookName => "环境感知";
        public string HookDescription => "查询角色当前所处的环境信息（室内外、温光、天气、房间）";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(EnvironmentQueryProvider).GetMethod(nameof(GetEnvironment))),
            };
        }

        /// <summary>
        /// 获取指定角色当前所处环境信息。
        /// </summary>
        [McpTool(Name = "get_environment",
                 Description = "获取指定角色当前所处环境信息：室内外、温光、天气、房间评分。")]
        public static string GetEnvironment(
            [McpParam(Description = "角色唯一 ID")] string pawnId)
        {
            try
            {
                var pawn = PawnQueryHelper.FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var env = EnvironmentCardMapper.CreateFrom(pawn);
                return CardSerializer.Default.SerializeEnvironment(env);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.EnvironmentQueryProvider] get_environment({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }
    }
}
