using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 地图生成 tick 工具。注意：返回的是地图生成时间，非当前游戏时间。
    /// 如需当前游戏时间，请使用 GameClock。
    /// </summary>
    public static class MapGenerationTick
    {
        public static int Get(int mapId = 0)
        {
            try
            {
                if (mapId == 0)
                {
                    mapId = Find.CurrentMap.uniqueID;
                }
                var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null)
                {
                    return -1;
                }

                return map.generationTick;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }

    /// <summary>
    /// 当前游戏时间工具。返回全局 tick，非地图生成 tick。
    /// </summary>
    public static class GameClock
    {
        /// <summary>
        /// 获取当前游戏 tick。如果 TickManager 不可用则返回 -1。
        /// </summary>
        public static int CurrentTick()
        {
            try
            {
                return Find.TickManager?.TicksGame ?? -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
