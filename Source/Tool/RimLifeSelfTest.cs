using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RimLife.Cards;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Infrastructure;
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

            var log = RimLifeCore.EventLog;
            if (log == null)
            {
                Skip("EventLog 为 null，无法测试");
                return;
            }

            var testEvent = MakeTestEvent($"selftest_{DateTime.Now.Ticks}", EventCategory.Social, 9999, "Minor");
            try
            {
                int before = log.TotalAppended;
                log.Append(testEvent);
                if (log.TotalAppended == before + 1)
                    Pass($"Append 成功 (total: {before} → {log.TotalAppended})");
                else
                    Fail("Append 后计数不正确", $"before={before} after={log.TotalAppended}");
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

                var byCategory = log.Query(EventQuery.ByCategory(EventCategory.Social));
                if (byCategory.Count > 0)
                    Pass($"Query(Social) 返回 {byCategory.Count} 条");
                else
                    Fail("Query(Social) 返回空");
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
                    DumpObject("Category", latest.Category);
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
                        DumpObject("  Category", evt.Category);
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
                    var evt = EventCardMapper.FromMentalBreak(pawn, pawn.MentalState);
                    if (evt != null)
                    {
                        Pass("FromMentalBreak 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  Category", evt.Category);
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
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static IGameEvent MakeTestEvent(string id, EventCategory cat, int tick, string severity)
        {
            return new TestGameEvent
            {
                EventID = id,
                DefName = "SelftestEvent",
                Category = cat,
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
            public EventCategory Category { get; set; }
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
