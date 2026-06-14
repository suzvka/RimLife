using RimLife;
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

            var json = CardSerializer.Default.SerializeEvent(evt);

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
            var json = CardSerializer.Default.SerializeEventList(new List<IGameEvent>());
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
            var json = CardSerializer.Default.SerializeEventList(events);

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

            var json = CardSerializer.Default.SerializeColonyContext(ctx);

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
            var json = CardSerializer.Default.SerializeColonyContext(null);
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

            var json = CardSerializer.Default.SerializeCharacterCard(card, null, EmptyProvider);

            Assert.Contains("\"id\":\"pawn_1\"", json);
            Assert.Contains("\"name\":\"Alice\"", json);
            Assert.Contains("\"view\":\"static\"", json);
            Assert.Contains("\"prompt\":\"\"", json); // no sections populated
        }

        [Fact]
        public void SerializeCharacterCard_DynamicView_IncludesPerspectiveAndMemory()
        {
            var card = new CharacterCard
            {
                ID = "pawn_dyn",
                Name = "Bob"
            };

            var provider = MakeProviders(new Dictionary<string, string>
            {
                ["skills"] = "射击 12🔥, 格斗 5",
                ["perspective"] = "视野内: Alice(5.2m), Bob(12.1m)",
                ["memory"] = "心态: 警觉, 回顾: 看见Alice在附近, 最近: 发现Alice, STM: 1, LTM: 0"
            });

            var json = CardSerializer.Default.SerializeCharacterCard(card, "dynamic", provider);

            Assert.Contains("\"view\":\"dynamic\"", json);
            Assert.Contains("\"prompt\"", json);
            Assert.Contains("射击", json);
            Assert.Contains("心态: 警觉", json);
            Assert.Contains("看见Alice在附近", json);
            // full 专属不应出现
            Assert.DoesNotContain("STM详情", json);
            Assert.DoesNotContain("LTM详情", json);
        }

        [Fact]
        public void SerializeCharacterCard_FullView_ContainsAllSectionsAndMemoryDetails()
        {
            var card = new CharacterCard
            {
                ID = "test_full",
                Name = "TestPawn",
                DefName = "Human",
                Gender = "Male",
                PawnType = "Character",
                PawnRelation = "OurParty",
                AgeBiologicalYears = 30f
            };

            var provider = MakeProviders(new Dictionary<string, string>
            {
                ["health"] = "疼痛: 低(Low), 流血: 无(None)",
                ["mood"] = "心情: 中等(Neutral)",
                ["skills"] = "射击 12🔥, 格斗 5",
                ["needs"] = "食物: 正常, 休息: 正常",
                ["activity"] = "姿态: 站立, 当前: 搬运",
                ["gear"] = "穿着: 布制衬衫(一般, 50%)",
                ["backstory"] = "童年: 矿场童工——在矿场长大",
                ["social"] = "Alice: 朋友(好感), 殖民地平均: 中等",
                ["psychology"] = "开放性: 高, 尽责性: 中, 外向性: 低, 宜人性: 中, 神经质: 低",
                ["perspective"] = "视野内: Alice(5.2m)",
                ["memory_full"] = "心态: 测试, STM: 1, LTM: 1\n  [STM详情] [100] Obs: 测试\n  [LTM详情] [50] test: 测试"
            });

            var json = CardSerializer.Default.SerializeCharacterCard(card, "full", provider);

            Assert.Contains("\"view\":\"full\"", json);
            Assert.Contains("\"prompt\"", json);
            Assert.Contains("疼痛", json);
            Assert.Contains("心情", json);
            Assert.Contains("射击", json);
            Assert.Contains("矿场", json);
            Assert.Contains("开放性", json);
            Assert.Contains("心态: 测试", json);
            // full 专属结构化列表
            Assert.Contains("STM详情", json);
            Assert.Contains("LTM详情", json);
            // 语义化：原始数值不应出现
            Assert.DoesNotContain("\"summaryPain\"", json);
            Assert.DoesNotContain("\"summaryBleedRate\"", json);
            Assert.DoesNotContain("\"moodLevel\"", json);
            Assert.DoesNotContain("\"baseVector\"", json);
            Assert.DoesNotContain("\"totalVector\"", json);
        }

        [Fact]
        public void SerializeCharacterCard_StaticView_NoMemoryOrPerspective()
        {
            var card = new CharacterCard
            {
                ID = "test_static",
                Name = "TestPawn",
                DefName = "Human",
                Gender = "Male",
                PawnType = "Character",
                PawnRelation = "OurParty"
            };

            var provider = MakeProviders(new Dictionary<string, string>
            {
                ["health"] = "疼痛: 低(Low), 流血: 无(None)",
                ["psychology"] = "开放性: 高, 尽责性: 中",
                ["perspective"] = "视野内: Alice(5.2m)",
                ["memory"] = "心态: 警觉"
            });

            var json = CardSerializer.Default.SerializeCharacterCard(card, "static", provider);

            Assert.Contains("\"view\":\"static\"", json);
            Assert.Contains("\"prompt\"", json);
            Assert.Contains("疼痛", json);
            Assert.Contains("开放性", json);
            // dynamic/full 专属不应出现
            Assert.DoesNotContain("视野内", json);
            Assert.DoesNotContain("心态: 警觉", json);
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

            var json = CardSerializer.Default.SerializeObjective(obj);

            Assert.Contains("\"id\":\"quest_1\"", json);
            Assert.Contains("\"title\":\"Rescue the prisoner\"", json);
            Assert.Contains("\"status\":\"Active\"", json);
            Assert.Contains("\"Reach the camp\"", json);
            Assert.Contains("\"isCompleted\":true", json);
        }

        [Fact]
        public void SerializeObjectiveList_Empty_ReturnsEmptyArray()
        {
            var json = CardSerializer.Default.SerializeObjectiveList(new List<ObjectiveCard>());
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
                Weather = new WeatherInfo
                {
                    Label = "Rain",
                    Description = "It's raining.",
                    IsRain = true,
                    WindSpeed = 1.5f
                }
            };

            var json = CardSerializer.Default.SerializeEnvironment(env);

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
                Room = new RoomInfo
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

            var json = CardSerializer.Default.SerializeEnvironment(env);

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

            var json = CardSerializer.Default.SerializeInteraction(rec);

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
            var json = CardSerializer.Default.SerializeInteractionList(records);

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
            var json = CardSerializer.Default.SerializeColonistSummaryList(colonists);

            Assert.Contains("\"id\":\"c1\"", json);
            Assert.Contains("\"Alice\"", json);
            Assert.Contains("\"isDowned\":true", json);
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static readonly IReadOnlyList<ICharacterContentProvider> EmptyProvider
            = new List<ICharacterContentProvider>();

        private static IReadOnlyList<ICharacterContentProvider> MakeProviders(Dictionary<string, string> sections)
        {
            var list = new List<ICharacterContentProvider>();
            foreach (var kv in sections)
                list.Add(new MockContentProvider(kv.Key, kv.Value));
            return list;
        }

        private class MockContentProvider : ICharacterContentProvider
        {
            private readonly string _content;
            public string SectionName { get; }

            public MockContentProvider(string sectionName, string content)
            {
                SectionName = sectionName;
                _content = content;
            }

            public string GetContent(string pawnId, string view)
            {
                return _content;
            }
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
