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
            // 1) 只看 Fact 自己的 domainLexemes
            if (fact != null &&
                fact.TryGetDomainLexeme(slot.Name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            // 2) 不再做任何“领域特例”逻辑，这些都应该在构建 Fact 时完成
            return string.Empty;
        }

    }
}
