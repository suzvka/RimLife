using RimWorld;
using System;
using System.Linq;
using Verse;

namespace RimLife
{
    public static class Tool
    {
        public static bool TryTranslate(string key, out string text)
        {
            text = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.CanTranslate())
            {
                var resolved = key.Translate().ToString();
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    text = resolved;
                    return true;
                }
            }

            return false;
        }
        public static Pawn GetPawn(string ID)
        {
            return PawnsFinder.AllMaps_Spawned.FirstOrFallback(pp => pp.ThingID == ID);
        }


    }
}
