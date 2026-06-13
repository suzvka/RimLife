using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimLife.Cards;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld Pawn 提取数据并组装 CharacterCard。
    /// 承担全部 RimWorld 耦合。
    /// </summary>
    public static class CharacterCardMapper
    {
        // ================================================================
        // 基础信息（廉价）
        // ================================================================

        /// <summary>
        /// 创建包含基本元数据的 CharacterCard（不展开子模块）。
        /// 必须在主线程上调用。
        /// </summary>
        public static CharacterCard CreateBasic(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            var card = new CharacterCard
            {
                ID = pawn.ThingID,
                Name = pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? pawn.LabelShort ?? "?",
                FullName = pawn.Name?.ToStringFull ?? pawn.LabelCap ?? pawn.Name?.ToStringShort ?? "?",
                DefName = pawn.def?.defName ?? "UnknownDef",
                FactionLabel = pawn.Faction?.Name ?? "Unknown",
                AgeBiologicalYears = pawn.ageTracker?.AgeBiologicalYearsFloat ?? 0f,
                Gender = pawn.gender.ToString(),
                PawnType = GetPawnType(pawn),
                PawnRelation = GetPawnRelation(pawn),
                IsDead = pawn.Dead,
                IsDowned = pawn.Downed,
                IsAwake = pawn.jobs?.curDriver?.asleep == false
            };

            return card;
        }

        // ================================================================
        // 按需填充各 Section（扩展方法链式调用）
        // ================================================================

        public static CharacterCard WithHealth(this CharacterCard card, Pawn p)
        {
            card.Health = MapHealth(p);
            return card;
        }

        public static CharacterCard WithMood(this CharacterCard card, Pawn p)
        {
            card.Mood = MapMood(p);
            return card;
        }

        public static CharacterCard WithSkills(this CharacterCard card, Pawn p)
        {
            card.Skills = MapSkills(p);
            return card;
        }

        public static CharacterCard WithNeeds(this CharacterCard card, Pawn p)
        {
            card.Needs = MapNeeds(p);
            return card;
        }

        public static CharacterCard WithActivity(this CharacterCard card, Pawn p)
        {
            card.Activity = MapActivity(p);
            return card;
        }

        public static CharacterCard WithGear(this CharacterCard card, Pawn p)
        {
            card.Gear = MapGear(p);
            return card;
        }

        public static CharacterCard WithBackstory(this CharacterCard card, Pawn p)
        {
            card.Backstory = MapBackstory(p);
            return card;
        }

        public static CharacterCard WithSocial(this CharacterCard card, Pawn p)
        {
            card.Social = MapSocial(p);
            return card;
        }

        public static CharacterCard WithPerspective(this CharacterCard card, Pawn p)
        {
            card.Perspective = MapPerspective(p);
            return card;
        }

        public static CharacterCard WithPsychology(this CharacterCard card, Pawn p)
        {
            card.Psychology = MapPsychology(card, p);
            return card;
        }

        public static CharacterCard WithMemory(this CharacterCard card, Pawn p)
        {
            card.Memory = MapMemory(p);
            return card;
        }

        // ================================================================
        // 完整构建（昂贵）
        // ================================================================

        /// <summary>
        /// 创建包含全部子模块的完整 CharacterCard。必须在主线程上调用。
        /// </summary>
        public static CharacterCard CreateFull(Pawn p)
        {
            return CreateBasic(p)
                .WithHealth(p)
                .WithMood(p)
                .WithSkills(p)
                .WithNeeds(p)
                .WithActivity(p)
                .WithGear(p)
                .WithBackstory(p)
                .WithSocial(p)
                .WithPerspective(p)
                .WithPsychology(p)
                .WithMemory(p);
        }

        /// <summary>
        /// 异步创建完整 CharacterCard。
        /// </summary>
        public static Task<CharacterCard> CreateFullAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new CharacterCard());
            return MainThreadDispatcher.EnqueueAsync(() => CreateFull(p));
        }

        // ================================================================
        // 各 Section 映射实现
        // ================================================================

        private static readonly PawnCapacityDef[] KeyCapacityDefs =
        [
            PawnCapacityDefOf.Moving,
            PawnCapacityDefOf.Manipulation,
            PawnCapacityDefOf.Talking,
            PawnCapacityDefOf.Consciousness,
            PawnCapacityDefOf.Sight,
            PawnCapacityDefOf.Hearing,
            PawnCapacityDefOf.Breathing
        ];

        private static HealthSection MapHealth(Pawn p)
        {
            if (p?.health == null) return new HealthSection
            {
                Capacities = new Dictionary<string, float>(),
                CapacityTiers = new Dictionary<string, string>(),
                Injuries = new List<HealthEntry>(),
                PainTier = "None",
                BleedTier = "None"
            };

            string wholeBodyLabel = p.RaceProps?.body?.corePart?.def?.label ?? "WholeBody";

            var summaryPain = p.health.hediffSet?.PainTotal ?? 0f;
            var summaryBleedRate = p.health.hediffSet?.BleedRateTotal ?? 0f;

            var capacities = new Dictionary<string, float>();
            if (p.health.capacities != null)
            {
                foreach (var def in KeyCapacityDefs)
                {
                    try
                    {
                        float level = p.health.capacities?.GetLevel(def) ?? 0f;
                        capacities[def.defName] = Mathf.Clamp01(level);
                    }
                    catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] capacity {def.defName}: {e.Message}"); }
                }
            }

            var injuries = new List<HealthEntry>();
            var hediffs = p.health.hediffSet?.hediffs;
            if (hediffs != null)
            {
                foreach (var h in hediffs)
                {
                    if (h == null || !h.Visible) continue;
                    var tendQuality = 0f;
                    if (h is Hediff tendable) tendQuality = tendable.TendPriority;
                    var immunity = 0f;
                    var immunizable = h.TryGetComp<HediffComp_Immunizable>();
                    if (immunizable != null) immunity = immunizable.Immunity;
                    var compDisappears = h.TryGetComp<HediffComp_Disappears>() != null;

                    injuries.Add(new HealthEntry
                    {
                        Label = h.def?.label ?? h.LabelCap,
                        Part = h.Part?.Label ?? wholeBodyLabel,
                        Severity = h.Severity,
                        IsBleeding = h.Bleeding,
                        IsPermanent = h.IsPermanent(),
                        IsInfection = h.def?.isInfection ?? false,
                        TendQuality = tendQuality,
                        AgeTicks = h.ageTicks,
                        Immunity = immunity,
                        CompDisappears = compDisappears
                    });
                }
            }

            string painTier = Framework.SemanticLabels.MapPainTier(summaryPain);
            string bleedTier = Framework.SemanticLabels.MapBleedSeverity(summaryBleedRate);
            var capacityTiers = new Dictionary<string, string>();
            foreach (var kv in capacities)
                capacityTiers[kv.Key] = Framework.SemanticLabels.MapCapacityTier(kv.Value);

            return new HealthSection
            {
                SummaryPain = summaryPain,
                SummaryBleedRate = summaryBleedRate,
                PainTier = painTier,
                BleedTier = bleedTier,
                Capacities = capacities,
                CapacityTiers = capacityTiers,
                Injuries = injuries
            };
        }

        private static MoodSection MapMood(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike) return new MoodSection
            {
                Traits = new List<TraitEntry>(),
                ActiveThoughts = new List<ThoughtEntry>()
            };

            var moodLevel = p.needs?.mood?.CurLevelPercentage ?? 0f;
            string mentalStateLabel = null;
            if (p.InMentalState)
                mentalStateLabel = p.MentalState?.def?.label ?? p.MentalState?.InspectLine;

            var traits = new List<TraitEntry>();
            var storyTraits = p.story?.traits?.allTraits;
            if (storyTraits != null)
            {
                foreach (var trait in storyTraits)
                {
                    if (trait == null) continue;
                    traits.Add(new TraitEntry
                    {
                        DefName = trait.def?.defName ?? string.Empty,
                        Label = trait.LabelCap,
                        Degree = trait.Degree
                    });
                }
            }

            var activeThoughts = new List<ThoughtEntry>();
            var allThoughts = new List<Thought>();
            try { p.needs?.mood?.thoughts?.GetAllMoodThoughts(allThoughts); }
                catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] GetAllMoodThoughts: {e.Message}"); }
            foreach (var t in allThoughts)
            {
                if (t == null) continue;
                float offset = 0f;
                try { offset = t.MoodOffset(); }
                catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] MoodOffset: {e.Message}"); }
                float durationRatio = 1f;
                if (t is Thought_Memory mem)
                {
                    int duration = mem.def?.DurationTicks ?? 0;
                    if (duration > 0)
                    {
                        durationRatio = 1f - (mem.age / (float)duration);
                        durationRatio = Mathf.Clamp01(durationRatio);
                    }
                }
                activeThoughts.Add(new ThoughtEntry
                {
                    Label = t.LabelCap,
                    MoodOffset = offset,
                    DurationRatio = durationRatio
                });
            }

            return new MoodSection
            {
                MoodLevel = moodLevel,
                MoodTier = Framework.SemanticLabels.MapMoodTier(moodLevel),
                MentalStateLabel = mentalStateLabel,
                Traits = traits,
                ActiveThoughts = activeThoughts
            };
        }

        private static SkillsSection MapSkills(Pawn p)
        {
            if (p?.skills == null) return new SkillsSection { AllSkills = new List<SkillEntry>() };

            var list = new List<SkillEntry>();
            var skills = p.skills.skills;
            if (skills != null)
            {
                foreach (var sr in skills)
                {
                    if (sr == null || sr.def == null) continue;
                    Passion passion;
                    try { passion = sr.passion; }
                    catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] passion {sr.def?.defName}: {e.Message}"); passion = Passion.None; }
                    list.Add(new SkillEntry
                    {
                        DefName = sr.def.defName,
                        Label = sr.def.label ?? sr.def.defName,
                        Level = sr.Level,
                        Passion = passion.ToString(),
                        HasPassion = passion != Passion.None,
                        TotallyDisabled = sr.TotallyDisabled
                    });
                }
            }
            return new SkillsSection { AllSkills = list };
        }

        private static NeedsSection MapNeeds(Pawn p)
        {
            if (p?.needs == null) return new NeedsSection { AllNeeds = new List<NeedEntry>() };

            var allNeedsList = new List<NeedEntry>();
            var allNeeds = p.needs.AllNeeds;
            if (allNeeds != null)
            {
                foreach (var need in allNeeds)
                {
                    if (need == null) continue;
                    try
                    {
                        float cur = need.CurLevelPercentage;
                        float thresholdLow = need.def?.baseLevel > 0f ? need.def.baseLevel : 0.3f;
                        bool critical = cur < thresholdLow * 0.5f || cur < 0.1f;
                        allNeedsList.Add(new NeedEntry
                        {
                            DefName = need.def?.defName ?? need.LabelCap,
                            Label = need.LabelCap,
                            CurLevel = cur,
                            ThresholdLow = thresholdLow,
                            IsCritical = critical,
                            NeedUrgency = Framework.SemanticLabels.MapNeedUrgency(need.def?.defName, cur)
                        });
                    }
                    catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] need {need.def?.defName}: {e.Message}"); }
                }
            }
            return new NeedsSection { AllNeeds = allNeedsList };
        }

        private static ActivitySection MapActivity(Pawn p)
        {
            if (p == null || p.jobs == null) return new ActivitySection { Activities = new List<ActivityEntry>() };

            string posture = null;
            try { posture = p.GetPosture().ToString(); }
                catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] GetPosture: {e.Message}"); }

            var activities = new List<ActivityEntry>();
            var jobQueue = p.jobs.jobQueue;
            if (jobQueue != null)
            {
                foreach (var job in jobQueue)
                {
                    if (job?.job == null) continue;
                    try
                    {
                        activities.Add(new ActivityEntry
                        {
                            JobDefName = job.job.def?.defName ?? string.Empty,
                            JobReport = job.job.GetReport(p)
                        });
                    }
                    catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] jobQueue report: {e.Message}"); }
                }
            }

            var curJob = p.CurJob;
            if (curJob != null)
            {
                try
                {
                    activities.Insert(0, new ActivityEntry
                    {
                        JobDefName = curJob.def?.defName ?? string.Empty,
                        JobReport = curJob.GetReport(p)
                    });
                }
                catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] curJob report: {e.Message}"); }
            }

            return new ActivitySection { Posture = posture, Activities = activities };
        }

        private static GearSection MapGear(Pawn p)
        {
            if (p == null) return new GearSection
            {
                WornGear = new List<GearItem>(),
                Inventory = new List<GearItem>()
            };

            var worn = p.apparel?.WornApparel.Select(MapGearItem).ToList() ?? new List<GearItem>();
            var inventory = p.inventory?.innerContainer.Select(MapGearItem).ToList() ?? new List<GearItem>();
            return new GearSection { WornGear = worn, Inventory = inventory };
        }

        private static GearItem MapGearItem(Thing thing)
        {
            string quality = thing.TryGetQuality(out var qc) ? qc.ToString() : QualityCategory.Normal.ToString();
            float durability = thing.def.useHitPoints ? (float)thing.HitPoints / thing.MaxHitPoints : 1f;
            return new GearItem
            {
                Name = thing.LabelCap,
                Quality = quality,
                Durability = durability,
                ConditionLabel = Framework.SemanticLabels.MapGearCondition(durability),
                Count = thing.stackCount
            };
        }

        private static BackstorySection MapBackstory(Pawn p)
        {
            if (p?.story == null) return new BackstorySection();

            BackstoryEntry? childhood = p.story.Childhood != null ? new BackstoryEntry
            {
                Title = p.story.Childhood.title,
                Description = p.story.Childhood.description
            } : (BackstoryEntry?)null;

            BackstoryEntry? adulthood = p.story.Adulthood != null ? new BackstoryEntry
            {
                Title = p.story.Adulthood.title,
                Description = p.story.Adulthood.description
            } : (BackstoryEntry?)null;

            return new BackstorySection { Childhood = childhood, Adulthood = adulthood };
        }

        private static SocialSection MapSocial(Pawn p)
        {
            if (p?.relations == null) return new SocialSection
            {
                Relations = new List<SocialRelation>(),
                ColonyOpinionAverage = 0f
            };

            var relations = new List<SocialRelation>();
            var directRelations = p.relations.DirectRelations;
            if (directRelations != null)
            {
                foreach (var dr in directRelations)
                {
                    if (dr?.otherPawn == null) continue;
                    try
                    {
                        var other = dr.otherPawn;
                        float opinion = p.relations.OpinionOf(other);
                        bool reciprocal = other.relations?.DirectRelations?
                            .Any(r => r.otherPawn == p && r.def == dr.def) ?? false;
                        relations.Add(new SocialRelation
                        {
                            OtherID = other.ThingID ?? "?",
                            OtherName = other.Name?.ToStringShort ?? other.LabelShortCap ?? "?",
                            RelationType = dr.def?.defName ?? "Unknown",
                            Opinion = opinion,
                            OpinionTier = Framework.SemanticLabels.MapOpinionTier(opinion),
                            IsReciprocal = reciprocal
                        });
                    }
                    catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] social relation {dr.def?.defName}: {e.Message}"); }
                }
            }

            float colonyAvg = CalculateColonyOpinionAverage(p);
            return new SocialSection { Relations = relations, ColonyOpinionAverage = colonyAvg };
        }

        private static float CalculateColonyOpinionAverage(Pawn p)
        {
            try
            {
                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists == null || colonists.Count == 0) return 0f;
                float sum = 0f;
                int count = 0;
                foreach (var c in colonists)
                {
                    if (c == p) continue;
                    if (c?.relations == null) continue;
                    try { sum += c.relations.OpinionOf(p); count++; }
                        catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] OpinionOf({c.ThingID}): {e.Message}"); }
                }
                return count > 0 ? sum / count : 0f;
            }
            catch (Exception e) { Log.Warning($"[RimLife.CharacterCardMapper] CalculateColonyOpinionAverage: {e.Message}"); return 0f; }
        }

        private static PerspectiveSection MapPerspective(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
                return new PerspectiveSection { VisiblePawnSnapshots = new List<PawnRelationSnapshot>() };

            Map map = pawn.Map;
            var visiblePawns = new List<PawnRelationSnapshot>();
            var allPawns = map.mapPawns.AllPawnsSpawned;
            foreach (var target in allPawns)
            {
                if (target == pawn) continue;
                if (target?.Position == null) continue;
                float dist = target.Position.DistanceTo(pawn.Position);
                if (dist > 26f) continue;
                if (!GenSight.LineOfSight(pawn.Position, target.Position, map, skipFirstCell: true)) continue;

                string name = target.Name?.ToStringShort ?? target.LabelShortCap ?? "?";
                string defName = target.def?.defName ?? "Unknown";
                string id = target.ThingID;
                if (id != null)
                    visiblePawns.Add(new PawnRelationSnapshot { ID = id, Name = name, DefName = defName, Distance = dist });
            }

            return new PerspectiveSection { VisiblePawnSnapshots = visiblePawns };
        }

        private static PsychologySection MapPsychology(CharacterCard card, Pawn p)
        {
            // 从 Traits 计算基础五维向量（需要 DefDatabase<TraitDef>）
            var baseVec = BigFiveVector.Zero;
            if (card.Mood?.Traits != null)
            {
                foreach (var t in card.Mood.Traits)
                {
                    if (t.DefName == null) continue;
                    TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(t.DefName);
                    if (def == null) continue;
                    var ext = def.GetModExtension<PersonalityExtension>();
                    if (ext == null) continue;
                    PersonalityEntry match = ext.GetByDegree(t.Degree);
                    if (match == null) continue;
                    var vec = new BigFiveVector(match.openness, match.conscientiousness, match.extraversion, match.agreeableness, match.neuroticism);
                    if (!vec.IsZero())
                    {
                        baseVec = new BigFiveVector(
                            baseVec.Openness + vec.Openness,
                            baseVec.Conscientiousness + vec.Conscientiousness,
                            baseVec.Extraversion + vec.Extraversion,
                            baseVec.Agreeableness + vec.Agreeableness,
                            baseVec.Neuroticism + vec.Neuroticism);
                    }
                }
            }

            return new PsychologySection
            {
                Openness = MapPsychologyLevel(baseVec.Openness, true),
                Conscientiousness = MapPsychologyLevel(baseVec.Conscientiousness, true),
                Extraversion = MapPsychologyLevel(baseVec.Extraversion, true),
                Agreeableness = MapPsychologyLevel(baseVec.Agreeableness, true),
                Neuroticism = MapPsychologyLevel(baseVec.Neuroticism, true),
                BaseVector = baseVec,
                TotalVector = baseVec,
                ExternalVectors = new Dictionary<string, BigFiveVector>()
            };
        }

        private static string MapPsychologyLevel(int sum, bool hadContribution)
        {
            if (!hadContribution) return "Undefined";
            if (sum <= -4) return "VeryLow";
            if (sum <= -1) return "Low";
            if (sum == 0) return "Average";
            if (sum <= 3) return "High";
            return "VeryHigh";
        }

        // ================================================================
        // Memory Section 映射
        // ================================================================

        private static MemorySection MapMemory(Pawn p)
        {
            if (p?.health?.hediffSet == null)
                return null;

            try
            {
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("PawnProMemory");
                if (hediffDef == null) return null;

                var hediff = p.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (hediff == null) return null;

                var comp = hediff.TryGetComp<HediffComp_PawnMemory>();
                if (comp == null) return null;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                return new MemorySection
                {
                    Snapshot = comp.CreateSnapshot(currentTick)
                };
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.CharacterCardMapper] MapMemory failed: {e.Message}");
                return null;
            }
        }

        // ================================================================
        // 辅助方法
        // ================================================================

        private static string GetPawnType(Pawn p)
        {
            if (p.RaceProps.Humanlike) return "Character";
            if (p.RaceProps.Animal) return "Animal";
            if (p.RaceProps.IsMechanoid) return "Mechanoid";
            if (p.RaceProps.Insect) return "Insect";
            return "Other";
        }

        private static string GetPawnRelation(Pawn p)
        {
            if (p.Faction == null) return "Other";
            if (p.Faction == Faction.OfPlayer) return "OurParty";
            var rel = p.Faction.PlayerRelationKind;
            switch (rel)
            {
                case FactionRelationKind.Ally: return "Ally";
                case FactionRelationKind.Hostile: return "Enemy";
                default: return "Neutral";
            }
        }
    }
}
