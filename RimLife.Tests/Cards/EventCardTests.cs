using System.Collections.Generic;
using RimLife.Cards;
using Xunit;

namespace RimLife.Tests.Cards
{
    /// <summary>
    /// EventCard / EventActorRef / BigFiveVector DTO 断言测试。
    /// 纯数据结构测试，验证工厂方法和值语义正确性。
    /// </summary>
    public class EventCardTests
    {
        // ================================================================
        // EventActorRef
        // ================================================================

        [Fact]
        public void Pawn_Factory_SetsAllFields()
        {
            var actor = EventActorRef.Pawn("pawn_001", "Alice", "Initiator");

            Assert.Equal("pawn_001", actor.ID);
            Assert.Equal("Alice", actor.Name);
            Assert.Equal("Initiator", actor.Role);
            Assert.Equal("Pawn", actor.RefType);
        }

        [Fact]
        public void Pawn_NullInputs_UsesFallback()
        {
            var actor = EventActorRef.Pawn(null, null, null);

            Assert.Equal("?", actor.ID);
            Assert.Equal("?", actor.Name);
            Assert.Equal("Bystander", actor.Role);
            Assert.Equal("Pawn", actor.RefType);
        }

        [Fact]
        public void Faction_Factory_SetsAllFields()
        {
            var actor = EventActorRef.Faction("PirateBand", "Hostile");

            Assert.Equal("PirateBand", actor.ID);
            Assert.Equal("PirateBand", actor.Name);
            Assert.Equal("Hostile", actor.Role);
            Assert.Equal("Faction", actor.RefType);
        }

        [Fact]
        public void Faction_NullInputs_UsesFallback()
        {
            var actor = EventActorRef.Faction(null, null);

            Assert.Equal("?", actor.ID);
            Assert.Equal("?", actor.Name);
            Assert.Equal("Bystander", actor.Role);
        }

        // ================================================================
        // BigFiveVector
        // ================================================================

        [Fact]
        public void BigFiveVector_Zero_AllComponentsZero()
        {
            var zero = BigFiveVector.Zero;

            Assert.Equal(0, zero.Openness);
            Assert.Equal(0, zero.Conscientiousness);
            Assert.Equal(0, zero.Extraversion);
            Assert.Equal(0, zero.Agreeableness);
            Assert.Equal(0, zero.Neuroticism);
            Assert.True(zero.IsZero());
        }

        [Fact]
        public void BigFiveVector_Constructor_SetsComponents()
        {
            var vec = new BigFiveVector(1, 2, 3, 4, 5);

            Assert.Equal(1, vec.Openness);
            Assert.Equal(2, vec.Conscientiousness);
            Assert.Equal(3, vec.Extraversion);
            Assert.Equal(4, vec.Agreeableness);
            Assert.Equal(5, vec.Neuroticism);
            Assert.False(vec.IsZero());
        }

        [Fact]
        public void BigFiveVector_IsZero_DetectsPartialZero()
        {
            var partial = new BigFiveVector(0, 1, 0, 0, 0);
            Assert.False(partial.IsZero());
        }

        [Fact]
        public void BigFiveVector_ToString_ContainsAllComponents()
        {
            var vec = new BigFiveVector(1, -2, 3, 0, 5);
            var str = vec.ToString();

            Assert.Contains("O=1", str);
            Assert.Contains("C=-2", str);
            Assert.Contains("E=3", str);
            Assert.Contains("A=0", str);
            Assert.Contains("N=5", str);
        }

        // ================================================================
        // EventCategory
        // ================================================================

        [Fact]
        public void EventCategory_AllValues_CanBeParsed()
        {
            var values = new[] { "Combat", "Nature", "Social", "Quest", "Health", "Economy", "Anomaly" };
            foreach (var v in values)
            {
                Assert.True(System.Enum.TryParse<EventCategory>(v, out _), $"Should parse '{v}'");
            }
        }

        // ================================================================
        // CharacterCard DTO
        // ================================================================

        [Fact]
        public void CharacterCard_Defaults_AllowNullSections()
        {
            var card = new CharacterCard();

            Assert.Null(card.Health);
            Assert.Null(card.Mood);
            Assert.Null(card.Skills);
            Assert.Null(card.Needs);
            Assert.Null(card.Activity);
            Assert.Null(card.Gear);
            Assert.Null(card.Backstory);
            Assert.Null(card.Social);
            Assert.Null(card.Perspective);
            Assert.Null(card.Psychology);
        }

        [Fact]
        public void CharacterCard_BasicFields_ReadWrite()
        {
            var card = new CharacterCard
            {
                ID = "pawn_001",
                Name = "Alice",
                FullName = "Alice Cooper",
                DefName = "Human",
                FactionLabel = "Colony",
                AgeBiologicalYears = 25.5f,
                Gender = "Female",
                PawnType = "Character",
                PawnRelation = "OurParty",
                IsDead = false,
                IsDowned = false,
                IsAwake = true
            };

            Assert.Equal("pawn_001", card.ID);
            Assert.Equal("Alice", card.Name);
            Assert.Equal("Alice Cooper", card.FullName);
            Assert.Equal(25.5f, card.AgeBiologicalYears);
            Assert.Equal("Female", card.Gender);
            Assert.True(card.IsAwake);
            Assert.False(card.IsDead);
        }

        // ================================================================
        // ColonyContext DTO
        // ================================================================

        [Fact]
        public void ColonyContext_Defaults_ZeroValues()
        {
            var ctx = new ColonyContext();

            Assert.Equal(0, ctx.CurrentTick);
            Assert.Equal(0, ctx.Year);
            Assert.Equal(0, ctx.PopulationAlive);
            Assert.Equal(0f, ctx.WealthTotal);
            Assert.Null(ctx.Season);
            Assert.Null(ctx.TimeOfDay);
            Assert.Null(ctx.Colonists);
            Assert.Null(ctx.FactionRelations);
            Assert.Null(ctx.ActiveThreats);
        }

        // ================================================================
        // EnvironmentCard DTO
        // ================================================================

        [Fact]
        public void EnvironmentCard_Defaults_NullSections()
        {
            var card = new EnvironmentCard();

            Assert.Null(card.Type);
            Assert.Null(card.Room);
            // WeatherSection is a struct (value type), cannot be null
            Assert.Null(card.ThingSummary);
        }

        // ================================================================
        // ObjectiveCard DTO
        // ================================================================

        [Fact]
        public void ObjectiveCard_Defaults_NullableDeadline()
        {
            var card = new ObjectiveCard();

            Assert.Null(card.ID);
            Assert.Null(card.DeadlineTick);
            Assert.Null(card.Steps);
        }

        [Fact]
        public void ObjectiveStepEntry_Defaults_FalseCompleted()
        {
            var step = new ObjectiveStepEntry();

            Assert.False(step.IsCompleted);
            Assert.Null(step.Label);
        }
    }
}
