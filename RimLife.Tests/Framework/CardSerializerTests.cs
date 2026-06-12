using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework.Mcp;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// CardSerializer 自检测试。覆盖所有 Card DTO 类型的序列化路径。
    /// </summary>
    public class CardSerializerTests
    {
        // ================================================================
        // IGameEvent
        // ================================================================

        [Fact]
        public void SerializeEvent_BasicEvent_ContainsExpectedFields()
        {
            var evt = new TestGameEvent
            {
                EventID = "test_001",
                DefName = "TestEvent",
                Tags = new List<string> { "Test", "Combat" },
                Tick = 5000,
                Severity = "Major",
                MapHint = "Map:123",
                Actors = new List<EventActorRef>
                {
                    EventActorRef.Pawn("pawn_1", "Alice", "Initiator")
                },
                Payload = new Dictionary<string, string> { ["damage"] = "20" }
            };

            var json = CardSerializer.SerializeEvent(evt);

            Assert.Contains("\"eventId\":\"test_001\"", json);
            Assert.Contains("\"defName\":\"TestEvent\"", json);
            Assert.Contains("\"Test\"", json);
            Assert.Contains("\"Combat\"", json);
            Assert.Contains("\"tick\":5000", json);
            Assert.Contains("\"severity\":\"Major\"", json);
            Assert.Contains("\"pawn_1\"", json);
            Assert.Contains("\"Alice\"", json);
            Assert.Contains("\"damage\":\"20\"", json);
        }

        [Fact]
        public void SerializeEventList_Empty_ReturnsEmptyArray()
        {
            var json = CardSerializer.SerializeEventList(new List<IGameEvent>());
            Assert.Equal("[]", json);
        }

        [Fact]
        public void SerializeEventList_Multiple_ReturnsArray()
        {
            var events = new List<IGameEvent>
            {
                new TestGameEvent { EventID = "e1", DefName = "A", Tick = 1 },
                new TestGameEvent { EventID = "e2", DefName = "B", Tick = 2 }
            };
            var json = CardSerializer.SerializeEventList(events);

            Assert.StartsWith("[", json);
            Assert.Contains("e1", json);
            Assert.Contains("e2", json);
            Assert.EndsWith("]", json);
        }

        // ================================================================
        // ColonyContext
        // ================================================================

        [Fact]
        public void SerializeColonyContext_FullContext_ContainsAllSections()
        {
            var ctx = new ColonyContext
            {
                CurrentTick = 10000,
                Season = "Summer",
                TimeOfDay = "Day",
                Year = 2,
                PopulationAlive = 5,
                WealthTotal = 15000f,
                FoodStatus = "Abundant",
                PowerStatus = "Stable",
                MoraleAverage = 0.65f,
                MoraleTier = "Good",
                TechLevel = "Industrial",
                StorytellerName = "Cassandra Classic",
                Difficulty = "Strive to Survive",
                ActiveThreats = new List<string> { "ActiveHostiles:3" },
                Colonists = new List<ColonistSummary>
                {
                    new ColonistSummary { ID = "c1", Name = "Alice", MoodTier = "Good", CurrentJob = "Haul" }
                },
                FactionRelations = new List<FactionStanding>
                {
                    new FactionStanding { FactionName = "Pirates", Goodwill = -80f, RelationLabel = "Hostile" }
                }
            };

            var json = CardSerializer.SerializeColonyContext(ctx);

            Assert.Contains("\"season\":\"Summer\"", json);
            Assert.Contains("\"populationAlive\":5", json);
            Assert.Contains("\"wealthTotal\":15000", json);
            Assert.Contains("\"foodStatus\":\"Abundant\"", json);
            Assert.Contains("\"moraleTier\":\"Good\"", json);
            Assert.Contains("\"techLevel\":\"Industrial\"", json);
            Assert.Contains("\"storytellerName\":\"Cassandra Classic\"", json);
            Assert.Contains("\"ActiveHostiles:3\"", json);
            Assert.Contains("\"Alice\"", json);
            Assert.Contains("\"Pirates\"", json);
        }

        [Fact]
        public void SerializeColonyContext_Null_ReturnsEmptyObject()
        {
            var json = CardSerializer.SerializeColonyContext(null);
            Assert.Equal("{}", json);
        }

        // ================================================================
        // CharacterCard
        // ================================================================

        [Fact]
        public void SerializeCharacterCard_Basic_AlwaysIncludesMetadata()
        {
            var card = new CharacterCard
            {
                ID = "pawn_1",
                Name = "Alice",
                FullName = "Alice Smith",
                DefName = "Human",
                AgeBiologicalYears = 28.5f,
                Gender = "Female",
                PawnType = "Character",
                PawnRelation = "OurParty",
                IsDead = false,
                IsDowned = false,
                IsAwake = true
            };

            var json = CardSerializer.SerializeCharacterCard(card, null);

            Assert.Contains("\"id\":\"pawn_1\"", json);
            Assert.Contains("\"name\":\"Alice\"", json);
            Assert.Contains("\"sections\":[]", json); // no sections populated
        }

        [Fact]
        public void SerializeCharacterCard_WithSections_IncludesRequestedSections()
        {
            var card = new CharacterCard
            {
                ID = "pawn_1",
                Name = "Bob",
                Skills = new SkillsSection
                {
                    AllSkills = new List<SkillEntry>
                    {
                        new SkillEntry { DefName = "Shooting", Label = "Shooting", Level = 12, Passion = "Major" }
                    }
                },
                Mood = new MoodSection
                {
                    MoodLevel = 0.72f,
                    MoodTier = "Good",
                    Traits = new List<TraitEntry>(),
                    ActiveThoughts = new List<ThoughtEntry>()
                }
            };

            var json = CardSerializer.SerializeCharacterCard(card, "skills,mood");

            Assert.Contains("\"Shooting\"", json);
            Assert.Contains("\"level\":12", json);
            Assert.Contains("\"moodLevel\":0.72", json);
            Assert.Contains("\"sections\":[\"mood\",\"skills\"]", json);
            // 不应包含未请求的 section
            Assert.DoesNotContain("\"health\"", json);
        }

        [Fact]
        public void SerializeCharacterCard_AllSections_WhenEmpty()
        {
            var card = CreateFullTestCard();
            var json = CardSerializer.SerializeCharacterCard(card, "");

            Assert.Contains("\"health\"", json);
            Assert.Contains("\"mood\"", json);
            Assert.Contains("\"skills\"", json);
            Assert.Contains("\"needs\"", json);
            Assert.Contains("\"backstory\"", json);
            Assert.Contains("\"psychology\"", json);
        }

        // ================================================================
        // ObjectiveCard
        // ================================================================

        [Fact]
        public void SerializeObjective_Basic_ContainsFields()
        {
            var obj = new ObjectiveCard
            {
                ID = "quest_1",
                Title = "Rescue the prisoner",
                Description = "A prisoner needs rescue.",
                Status = "Active",
                Source = "QuestSystem",
                Steps = new List<ObjectiveStepEntry>
                {
                    new ObjectiveStepEntry { Label = "Reach the camp", IsCompleted = true },
                    new ObjectiveStepEntry { Label = "Escort to safety", IsCompleted = false }
                }
            };

            var json = CardSerializer.SerializeObjective(obj);

            Assert.Contains("\"id\":\"quest_1\"", json);
            Assert.Contains("\"title\":\"Rescue the prisoner\"", json);
            Assert.Contains("\"status\":\"Active\"", json);
            Assert.Contains("\"Reach the camp\"", json);
            Assert.Contains("\"isCompleted\":true", json);
        }

        [Fact]
        public void SerializeObjectiveList_Empty_ReturnsEmptyArray()
        {
            var json = CardSerializer.SerializeObjectiveList(new List<ObjectiveCard>());
            Assert.Equal("[]", json);
        }

        // ================================================================
        // EnvironmentCard
        // ================================================================

        [Fact]
        public void SerializeEnvironment_Outdoors_IncludesWeather()
        {
            var env = new EnvironmentCard
            {
                Type = "Outdoors",
                Temperature = 25f,
                LightLevel = 0.8f,
                ThermalComfort = "Comfortable",
                LightLabel = "Bright",
                Weather = new WeatherSection
                {
                    Label = "Rain",
                    Description = "It's raining.",
                    IsRain = true,
                    WindSpeed = 1.5f
                }
            };

            var json = CardSerializer.SerializeEnvironment(env);

            Assert.Contains("\"type\":\"Outdoors\"", json);
            Assert.Contains("\"temperature\":25.0", json);
            Assert.Contains("\"Rain\"", json);
            Assert.DoesNotContain("\"room\"", json);
        }

        [Fact]
        public void SerializeEnvironment_Indoors_IncludesRoom()
        {
            var env = new EnvironmentCard
            {
                Type = "Indoors",
                Temperature = 21f,
                LightLevel = 0.5f,
                Room = new RoomSection
                {
                    RoleLabel = "Bedroom",
                    BaseStats = new RoomStats
                    {
                        Impressiveness = 65f,
                        Beauty = 3f,
                        Space = 25f,
                        Cleanliness = 0.5f,
                        Wealth = 200f
                    },
                    Tags = new List<string> { " cramped" }
                }
            };

            var json = CardSerializer.SerializeEnvironment(env);

            Assert.Contains("\"type\":\"Indoors\"", json);
            Assert.Contains("\"roleLabel\":\"Bedroom\"", json);
            Assert.Contains("\"impressiveness\":65", json);
        }

        // ================================================================
        // InteractionRecord
        // ================================================================

        [Fact]
        public void SerializeInteraction_Basic_ContainsFields()
        {
            var rec = new InteractionRecord
            {
                Tick = 5000,
                InitiatorID = "pawn_a",
                RecipientID = "pawn_b",
                InteractionDef = "Insult",
                Outcome = "Slighted"
            };

            var json = CardSerializer.SerializeInteraction(rec);

            Assert.Contains("\"tick\":5000", json);
            Assert.Contains("\"initiatorId\":\"pawn_a\"", json);
            Assert.Contains("\"recipientId\":\"pawn_b\"", json);
            Assert.Contains("\"interactionDef\":\"Insult\"", json);
            Assert.Contains("\"outcome\":\"Slighted\"", json);
        }

        [Fact]
        public void SerializeInteractionList_Multiple_ReturnsArray()
        {
            var records = new List<InteractionRecord>
            {
                new InteractionRecord { Tick = 1, InitiatorID = "a", RecipientID = "b" },
                new InteractionRecord { Tick = 2, InitiatorID = "b", RecipientID = "a" }
            };
            var json = CardSerializer.SerializeInteractionList(records);

            Assert.StartsWith("[", json);
            Assert.Contains("\"tick\":1", json);
            Assert.Contains("\"tick\":2", json);
            Assert.EndsWith("]", json);
        }

        // ================================================================
        // ColonistSummary
        // ================================================================

        [Fact]
        public void SerializeColonistSummaryList_Multiple_ReturnsArray()
        {
            var colonists = new List<ColonistSummary>
            {
                new ColonistSummary { ID = "c1", Name = "Alice", MoodTier = "Good" },
                new ColonistSummary { ID = "c2", Name = "Bob", MoodTier = "Neutral", IsDowned = true }
            };
            var json = CardSerializer.SerializeColonistSummaryList(colonists);

            Assert.Contains("\"id\":\"c1\"", json);
            Assert.Contains("\"Alice\"", json);
            Assert.Contains("\"isDowned\":true", json);
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static CharacterCard CreateFullTestCard()
        {
            return new CharacterCard
            {
                ID = "test_full",
                Name = "TestPawn",
                DefName = "Human",
                Gender = "Male",
                PawnType = "Character",
                PawnRelation = "OurParty",
                AgeBiologicalYears = 30f,
                Health = new HealthSection
                {
                    PainTier = "Low",
                    BleedTier = "None",
                    Capacities = new Dictionary<string, float>(),
                    CapacityTiers = new Dictionary<string, string>(),
                    Injuries = new List<HealthEntry>()
                },
                Mood = new MoodSection
                {
                    MoodLevel = 0.5f,
                    MoodTier = "Neutral",
                    Traits = new List<TraitEntry>(),
                    ActiveThoughts = new List<ThoughtEntry>()
                },
                Skills = new SkillsSection { AllSkills = new List<SkillEntry>() },
                Needs = new NeedsSection { AllNeeds = new List<NeedEntry>() },
                Activity = new ActivitySection { Activities = new List<ActivityEntry>() },
                Gear = new GearSection { WornGear = new List<GearItem>(), Inventory = new List<GearItem>() },
                Backstory = new BackstorySection(),
                Social = new SocialSection { Relations = new List<SocialRelation>() },
                Perspective = new PerspectiveSection { VisiblePawnSnapshots = new List<PawnRelationSnapshot>() },
                Psychology = new PsychologySection
                {
                    BaseVector = BigFiveVector.Zero,
                    TotalVector = BigFiveVector.Zero,
                    ExternalVectors = new Dictionary<string, BigFiveVector>()
                }
            };
        }

        private class TestGameEvent : IGameEvent
        {
            public string EventID { get; set; }
            public string DefName { get; set; }
            public IReadOnlyList<string> Tags { get; set; } = new List<string>();
            public int Tick { get; set; }
            public string Severity { get; set; } = "Minor";
            public IReadOnlyList<EventActorRef> Actors { get; set; } = new List<EventActorRef>();
            public string MapHint { get; set; } = "";
            public IDictionary<string, string> Payload { get; set; } = new Dictionary<string, string>();
        }
    }
}
