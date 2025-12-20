using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    public static class MapTick
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
}
