using NPCLife.Framework.Mcp;
using RimLife.Mappers;
using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 角色与环境查询 Skill 的 Hook Provider（环境感知工具）。
    /// 提供角色当前所处环境信息查询。
    /// 注入到 character_query skill，与 CharacterQueryProvider 共用同一 HookId。
    /// </summary>
    public class EnvironmentQueryProvider : IMcpHookProvider
    {
        public string HookId => "character_query";
        public string HookName => "角色与环境查询";
        public string HookDescription => "查询角色当前所处的环境信息（室内外、温光、天气、房间）";

        public IReadOnlyList<McpTool> GetTools()
        {
            return Array.Empty<McpTool>();
        }

        /// <summary>
        /// 获取殖民地整体环境信息（温度、天气、光照）。
        /// 已由上下文注入替代，保留供测试调用。
        /// </summary>
        public static string GetColonyEnvironment(
            [McpParam(Description = "地图 ID，0=当前地图",
                      Required = McpRequired.False)] int mapId = 0)
        {
            try
            {
                var env = EnvironmentCardMapper.CreateForMap(mapId);
                return CardSerializer.Default.SerializeEnvironment(env);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.EnvironmentQueryProvider] get_colony_environment failed: {e.Message}");
                return "{}";
            }
        }
    }
}
