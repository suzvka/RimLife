using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Mcp;
using RimLife.Infrastructure;
using RimLife.Infrastructure.Mcp;
using RimLife.Mappers;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife.Tool
{
    /// <summary>
    /// RimLife 游戏内自检测试面板。
    ///
    /// 使用方式（Dev Mode 下）:
    ///   1. 选中游戏中任意角色
    ///   2. 在角色底部 Gizmo 面板找到 ★ RimLife 全量自检 按钮
    ///   3. 点击弹出测试菜单，选择测试项
    ///
    /// 日志输出到 Dev Console。
    /// </summary>
    public static class RimLifeSelfTest
    {
        // ================================================================
        // 测试辅助
        // ================================================================

        private static int _passed;
        private static int _failed;
        private static int _skipped;
        private static Stopwatch _sw;

        private static void BeginSuite(string name)
        {
            _passed = 0;
            _failed = 0;
            _skipped = 0;
            _sw = Stopwatch.StartNew();
            Log.Message(new string('━', 60));
            Log.Message($"  [RimLife.Test] ═══ {name} ═══");
            Log.Message(new string('━', 60));
        }

        private static void EndSuite()
        {
            _sw.Stop();
            Log.Message(new string('━', 60));
            Log.Message($"  [RimLife.Test] 完成: ✓{_passed}  ✗{_failed}  -{_skipped}  耗时 {_sw.ElapsedMilliseconds}ms");
            Log.Message(new string('━', 60));
        }

        private static void Pass(string message)
        {
            _passed++;
            Log.Message($"  ✓ {message}");
        }

        private static void Fail(string message, string detail = null)
        {
            _failed++;
            if (detail != null)
                Log.Warning($"  ✗ {message}  →  {detail}");
            else
                Log.Warning($"  ✗ {message}");
        }

        private static void Skip(string message)
        {
            _skipped++;
            Log.Message($"  - {message}  (跳过)");
        }

        private static void Section(string title)
        {
            Log.Message(string.Empty);
            Log.Message($"  ── {title} ──");
        }

        private static void DumpObject(string label, object obj)
        {
            if (obj == null)
            {
                Log.Message($"    {label}: (null)");
                return;
            }
            Log.Message($"    {label}: {obj}");
        }

        // ================================================================
        // 🔴 [Run All Tests] — 一键运行全部
        // ================================================================

        public static void RunAllTests()
        {
            BeginSuite("RimLife 全量自检");
            TestInfrastructure();
            TestJsonRoundTrip();
            TestFramework();
            TestEventLog();
            TestMappers();
            TestEventCardMapper();
            TestHarmonyStatus();
            TestCardSerializer();
            TestDirectorMcpTools();
            TestSkillSystem();
            EndSuite();
        }

        // ================================================================
        // 1. 基础设施测试
        // ================================================================

        public static void TestInfrastructure()
        {
            Section("1. 基础设施");

            if (RimLifeCore.SaveStore != null)
            {
                Pass("RimLifeCore.SaveStore 已注册");
                DumpObject("  类型", RimLifeCore.SaveStore.GetType().Name);
            }
            else
                Fail("RimLifeCore.SaveStore 未注册 (存档未完全加载?)");

            try
            {
                var cs = RimLifeCore.CacheStore;
                Pass($"CacheStore 可用 ({cs.GetType().Name})");
            }
            catch (Exception e)
            {
                Fail("CacheStore 初始化失败", e.Message);
            }

            var saveId = SaveIdResolver.CurrentSaveId;
            if (!string.IsNullOrEmpty(saveId))
            {
                Pass("SaveIdResolver 已设置");
                DumpObject("  GUID", saveId);
            }
            else
                Skip("SaveIdResolver 未设置 (可能是新档，FinalizeInit 尚未调用)");

            if (RimLifeCore.EventLog != null)
            {
                Pass($"EventLog 可用 ({RimLifeCore.EventLog.GetType().Name})");
                DumpObject("  TotalAppended", RimLifeCore.EventLog.TotalAppended);
            }
            else
                Skip("EventLog 未初始化 (SaveStore 为 null?)");
        }

        // ================================================================
        // 2. JSON 往返测试
        // ================================================================

        public static void TestJsonRoundTrip()
        {
            Section("2. JSON 往返");

            try
            {
                var dict = JsonParser.ParseDict("{\"key\":\"value\",\"num\":\"42\"}");
                if (dict.Count == 2 && dict["key"] == "value" && dict["num"] == "42")
                    Pass("ParseDict 基础解析");
                else
                    Fail("ParseDict 结果不正确", $"Count={dict.Count}");
            }
            catch (Exception e) { Fail("ParseDict 异常", e.Message); }

            try
            {
                var original = new Dictionary<string, string>
                {
                    ["id"] = "test_001",
                    ["name"] = "测试角色",
                    ["score"] = "99"
                };
                var json = JsonParser.SerializeDict(original);
                var parsed = JsonParser.ParseDict(json);
                bool ok = parsed.Count == 3 &&
                          parsed["id"] == "test_001" &&
                          parsed["name"] == "测试角色";
                if (ok)
                    Pass("SerializeDict → ParseDict 往返一致");
                else
                    Fail("往返后数据不一致");
            }
            catch (Exception e) { Fail("序列化往返异常", e.Message); }

            try
            {
                var json = "{\"event\":{\"id\":\"evt_1\"},\"actors\":[{\"role\":\"Initiator\"}]}";
                var dict = JsonParser.ParseDict(json);
                if (dict.ContainsKey("event") && dict["event"].Contains("evt_1")
                    && dict.ContainsKey("actors") && dict["actors"].Contains("Initiator"))
                    Pass("嵌套对象/数组保留为原始 JSON");
                else
                    Fail("嵌套结构解析不正确");
            }
            catch (Exception e) { Fail("嵌套解析异常", e.Message); }

            try
            {
                var escaped = JsonHelper.Escape("say \"hello\"\nworld");
                var unescaped = JsonParser.UnescapeJson(escaped);
                if (unescaped == "say \"hello\"\nworld")
                    Pass("Escape/Unescape 往返一致");
                else
                    Fail("转义往返数据不一致", $"got: '{unescaped}'");
            }
            catch (Exception e) { Fail("转义往返异常", e.Message); }

            try
            {
                var wJson = new JsonWriter(256)
                    .Prop("id", "test")
                    .Prop("tick", 1000)
                    .Prop("active", true)
                    .Close();
                if (wJson.Contains("\"id\":\"test\"") && wJson.Contains("\"tick\":1000"))
                    Pass("JsonWriter 链式构建");
                else
                    Fail("JsonWriter 输出不正确", wJson);
            }
            catch (Exception e) { Fail("JsonWriter 异常", e.Message); }
        }

        // ================================================================
        // 3. Framework 纯逻辑测试
        // ================================================================

        public static void TestFramework()
        {
            Section("3. Framework 纯逻辑");

            try
            {
                bool ok = SemanticLabels.MapPainTier(0f) == "None"
                       && SemanticLabels.MapPainTier(0.15f) == "Moderate"
                       && SemanticLabels.MapPainTier(0.70f) == "Extreme"
                       && SemanticLabels.MapPainTier(0.90f) == "Shock";
                if (ok) Pass("MapPainTier 阈值正确");
                else Fail("MapPainTier 阈值不匹配");
            }
            catch (Exception e) { Fail("MapPainTier 异常", e.Message); }

            try
            {
                bool ok = SemanticLabels.MapMoodTier(0.01f) == "Devastated"
                       && SemanticLabels.MapMoodTier(0.30f) == "Content"
                       && SemanticLabels.MapMoodTier(0.80f) == "Thriving";
                if (ok) Pass("MapMoodTier 阈值正确");
                else Fail("MapMoodTier 阈值不匹配");
            }
            catch (Exception e) { Fail("MapMoodTier 异常", e.Message); }

            try
            {
                bool ok = SemanticLabels.MapNeedUrgency("Food", 0.05f) == "Starving"
                       && SemanticLabels.MapNeedUrgency("Rest", 0.60f) == "Rested"
                       && SemanticLabels.MapNeedUrgency("Joy", 0.05f) == "Bored";
                if (ok) Pass("MapNeedUrgency 覆盖已知需求");
                else Fail("MapNeedUrgency 结果不匹配");
            }
            catch (Exception e) { Fail("MapNeedUrgency 异常", e.Message); }

            try
            {
                var a = new RandomInt(12345UL);
                var b = new RandomInt(12345UL);
                bool same = true;
                for (int i = 0; i < 10; i++)
                    if (a.Get(0, 1000) != b.Get(0, 1000)) { same = false; break; }
                if (same) Pass("RandomInt 同 seed 确定性");
                else Fail("RandomInt 同 seed 产生不同序列");
            }
            catch (Exception e) { Fail("RandomInt 异常", e.Message); }
        }

        // ================================================================
        // 4. EventLog 集成测试
        // ================================================================

        public static void TestEventLog()
        {
            Section("4. EventLog 集成");

            DumpObject("SaveStore", RimLifeCore.SaveStore != null ? "已注册" : "null");

            var log = RimLifeCore.EventLog;
            if (log == null)
            {
                Skip("EventLog 为 null，无法测试 (SaveStore 未就绪?)");
                return;
            }

            DumpObject("EventLog 类型", log.GetType().Name);
            DumpObject("当前 TotalAppended", log.TotalAppended);
            DumpObject("当前 _events 数量", log.Count(EventQuery.All));

            var testEvent = MakeTestEvent($"selftest_{DateTime.Now.Ticks}", new List<string> { "Selftest", "Social" }, 9999, "Minor");
            if (testEvent == null)
            {
                Fail("MakeTestEvent 返回 null");
                return;
            }

            try
            {
                int before = log.TotalAppended;
                log.Append(testEvent);
                int after = log.TotalAppended;
                if (after == before + 1)
                    Pass($"Append 成功 (total: {before} → {after})");
                else
                    Fail("Append 后计数不正确", $"before={before} after={after} type={log.GetType().Name}");
            }
            catch (Exception e) { Fail("Append 异常", e.Message); }

            try
            {
                var latest = log.Latest;
                if (latest != null && latest.EventID == testEvent.EventID)
                    Pass($"Latest 正确 ({latest.EventID})");
                else
                    Fail("Latest 不正确", latest?.EventID ?? "null");
            }
            catch (Exception e) { Fail("Latest 异常", e.Message); }

            try
            {
                var all = log.Query(EventQuery.All);
                if (all.Count > 0)
                    Pass($"Query(All) 返回 {all.Count} 条");
                else
                    Fail("Query(All) 返回空");

                var byTag = log.Query(EventQuery.ByAnyTag("Social"));
                if (byTag.Count > 0)
                    Pass($"Query(ByAnyTag:Social) 返回 {byTag.Count} 条");
                else
                    Fail("Query(ByAnyTag:Social) 返回空");
            }
            catch (Exception e) { Fail("Query 异常", e.Message); }

            try
            {
                int count = log.Count(EventQuery.All);
                Pass($"Count(All) = {count}");
            }
            catch (Exception e) { Fail("Count 异常", e.Message); }

            try
            {
                var latest = log.Latest;
                if (latest != null)
                {
                    DumpObject("EventID", latest.EventID);
                    DumpObject("DefName", latest.DefName);
                    DumpObject("Tags", string.Join(", ", latest.Tags ?? new List<string>()));
                    DumpObject("Tick", latest.Tick);
                    DumpObject("Severity", latest.Severity);
                    DumpObject("MapHint", latest.MapHint);
                    if (latest.Actors != null)
                        Log.Message($"    Actors: {latest.Actors.Count} 个");
                    if (latest.Payload != null)
                        Log.Message($"    Payload: {latest.Payload.Count} 个键");
                }
            }
            catch (Exception e) { Fail("事件详情输出异常", e.Message); }
        }

        // ================================================================
        // 5. Mapper 集成测试
        // ================================================================

        public static void TestMappers()
        {
            Section("5. Mapper 数据采集");

            var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
            if (pawn == null)
            {
                Skip("当前地图无可用 Pawn");
                return;
            }

            DumpObject("测试 Pawn", $"{pawn.LabelCap} ({pawn.ThingID})");

            // CharacterCardMapper — Basic
            try
            {
                var card = CharacterCardMapper.CreateBasic(pawn);
                bool ok = !string.IsNullOrEmpty(card.ID)
                       && !string.IsNullOrEmpty(card.Name)
                       && card.PawnType != null;
                if (ok)
                {
                    Pass("CreateBasic 成功");
                    DumpObject("  ID", card.ID);
                    DumpObject("  Name", card.Name);
                    DumpObject("  FullName", card.FullName);
                    DumpObject("  DefName", card.DefName);
                    DumpObject("  FactionLabel", card.FactionLabel);
                    DumpObject("  Gender", card.Gender);
                    DumpObject("  PawnType", card.PawnType);
                    DumpObject("  PawnRelation", card.PawnRelation);
                    DumpObject("  IsDead", card.IsDead);
                    DumpObject("  IsDowned", card.IsDowned);
                    DumpObject("  IsAwake", card.IsAwake);
                }
                else
                    Fail("CreateBasic 返回不完整", $"ID={card.ID} Name={card.Name}");
            }
            catch (Exception e) { Fail("CreateBasic 异常", e.Message); }

            // Health
            try
            {
                var card = CharacterCardMapper.CreateBasic(pawn);
                card.WithHealth(pawn);
                if (card.Health != null)
                {
                    Pass("WithHealth 成功");
                    DumpObject("  PainTier", card.Health.PainTier);
                    DumpObject("  BleedTier", card.Health.BleedTier);
                    DumpObject("  Injuries", card.Health.Injuries?.Count ?? 0);
                    DumpObject("  Capacities", card.Health.Capacities?.Count ?? 0);
                }
                else
                    Fail("WithHealth 返回 null");
            }
            catch (Exception e) { Fail("WithHealth 异常", e.Message); }

            // Mood
            try
            {
                var card = CharacterCardMapper.CreateBasic(pawn);
                card.WithMood(pawn);
                if (card.Mood != null)
                {
                    Pass("WithMood 成功");
                    DumpObject("  MoodTier", card.Mood.MoodTier);
                    DumpObject("  MoodLevel", card.Mood.MoodLevel);
                    DumpObject("  Traits", card.Mood.Traits?.Count ?? 0);
                    DumpObject("  Thoughts", card.Mood.ActiveThoughts?.Count ?? 0);
                }
                else
                    Fail("WithMood 返回 null");
            }
            catch (Exception e) { Fail("WithMood 异常", e.Message); }

            // Needs
            try
            {
                var card = CharacterCardMapper.CreateBasic(pawn);
                card.WithNeeds(pawn);
                if (card.Needs != null)
                {
                    Pass("WithNeeds 成功");
                    DumpObject("  Needs", card.Needs.AllNeeds?.Count ?? 0);
                    if (card.Needs.AllNeeds != null)
                    {
                        foreach (var n in card.Needs.AllNeeds.Take(5))
                            Log.Message($"    - {n.DefName}: {n.CurLevel:F2} [{n.NeedUrgency}]");
                    }
                }
                else
                    Fail("WithNeeds 返回 null");
            }
            catch (Exception e) { Fail("WithNeeds 异常", e.Message); }

            // EnvironmentCardMapper
            try
            {
                var env = EnvironmentCardMapper.CreateFrom(pawn);
                if (env != null)
                {
                    Pass("CreateFrom 成功");
                    DumpObject("  Type", env.Type);
                    DumpObject("  Temperature", env.Temperature);
                    DumpObject("  ThermalComfort", env.ThermalComfort);
                    DumpObject("  LightLabel", env.LightLabel);
                    if (env.Room != null)
                    {
                        DumpObject("  RoomRole", env.Room.RoleLabel);
                        DumpObject("  RoomImpressiveness", env.Room.BaseStats.Impressiveness);
                    }
                    if (!string.IsNullOrEmpty(env.Weather.Label))
                        DumpObject("  Weather", env.Weather.Label);
                    if (env.ThingSummary != null)
                        DumpObject("  ThingSummary entries", env.ThingSummary.Count);
                }
                else
                    Fail("CreateFrom 返回 null");
            }
            catch (Exception e) { Fail("CreateFrom 异常", e.Message); }
        }

        // ================================================================
        // 6. EventCardMapper 测试
        // ================================================================

        public static void TestEventCardMapper()
        {
            Section("6. EventCardMapper 构造");

            try
            {
                var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
                if (pawn != null)
                {
                    var evt = EventCardMapper.FromDeath(pawn, null);
                    if (evt != null && evt.DefName == "PawnDeath")
                    {
                        Pass("FromDeath 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  Tags", string.Join(", ", evt.Tags ?? new List<string>()));
                        DumpObject("  Severity", evt.Severity);
                    }
                    else
                        Fail("FromDeath 返回异常");
                }
                else
                    Skip("无可用 Pawn");
            }
            catch (Exception e) { Fail("FromDeath 异常", e.Message); }

            try
            {
                var pawns = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.Take(2).ToList();
                if (pawns != null && pawns.Count >= 2)
                {
                    var intDef = DefDatabase<InteractionDef>.GetNamedSilentFail("Chat")
                              ?? DefDatabase<InteractionDef>.AllDefs.FirstOrDefault();
                    var evt = EventCardMapper.FromSocialInteraction(pawns[0], pawns[1], intDef);
                    if (evt != null)
                    {
                        Pass("FromSocialInteraction 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  DefName", evt.DefName);
                        DumpObject("  Actors", evt.Actors?.Count ?? 0);
                    }
                    else
                        Fail("FromSocialInteraction 返回 null");
                }
                else
                    Skip("需要至少 2 个 Pawn");
            }
            catch (Exception e) { Fail("FromSocialInteraction 异常", e.Message); }

            try
            {
                var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
                if (pawn != null)
                {
                    var evt = EventCardMapper.FromFactionChange(pawn, pawn.Faction ?? Faction.OfPlayer);
                    if (evt != null && evt.DefName == "FactionChange")
                    {
                        Pass("FromFactionChange 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  Payload", $"{evt.Payload?.Count ?? 0} keys");
                    }
                    else
                        Fail("FromFactionChange 返回异常");
                }
                else
                    Skip("无可用 Pawn");
            }
            catch (Exception e) { Fail("FromFactionChange 异常", e.Message); }

            try
            {
                var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
                if (pawn != null)
                {
                    var evt = EventCardMapper.FromMentalBreak(pawn, "Selftest", null);
                    if (evt != null)
                    {
                        Pass("FromMentalBreak 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  Tags", string.Join(", ", evt.Tags ?? new List<string>()));
                    }
                    else
                        Fail("FromMentalBreak 返回 null");
                }
                else
                    Skip("无可用 Pawn");
            }
            catch (Exception e) { Fail("FromMentalBreak 异常", e.Message); }
        }

        // ================================================================
        // 7. Harmony Patch 状态
        // ================================================================

        public static void TestHarmonyStatus()
        {
            Section("7. Harmony Patch 状态");

            try
            {
                var harmony = new HarmonyLib.Harmony("RimLife.Core");
                var patched = harmony.GetPatchedMethods();
                int count = 0;
                foreach (var method in patched)
                {
                    var patches = HarmonyLib.Harmony.GetPatchInfo(method);
                    if (patches != null)
                    {
                        count++;
                        Log.Message($"    {method.DeclaringType?.Name}.{method.Name}");
                        if (patches.Prefixes.Count > 0)
                            Log.Message($"      Prefixes: {patches.Prefixes.Count}");
                        if (patches.Postfixes.Count > 0)
                            Log.Message($"      Postfixes: {patches.Postfixes.Count}");
                    }
                }
                if (count > 0)
                    Pass($"已注册 {count} 个 Harmony patch");
                else
                    Fail("未检测到任何已注册的 Harmony patch");
            }
            catch (Exception e) { Fail("Harmony 状态检测异常", e.Message); }
        }

        // ================================================================
        // 8. CardSerializer 序列化测试
        // ================================================================

        public static void TestCardSerializer()
        {
            Section("8. CardSerializer 序列化");

            var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();

            // --- ColonyContext ---
            try
            {
                var ctx = ColonyContextMapper.Create();
                if (ctx != null)
                {
                    var json = CardSerializer.SerializeColonyContext(ctx);
                    if (json.Length > 10 && json.StartsWith("{") && json.EndsWith("}"))
                    {
                        Pass($"SerializeColonyContext 成功 ({json.Length} chars)");
                        DumpObject("  season", ctx.Season);
                        DumpObject("  populationAlive", ctx.PopulationAlive);
                    }
                    else
                        Fail("SerializeColonyContext 输出异常", $"len={json.Length}");
                }
                else
                    Skip("ColonyContextMapper.Create 返回 null");
            }
            catch (Exception e) { Fail("SerializeColonyContext 异常", e.Message); }

            // --- CharacterCard ---
            if (pawn != null)
            {
                try
                {
                    var card = CharacterCardMapper.CreateBasic(pawn)
                        .WithHealth(pawn)
                        .WithMood(pawn)
                        .WithSkills(pawn);
                    var json = CardSerializer.SerializeCharacterCard(card, "health,mood,skills");
                    if (json.Length > 50 && json.Contains("\"id\""))
                    {
                        Pass($"SerializeCharacterCard 成功 ({json.Length} chars)");
                        DumpObject("  pawn", card.Name);
                        DumpObject("  sections", "health,mood,skills");
                    }
                    else
                        Fail("SerializeCharacterCard 输出异常");
                }
                catch (Exception e) { Fail("SerializeCharacterCard 异常", e.Message); }
            }
            else
                Skip("SerializeCharacterCard — 无可用 Pawn");

            // --- IGameEvent ---
            try
            {
                var evt = MakeTestEvent("serializer_test", new List<string> { "Test" }, 10000, "Major");
                var json = CardSerializer.SerializeEvent(evt);
                if (json.Contains("serializer_test") && json.Contains("Test"))
                {
                    Pass("SerializeEvent 成功");
                    DumpObject("  EventID", evt.EventID);
                }
                else
                    Fail("SerializeEvent 输出不完整");
            }
            catch (Exception e) { Fail("SerializeEvent 异常", e.Message); }

            // --- EventList ---
            try
            {
                var events = new List<IGameEvent>
                {
                    MakeTestEvent("list_1", new List<string>{"A"}, 1, "Minor"),
                    MakeTestEvent("list_2", new List<string>{"B"}, 2, "Major")
                };
                var json = CardSerializer.SerializeEventList(events);
                if (json.StartsWith("[") && json.Contains("list_1") && json.Contains("list_2"))
                    Pass("SerializeEventList 成功");
                else
                    Fail("SerializeEventList 输出异常");
            }
            catch (Exception e) { Fail("SerializeEventList 异常", e.Message); }

            // --- ObjectiveCard ---
            try
            {
                var objectives = ObjectiveCardMapper.GetActive();
                var json = CardSerializer.SerializeObjectiveList(objectives);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"SerializeObjectiveList 成功 ({objectives.Count} objectives)");
                else
                    Fail("SerializeObjectiveList 输出异常");
            }
            catch (Exception e) { Fail("SerializeObjectiveList 异常", e.Message); }

            // --- EnvironmentCard ---
            if (pawn != null)
            {
                try
                {
                    var env = EnvironmentCardMapper.CreateFrom(pawn);
                    var json = CardSerializer.SerializeEnvironment(env);
                    if (json.Contains("\"type\""))
                    {
                        Pass($"SerializeEnvironment 成功 ({env.Type})");
                    }
                    else
                        Fail("SerializeEnvironment 输出不完整");
                }
                catch (Exception e) { Fail("SerializeEnvironment 异常", e.Message); }
            }
            else
                Skip("SerializeEnvironment — 无可用 Pawn");

            // --- InteractionRecord ---
            try
            {
                var store = RimLifeCore.InteractionStore;
                if (store != null && store.TotalAppended > 0)
                {
                    var records = store.QueryByPawn("test", null, 3);
                    var json = CardSerializer.SerializeInteractionList(records);
                    if (json.StartsWith("["))
                        Pass($"SerializeInteractionList 成功 ({records.Count} records)");
                    else
                        Fail("SerializeInteractionList 输出异常");
                }
                else
                    Skip("SerializeInteractionList — InteractionStore 为空");
            }
            catch (Exception e) { Fail("SerializeInteractionList 异常", e.Message); }

            // --- ColonistSummaryList (from ColonyContext) ---
            try
            {
                var ctx = ColonyContextMapper.Create();
                if (ctx?.Colonists != null && ctx.Colonists.Count > 0)
                {
                    var json = CardSerializer.SerializeColonistSummaryList(ctx.Colonists);
                    if (json.StartsWith("[") && json.Contains(ctx.Colonists[0].Name))
                        Pass($"SerializeColonistSummaryList 成功 ({ctx.Colonists.Count} colonists)");
                    else
                        Fail("SerializeColonistSummaryList 输出异常");
                }
                else
                    Skip("SerializeColonistSummaryList — 无 Colonists 数据");
            }
            catch (Exception e) { Fail("SerializeColonistSummaryList 异常", e.Message); }
        }

        // ================================================================
        // 9. DirectorMcpTools 工具调用测试
        // ================================================================

        public static void TestDirectorMcpTools()
        {
            Section("9. DirectorMcpTools 工具调用");

            var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
            string pawnId = pawn?.ThingID;

            // --- 9.1 get_colony_overview ---
            try
            {
                var json = DirectorMcpTools.GetColonyOverview();
                if (json.Length > 20 && json.StartsWith("{") && json.EndsWith("}"))
                {
                    Pass($"get_colony_overview 成功 ({json.Length} chars)");
                    DumpObject("  preview", json.Length > 120 ? json.Substring(0, 120) + "..." : json);
                }
                else
                    Fail("get_colony_overview 输出异常", $"len={json.Length}");
            }
            catch (Exception e) { Fail("get_colony_overview 异常", e.Message); }

            // --- 9.2 get_recent_events ---
            try
            {
                var eventLog = RimLifeCore.EventLog;
                if (eventLog != null && eventLog.TotalAppended > 0)
                {
                    var json = DirectorMcpTools.GetRecentEvents(5);
                    if (json.StartsWith("[") && json.EndsWith("]"))
                        Pass($"get_recent_events 成功");
                    else
                        Fail("get_recent_events 输出异常");
                }
                else
                    Skip("get_recent_events — EventLog 为空");
            }
            catch (Exception e) { Fail("get_recent_events 异常", e.Message); }

            // --- 9.3 get_active_objectives ---
            try
            {
                var json = DirectorMcpTools.GetActiveObjectives();
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass("get_active_objectives 成功");
                else
                    Fail("get_active_objectives 输出异常");
            }
            catch (Exception e) { Fail("get_active_objectives 异常", e.Message); }

            // --- 9.4 get_character_card ---
            if (pawnId != null)
            {
                try
                {
                    var json = DirectorMcpTools.GetCharacterCard(pawnId, "health,mood,skills");
                    if (json.Length > 50 && json.Contains("\"id\""))
                        Pass($"get_character_card 成功 ({json.Length} chars)");
                    else
                        Fail("get_character_card 输出异常");
                }
                catch (Exception e) { Fail("get_character_card 异常", e.Message); }

                try
                {
                    var json = DirectorMcpTools.GetCharacterCard(pawnId); // all sections
                    if (json.Length > 50)
                        Pass($"get_character_card(all) 成功 ({json.Length} chars)");
                    else
                        Fail("get_character_card(all) 输出异常");
                }
                catch (Exception e) { Fail("get_character_card(all) 异常", e.Message); }
            }
            else
                Skip("get_character_card — 无可用 Pawn");

            // --- 9.5 find_characters ---
            try
            {
                var json = DirectorMcpTools.FindCharacters(moodTier: "Content", limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(moodTier) 成功");
                else
                    Fail("find_characters 输出异常");
            }
            catch (Exception e) { Fail("find_characters 异常", e.Message); }

            try
            {
                var json = DirectorMcpTools.FindCharacters(minSkill: "Shooting=3", limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(skill) 成功");
                else
                    Fail("find_characters(skill) 输出异常");
            }
            catch (Exception e) { Fail("find_characters(skill) 异常", e.Message); }

            try
            {
                var json = DirectorMcpTools.FindCharacters(injuredOnly: true, limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(injured) 成功");
                else
                    Fail("find_characters(injured) 输出异常");
            }
            catch (Exception e) { Fail("find_characters(injured) 异常", e.Message); }

            // --- 9.6 query_events ---
            try
            {
                var eventLog = RimLifeCore.EventLog;
                if (eventLog != null && eventLog.TotalAppended > 0)
                {
                    var json = DirectorMcpTools.QueryEvents(tagsAny: "Combat", limit: 5);
                    if (json.StartsWith("[") && json.EndsWith("]"))
                        Pass("query_events(Combat) 成功");
                    else
                        Fail("query_events 输出异常");
                }
                else
                    Skip("query_events — EventLog 为空");
            }
            catch (Exception e) { Fail("query_events 异常", e.Message); }

            // --- 9.7 get_relationships ---
            if (pawnId != null)
            {
                try
                {
                    var json = DirectorMcpTools.GetRelationships(pawnId);
                    if (json.Length > 5 && json.StartsWith("{"))
                        Pass($"get_relationships 成功 ({json.Length} chars)");
                    else
                        Fail("get_relationships 输出异常");
                }
                catch (Exception e) { Fail("get_relationships 异常", e.Message); }
            }
            else
                Skip("get_relationships — 无可用 Pawn");

            // --- 9.8 get_interaction_history ---
            try
            {
                var store = RimLifeCore.InteractionStore;
                if (store != null && store.TotalAppended > 0 && pawnId != null)
                {
                    var json = DirectorMcpTools.GetInteractionHistory(pawnId, limit: 5);
                    if (json.StartsWith("[") && json.EndsWith("]"))
                        Pass("get_interaction_history 成功");
                    else
                        Fail("get_interaction_history 输出异常");
                }
                else
                    Skip("get_interaction_history — InteractionStore 为空");
            }
            catch (Exception e) { Fail("get_interaction_history 异常", e.Message); }

            // --- 9.9 get_environment ---
            if (pawnId != null)
            {
                try
                {
                    var json = DirectorMcpTools.GetEnvironment(pawnId);
                    if (json.Contains("\"type\""))
                        Pass("get_environment 成功");
                    else
                        Fail("get_environment 输出异常");
                }
                catch (Exception e) { Fail("get_environment 异常", e.Message); }
            }
            else
                Skip("get_environment — 无可用 Pawn");

            // --- MCP 工具定义生成 ---
            try
            {
                var json = McpToolGenerator.SerializeAllFrom(typeof(DirectorMcpTools));
                if (json.StartsWith("[") && json.EndsWith("]") && json.Length > 100)
                {
                    // 计数工具数量
                    int toolCount = 0;
                    for (int i = 0; i < json.Length; i++)
                        if (json[i] == '{') toolCount++;
                    Pass($"MCP 工具定义生成成功 ({toolCount} tools, {json.Length} chars)");
                }
                else
                    Fail("MCP 工具定义生成异常", $"len={json.Length}");
            }
            catch (Exception e) { Fail("MCP 工具定义生成异常", e.Message); }
        }

        // ================================================================
        // 10. Skill 系统测试
        // ================================================================

        public static void TestSkillSystem()
        {
            Section("10. Skill 按需激活系统");

            // 确保注册表已初始化
            RimLifeCore.EnsureSkillRegistryInitialized();

            // --- 10.1 初始状态 ---
            try
            {
                DumpObject("已注册 Skill 数", McpSkillRegistry.SkillCount);
                DumpObject("已注册工具总数", McpSkillRegistry.TotalToolCount);
                DumpObject("初始激活 Skill 数", McpSkillRegistry.ActiveSkillCount);

                if (McpSkillRegistry.SkillCount >= 7)
                    Pass($"Skill 注册表: {McpSkillRegistry.SkillCount} skills, {McpSkillRegistry.TotalToolCount} tools");
                else
                    Fail("Skill 注册数不足", $"expected >=7, got {McpSkillRegistry.SkillCount}");
            }
            catch (Exception e) { Fail("Skill 注册表状态异常", e.Message); }

            // --- 10.2 list_skills ---
            try
            {
                McpSkillRegistry.Reset();
                var json = SystemMcpTools.ListSkills();
                if (json.Contains("\"skills\"") && json.Contains("colony_overview"))
                {
                    int len = json.Length;
                    Pass($"list_skills 成功 ({len} chars, 轻量摘要)");
                    if (len < 100)
                        DumpObject("  JSON", json);
                    else
                        DumpObject("  前 100 字符", json.Substring(0, 100) + "...");
                }
                else
                    Fail("list_skills 输出异常");
            }
            catch (Exception e) { Fail("list_skills 异常", e.Message); }

            // --- 10.3 activate_skill ---
            try
            {
                var result = SystemMcpTools.ActivateSkill("colony_overview");
                if (result.Contains("\"activated\"") && result.Contains("colony_overview"))
                {
                    Pass("activate_skill(colony_overview) 成功");
                    DumpObject("  preview", result.Length > 200 ? result.Substring(0, 200) + "..." : result);
                }
                else
                    Fail("activate_skill 输出异常", $"len={result.Length}");
            }
            catch (Exception e) { Fail("activate_skill 异常", e.Message); }

            // --- 10.4 激活后工具数 ---
            try
            {
                var json = McpToolGenerator.SerializeAllActiveTools();
                int toolCount = 0;
                for (int i = 0; i < json.Length; i++)
                    if (json[i] == '{' && i + 1 < json.Length && json[i + 1] == '"') toolCount++;

                // 用更可靠的方式计数
                int nameCount = 0;
                int pos = 0;
                while ((pos = json.IndexOf("\"name\":", pos, StringComparison.Ordinal)) >= 0)
                {
                    nameCount++;
                    pos++;
                }

                if (nameCount >= 3)
                    Pass($"激活后活跃工具数: {nameCount} (含 system + colony_overview)");
                else
                    Fail("活跃工具数不足", $"expected >=3, got {nameCount}");
            }
            catch (Exception e) { Fail("SerializeAllActiveTools 异常", e.Message); }

            // --- 10.5 累积激活 ---
            try
            {
                SystemMcpTools.ActivateSkill("character_query");
                SystemMcpTools.ActivateSkill("relationship_query");
                int activeSkillCount = McpSkillRegistry.ActiveSkillCount;
                if (activeSkillCount == 4)
                    Pass($"累积激活: {activeSkillCount} skills active (system + colony + character + relationship)");
                else
                    Fail("累积激活数不正确", $"expected 4, got {activeSkillCount}");
            }
            catch (Exception e) { Fail("累积激活异常", e.Message); }

            // --- 10.6 deactivate_skill ---
            try
            {
                SystemMcpTools.DeactivateSkill("relationship_query");
                if (!McpSkillRegistry.IsActive("relationship_query"))
                    Pass("deactivate_skill(relationship_query) 成功");
                else
                    Fail("反激活失败");
            }
            catch (Exception e) { Fail("deactivate_skill 异常", e.Message); }

            // --- 10.7 system 不可反激活 ---
            try
            {
                var result = SystemMcpTools.DeactivateSkill(McpSkillRegistry.SystemSkillId);
                if (result.Contains("\"error\"") && McpSkillRegistry.IsActive(McpSkillRegistry.SystemSkillId))
                    Pass("system skill 不可反激活 (预期行为)");
                else
                    Fail("system skill 应不可反激活");
            }
            catch (Exception e) { Fail("system deactivate 异常", e.Message); }

            // --- 10.8 Token 节省对比 ---
            try
            {
                McpSkillRegistry.Reset();
                var initialJson = McpToolGenerator.SerializeAllActiveTools();

                // 激活全部
                foreach (var id in McpSkillRegistry.GetAllSkillIds())
                    McpSkillRegistry.ActivateSkill(id);
                var fullJson = McpToolGenerator.SerializeAllActiveTools();

                int initialLen = initialJson.Length;
                int fullLen = fullJson.Length;
                double savings = fullLen > 0 ? (1.0 - (double)initialLen / fullLen) * 100 : 0;
                Pass($"Token 节省: 初始 {initialLen} chars vs 全量 {fullLen} chars ({savings:F0}% 节省)");
            }
            catch (Exception e) { Fail("Token 对比异常", e.Message); }

            // --- 10.9 preload_skill ---
            try
            {
                McpSkillRegistry.Reset();
                var result = SystemMcpTools.PreloadSkill("colony_overview");
                if (result.Contains("\"action\":\"preloaded\"") && McpSkillRegistry.IsPreloaded("colony_overview"))
                {
                    Pass("preload_skill(colony_overview) 成功 (已激活+已预载)");
                }
                else
                    Fail("preload_skill 输出异常", $"len={result.Length}");
            }
            catch (Exception e) { Fail("preload_skill 异常", e.Message); }

            // --- 10.10 重复预载 ---
            try
            {
                var result = SystemMcpTools.PreloadSkill("colony_overview");
                if (result.Contains("already_preloaded"))
                    Pass("重复 preload_skill 返回 already_preloaded (幂等)");
                else
                    Fail("重复预载应返回 already_preloaded");
            }
            catch (Exception e) { Fail("重复预载异常", e.Message); }

            // --- 10.11 unpreload_skill ---
            try
            {
                var result = SystemMcpTools.UnpreloadSkill("colony_overview");
                if (result.Contains("\"action\":\"unpreloaded\"") && !McpSkillRegistry.IsPreloaded("colony_overview"))
                {
                    Pass("unpreload_skill 成功 (解除预载，会话内仍激活)");
                }
                else
                    Fail("unpreload_skill 输出异常");
            }
            catch (Exception e) { Fail("unpreload_skill 异常", e.Message); }

            // --- 10.12 unpreload 但保持激活 ---
            try
            {
                if (McpSkillRegistry.IsActive("colony_overview"))
                    Pass("unpreload 后技能仍保持激活 (当前会话不受影响)");
                else
                    Fail("unpreload 后技能不应被反激活");
            }
            catch (Exception e) { Fail("unpreload 激活状态验证异常", e.Message); }

            // --- 10.13 ApplyPreloads 批量恢复 ---
            try
            {
                McpSkillRegistry.Reset();
                int count = McpSkillRegistry.ApplyPreloads(new[] { "colony_overview", "character_query", "knowledge_management" });
                if (count == 3 && McpSkillRegistry.IsActive("colony_overview")
                    && McpSkillRegistry.IsActive("character_query")
                    && McpSkillRegistry.IsActive("knowledge_management"))
                    Pass($"ApplyPreloads 批量激活 {count} 个技能 (冷启动恢复)");
                else
                    Fail($"ApplyPreloads 数量不正确", $"expected 3, got {count}");
            }
            catch (Exception e) { Fail("ApplyPreloads 异常", e.Message); }

            // --- 10.14 list_skills 含预载状态 ---
            try
            {
                McpSkillRegistry.Reset();
                McpSkillRegistry.ApplyPreloads(new[] { "colony_overview" });
                var json = SystemMcpTools.ListSkills();
                if (json.Contains("\"preloaded\":true") && json.Contains("\"preloadedSkillIds\""))
                    Pass("list_skills 包含 preloaded 字段和 preloadedSkillIds 列表");
                else
                    Fail("list_skills 缺少预载信息");
            }
            catch (Exception e) { Fail("list_skills 预载字段异常", e.Message); }

            // --- 10.15 预载轮次节省验证 ---
            try
            {
                McpSkillRegistry.Reset();
                // 模拟冷启动：ApplyPreloads → 直接可用
                McpSkillRegistry.ApplyPreloads(new[] { "colony_overview", "character_query" });
                var activeIds = McpSkillRegistry.GetActiveSkillIds();
                if (activeIds.Contains("colony_overview") && activeIds.Contains("character_query"))
                {
                    int preloadedCount = McpSkillRegistry.GetPreloadSkillIds().Count;
                    Pass($"预载轮次节省: 冷启动后 {preloadedCount} 个技能自动激活，免去 list_skills→activate_skill (省 1 轮 LLM 调用)");
                }
                else
                    Fail("预载冷启动恢复失败");
            }
            catch (Exception e) { Fail("轮次节省验证异常", e.Message); }

            McpSkillRegistry.Reset();
        }

        // ================================================================
        // Gizmo 注入 — 获得角色 Gizmo 列表
        // ================================================================

        public static IEnumerable<Gizmo> GetTestGizmos(Pawn pawn)
        {
            if (pawn == null) yield break;
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "★ RimLife 全量自检",
                defaultDesc = "运行 RimLife 全套自检测试，结果输出到 Dev Console。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false),
                action = () => ShowTestMenu()
            };
        }

        private static void ShowTestMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("★ 一键运行全部测试", () => RunAllTests()),
                new FloatMenuOption("1. 基础设施 (SaveStore/CacheStore/EventLog)", () => TestInfrastructure()),
                new FloatMenuOption("2. JSON 往返 (ParseDict/Serialize/Writer)", () => TestJsonRoundTrip()),
                new FloatMenuOption("3. Framework 纯逻辑 (SemanticLabels/RandomInt)", () => TestFramework()),
                new FloatMenuOption("4. EventLog 集成 (Append/Query/Count)", () => TestEventLog()),
                new FloatMenuOption("5. Mapper 数据采集 (CharacterCard/EnvironmentCard)", () => TestMappers()),
                new FloatMenuOption("6. EventCardMapper 构造 (FromDeath/FromSocial/...)", () => TestEventCardMapper()),
                new FloatMenuOption("7. Harmony Patch 状态", () => TestHarmonyStatus()),
                new FloatMenuOption("8. CardSerializer 序列化 (ColonyContext/CharacterCard/...)", () => TestCardSerializer()),
                new FloatMenuOption("9. DirectorMcpTools 工具调用 (所有9个MCP工具)", () => TestDirectorMcpTools()),
                new FloatMenuOption("10. Skill 按需激活系统 (list/activate/deactivate/token对比)", () => TestSkillSystem()),
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static IGameEvent MakeTestEvent(string id, IReadOnlyList<string> tags, int tick, string severity)
        {
            return new TestGameEvent
            {
                EventID = id,
                DefName = "SelftestEvent",
                Tags = tags,
                Tick = tick,
                Severity = severity,
                Actors = new List<EventActorRef>
                {
                    EventActorRef.Pawn("test_pawn_001", "TestPawn", "Initiator")
                },
                MapHint = "SelftestLocation",
                Payload = new Dictionary<string, string> { ["test"] = "true" }
            };
        }

        private class TestGameEvent : IGameEvent
        {
            public string EventID { get; set; }
            public string DefName { get; set; }
            public IReadOnlyList<string> Tags { get; set; }
            public int Tick { get; set; }
            public string Severity { get; set; }
            public IReadOnlyList<EventActorRef> Actors { get; set; }
            public string MapHint { get; set; }
            public IDictionary<string, string> Payload { get; set; }
        }
    }

    // ================================================================
    // Harmony 补丁 — 将自检 Gizmo 注入到选中角色的底部面板
    // 与 PawnProDebug 使用完全相同的注入模式。
    // ================================================================
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_GetGizmos_RimLifeSelfTestPatch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__instance == null) return;
                if (!Prefs.DevMode) return;
                if (!Find.Selector.SelectedObjects.Contains(__instance)) return;

                var list = __result.ToList();
                list.AddRange(RimLifeSelfTest.GetTestGizmos(__instance));
                __result = list;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.Test] Gizmo injection failed: {e.Message}");
            }
        }
    }
}
