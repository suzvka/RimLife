using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 在 Def 加载完成后，将大五人格映射注入到 TraitDef 的 modExtensions 中。
    /// XML Patch 无法覆盖 C# 实例化的 Vanilla TraitDef，因此改用程序化注入。
    /// 数据来源：Traits_Singular_CorrectedPatch.xml / Traits_Spectrum_CorrectedPatch.xml。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TraitPersonalityInjector
    {
        static TraitPersonalityInjector()
        {
            // 构建 { defName → { degree → PersonalityEntry } } 映射表
            var traitMap = BuildTraitMap();
            if (traitMap == null || traitMap.Count == 0) return;

            foreach (var traitDef in DefDatabase<TraitDef>.AllDefs)
            {
                if (traitDef?.defName == null) continue;
                if (!traitMap.TryGetValue(traitDef.defName, out var entryMap)) continue;

                var ext = new PersonalityExtension { data = new List<PersonalityEntry>() };
                foreach (var kv in entryMap)
                {
                    ext.data.Add(new PersonalityEntry
                    {
                        degree = kv.Key,
                        openness = kv.Value.o,
                        conscientiousness = kv.Value.c,
                        extraversion = kv.Value.e,
                        agreeableness = kv.Value.a,
                        neuroticism = kv.Value.n
                    });
                }

                if (traitDef.modExtensions == null)
                    traitDef.modExtensions = new List<DefModExtension>();
                traitDef.modExtensions.Add(ext);
            }
        }

        #region Data

        private struct Entry { public int o, c, e, a, n; }

        private static Dictionary<string, Dictionary<int, Entry>> BuildTraitMap()
        {
            return new Dictionary<string, Dictionary<int, Entry>>
            {
                // ===== Spectrum traits (Traits_Spectrum_CorrectedPatch.xml) =====
                ["SpeedOffset"] = D( (-1, 0,0,0,0,0), (1, 0,0,0,0,0), (2, 0,0,0,0,0) ),
                ["DrugDesire"] = D(
                    (2,  2,-2, 0, 0, 2),
                    (1,  1,-1, 0, 0, 1),
                    (-1,-1, 1, 0, 0,-1) ),
                ["NaturalMood"] = D(
                    (2,  0, 0, 2, 2,-2),
                    (1,  0, 0, 1, 1,-1),
                    (-1, 0, 0,-1,-1, 1),
                    (-2, 0, 0,-2,-2, 2) ),
                ["Nerves"] = D(
                    (2,  0,0,0,0,-3),
                    (1,  0,0,0,0,-1),
                    (-1, 0,0,0,0, 1),
                    (-2, 0,0,0,0, 2) ),
                ["Neurotic"] = D(
                    (1, 0,0,0,0,2),
                    (2, 0,0,0,0,3) ),
                ["Industriousness"] = D(
                    (2,  0, 3,0,0,0),
                    (1,  0, 2,0,0,0),
                    (-1, 0,-2,0,0,0),
                    (-2, 0,-3,0,0,0) ),
                ["PsychicSensitivity"] = D(
                    (2, 0,0,0,0,0), (1, 0,0,0,0,0),
                    (-1,0,0,0,0,0), (-2,0,0,0,0,0) ),
                ["ShootingAccuracy"] = D(
                    (1,  0, 2,0,0,-2),
                    (-1, 0,-2,0,0, 2) ),
                ["Beauty"] = D(
                    (2, 0,0,0,0,0), (1, 0,0,0,0,0),
                    (-1,0,0,-1,0,1), (-2,0,0,0,0,0) ),
                ["Immunity"] = D(
                    (1, 0,0,0,0,0), (-1,0,0,0,0,0) ),

                // ===== Singular traits (Traits_Singular_CorrectedPatch.xml) =====
                ["Nudist"]           = D( (0, 2, 0, 0, 0, 0) ),
                ["Bloodlust"]        = D( (0, 0,-2, 1,-3, 2) ),
                ["Kind"]             = D( (0, 0, 1, 0, 3,-1) ),
                ["Psychopath"]       = D( (0, 0,-2,-1,-3,-2) ),
                ["Cannibal"]         = D( (0, 2, 0, 0,-3, 1) ),
                ["Abrasive"]         = D( (0, 0, 0, 0,-2, 1) ),
                ["TooSmart"]         = D( (0, 2, 0, 0, 0, 1) ),
                ["Brawler"]          = D( (0, 0, 0, 1,-2, 0) ),
                ["Masochist"]        = D( (0, 1, 0, 0, 0, 2) ),
                ["NightOwl"]         = D( (0, 1, 0,-1, 0, 0) ),
                ["Greedy"]           = D( (0, 0, 0, 0,-1, 0) ),
                ["Jealous"]          = D( (0, 0, 0, 0,-1, 1) ),
                ["Ascetic"]          = D( (0,-1, 1,-1, 0,-1) ),
                ["Gay"]              = D( (0, 0,0,0,0,0) ),
                ["Bisexual"]         = D( (0, 0,0,0,0,0) ),
                ["Asexual"]          = D( (0, 0,0,0,0,0) ),
                ["AnnoyingVoice"]    = D( (0, 0,0,0,0,0) ),
                ["CreepyBreathing"]  = D( (0, 0,0,0,0,0) ),
                ["Pyromaniac"]       = D( (0, 0,-3, 0, 0, 2) ),
                ["Wimp"]             = D( (0, 0, 0, 0, 0, 3) ),
                ["Nimble"]           = D( (0, 0,0,0,0,0) ),
                ["FastLearner"]      = D( (0, 2, 1, 0, 0, 0) ),
                ["SlowLearner"]      = D( (0,-2, 0, 0, 0, 0) ),
                ["Undergrounder"]    = D( (0,-1, 0,-1, 0,-1) ),
                ["Transhumanist"]    = D( (0, 3, 0, 0, 0, 0) ),
                ["BodyPurist"]       = D( (0,-2, 1, 0,-1, 0) ),
                ["DislikesMen"]      = D( (0, 0, 0,-1,-2, 2) ),
                ["DislikesWomen"]    = D( (0, 0, 0,-1,-2, 2) ),
                ["GreatMemory"]      = D( (0, 2, 0, 0, 0, 0) ),
                ["Tough"]            = D( (0, 0, 0, 0, 0,-2) ),
                ["TorturedArtist"]   = D( (0, 3,-2,-2,-1, 3) ),
                ["Gourmand"]         = D( (0, 1,-2, 1, 0, 0) ),
                ["QuickSleeper"]     = D( (0, 0, 2, 0, 0,-1) ),
            };
        }

        /// <summary>Helper: map (degree, o, c, e, a, n) tuples to a degree→Entry dict.</summary>
        private static Dictionary<int, Entry> D(params (int d, int o, int c, int e, int a, int n)[] entries)
        {
            var map = new Dictionary<int, Entry>();
            foreach (var (d, o, c, e, a, n) in entries)
                map[d] = new Entry { o = o, c = c, e = e, a = a, n = n };
            return map;
        }

        #endregion
    }
}
