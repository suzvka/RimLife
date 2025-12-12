using System;
using Verse;

namespace RimLife
{
    public sealed class SlotRealizer : ISlotRealizer
    {
        public string RealizeSlot(SlotRequest slot, Fact fact)
        {
            switch (slot.Type)
            {
                case SlotType.SubjectNp:
                    return RealizeSubject(slot, fact);
                case SlotType.DomainNp:
                    return RealizeDomain(slot, fact);
                case SlotType.AdjIntensity:
                    return "Test:AdjIntensity";
                case SlotType.AdjQuality:
                    return "Test:AdjQuality";
                case SlotType.AdvDegree:
                    return "Test:AdvDegree";
                case SlotType.StateVerb:
                    return "Test:StateVerb";
                case SlotType.RiskClause:
                    return "Test:RiskClause";
                case SlotType.TimeAdverb:
                    return "Test:TimeAdverb";
                case SlotType.Connective:
                    return "Test:Connective";
                default:
                    return "Test";
            }
        }

        private string RealizeSubject(SlotRequest slot, Fact fact)
        {
            var pawn = fact?.Subject;
            if (pawn == null)
                return string.Empty;

            return !string.IsNullOrWhiteSpace(pawn.Name) ? pawn.Name : pawn.FullName ?? string.Empty;
        }

        private string RealizeDomain(SlotRequest slot, Fact fact)
        {
            if (fact?.SourcePayload is HealthNarrative health)
            {
                if (string.Equals(slot.Name, "NOUN_Part", StringComparison.OrdinalIgnoreCase))
                    return ResolveHealthBodyPart(health);

                if (string.Equals(slot.Name, "NOUN_Injury", StringComparison.OrdinalIgnoreCase))
                    return ResolveHealthInjuryNoun(health);
            }

            if (fact?.Topic == FactTopic.Health)
            {
                if (TryTranslate("RimLife.Health.Domain.Injury", out var localized))
                    return localized;

                return "injury";
            }

            return string.Empty;
        }

        private static string ResolveHealthInjuryNoun(HealthNarrative health)
        {
            if (!string.IsNullOrWhiteSpace(health.Noun))
                return health.Noun;

            if (TryTranslate("RimLife.Health.Domain.Injury", out var localized))
                return localized;

            return "injury";
        }

        private static string ResolveHealthBodyPart(HealthNarrative health)
        {
            if (health?.RelatedNouns != null &&
                health.RelatedNouns.TryGetValue("OnPart", out var part) &&
                !string.IsNullOrWhiteSpace(part))
            {
                return part;
            }

            return TryTranslate("RimLife.Health.Body.Generic", out var localized)
                ? localized
                : "body";
        }

        private static bool TryTranslate(string key, out string text)
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

    }
}
