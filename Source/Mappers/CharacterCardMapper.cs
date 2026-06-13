using RimLife.Cards;
using RimWorld;
using System;
using Verse;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld Pawn 提取身份元数据并组装 CharacterCard。
    /// 仅负责身份信息，各维度的语义描述由 IPawnPromptProvider 动态生成。
    /// </summary>
    public static class CharacterCardMapper
    {
        /// <summary>
        /// 创建包含基本元数据的 CharacterCard（仅身份信息）。
        /// 必须在主线程上调用。
        /// </summary>
        public static CharacterCard CreateBasic(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            return new CharacterCard
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
        }

        // ================================================================
        // 辅助方法
        // ================================================================

        public static string GetPawnType(Pawn p)
        {
            if (p.RaceProps.Humanlike) return "Character";
            if (p.RaceProps.Animal) return "Animal";
            if (p.RaceProps.IsMechanoid) return "Mechanoid";
            if (p.RaceProps.Insect) return "Insect";
            return "Other";
        }

        public static string GetPawnRelation(Pawn p)
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
