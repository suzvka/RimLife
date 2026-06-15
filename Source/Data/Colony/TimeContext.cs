using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimLife.Framework;

namespace RimLife
{
    /// <summary>
    /// 时间上下文快照。轻量级值类型，不需要 Pawn 参数。
    /// 注意：此数据为快照，不保证其时序一致性。
    /// </summary>
    public struct TimeContext
    {
        /// <summary>当前游戏 tick。</summary>
        public int CurrentTick;

        /// <summary>季节: "Spring"/"Summer"/"Fall"/"Winter"。</summary>
        public string Season;

        /// <summary>时段: "Dawn"/"Day"/"Dusk"/"Night"。</summary>
        public string TimeOfDay;

        /// <summary>季度标签 (例如 "Apr-Jun")。</summary>
        public string Quadrum;

        /// <summary>游戏年份。</summary>
        public int Year;

        /// <summary>季度内天数 (1~15)。</summary>
        public int DayOfQuadrum;

        /// <summary>当前小时 (0~23)。</summary>
        public int Hour;

        /// <summary>
        /// 基于指定地图获取当前时间上下文。
        /// 必须在主线程上调用。
        /// </summary>
        public static TimeContext Current(int mapId = 0)
        {
            var ctx = new TimeContext();

            try
            {
                ctx.CurrentTick = Find.TickManager?.TicksGame ?? -1;
                if (ctx.CurrentTick < 0) return ctx;

                // 获取地图以确定 tile
                Map map = null;
                if (mapId == 0)
                    map = Find.CurrentMap;
                else
                    map = Find.Maps.Find(m => m.uniqueID == mapId);

                if (map == null)
                    map = Find.AnyPlayerHomeMap;

                if (map == null)
                {
                    ctx.TimeOfDay = "Unknown";
                    ctx.Hour = -1;
                    return ctx;
                }

                // 使用 RimWorld API 获取时间信息
                int tick = ctx.CurrentTick;
                int tile = map.Tile;

                // 转换绝对 tick 和经纬度
                long absTick = GenDate.TickGameToAbs(tick);
                Vector2 longLat = Find.WorldGrid.LongLatOf(tile);
                float longitude = longLat.x;

                // 季节
                try { ctx.Season = GenDate.Season(absTick, longLat).ToString(); } catch { ctx.Season = "Unknown"; }
                try { ctx.Year = GenDate.Year(absTick, longitude); } catch { ctx.Year = 0; }

                // 本地时间（依赖经度）
                try { ctx.Hour = GenDate.HourInteger(absTick, longitude); } catch { ctx.Hour = -1; }
                ctx.TimeOfDay = ctx.Hour >= 0 ? MapTimeOfDay(ctx.Hour) : "Unknown";
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife] TimeContext.Current failed: {e.Message}");
            }

            return ctx;
        }

        /// <summary>
        /// 异步获取时间上下文。
        /// </summary>
        public static System.Threading.Tasks.Task<TimeContext> CurrentAsync(int mapId = 0)
        {
            return MainThreadDispatcher.EnqueueAsync(() => Current(mapId));
        }

        private static string MapTimeOfDay(int hour)
        {
            if (hour >= 5 && hour < 7) return "Dawn";
            if (hour >= 7 && hour < 18) return "Day";
            if (hour >= 18 && hour < 20) return "Dusk";
            return "Night";
        }
    }
}
