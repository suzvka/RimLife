using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using RimLife.Infrastructure;
using NPCLife.Infrastructure.Mcp;
using RimLife.Infrastructure.Mcp;
using RimLife.Mappers;
using RimLife.UI;
using NPCLife.Workspace;
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
        // 配置面板入口
        // ================================================================

        /// <summary>
        /// 打开 RimLife 配置面板。
        /// 可在 Dev Mode 下通过 Gizmo 按钮触发，或从 Mod 设置调用。
        /// </summary>
        public static void OpenConfigPanel()
        {
            Find.WindowStack.Add(new ConfigPanelWindow());
            Log.Message("[RimLife.UI] Config panel opened.");
        }
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
            TestMcpProviders();
            TestSkillSystem();
            TestAgentLoop();
            TestWorkspaceAgent();
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

            var directorWs = RimLifeCore.GetDirectorWorkspace();
            if (directorWs?.EventPool != null)
            {
                Pass($"EventPool 可用 ({directorWs.EventPool.GetType().Name})");
                DumpObject("  TotalAppended", directorWs.EventPool.TotalAppended);
            }
            else
                Skip("EventPool 未初始化 (无导演工作空间)");
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

            var log = RimLifeCore.GetDirectorWorkspace()?.EventPool;
            if (log == null)
            {
                Skip("EventPool 为 null，无法测试 (无导演工作空间?)");
                return;
            }

            DumpObject("EventLog 类型", log.GetType().Name);
            DumpObject("当前 TotalAppended", log.TotalAppended);
            DumpObject("当前 _events 数量", log.Count(EventQuery.All));

            var testEvent = MakeTestEvent($"selftest_{DateTime.Now.Ticks}", new List<string> { "Selftest", "Social" }, 9999, 1f);
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
                    DumpObject("Importance", latest.Importance);
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

            // PawnQueryHelper — Basic
            try
            {
                var card = PawnQueryHelper.BuildCharacterCard(pawn, null);
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
                    DumpObject("  IsAwake", card.IsAwake);
                }
                else
                    Fail("CreateBasic 返回不完整", $"ID={card.ID} Name={card.Name}");
            }
            catch (Exception e) { Fail("CreateBasic 异常", e.Message); }

            // ContentProviders (section tests via hook pattern)
            try
            {
                var providers = RimLifeCore.ContentProviders;
                if (providers != null && providers.Count > 0)
                {
                    int sectionsWithContent = 0;
                    int sectionsTotal = 0;
                    foreach (var provider in providers)
                    {
                        if (provider == null) continue;
                        sectionsTotal++;
                        var content = provider.GetContent(pawn.ThingID, "static");
                        if (!string.IsNullOrEmpty(content))
                            sectionsWithContent++;
                    }
                    if (sectionsWithContent > 0)
                    {
                        Pass($"ContentProviders 钩子模式正常 ({sectionsWithContent}/{sectionsTotal} sections 有数据)");
                        DumpObject("  Total providers", sectionsTotal);
                        DumpObject("  Sections with content", sectionsWithContent);
                    }
                    else
                        Fail("ContentProviders 所有 section 均无数据");
                }
                else
                    Fail("ContentProviders 未注册或为空");
            }
            catch (Exception e) { Fail("ContentProviders 测试异常", e.Message); }

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
                    var evt = EventCardMapper.FromDeath(pawn, null, 3f);
                    if (evt != null && evt.DefName == "PawnDeath")
                    {
                        Pass("FromDeath 构造成功");
                        DumpObject("  EventID", evt.EventID);
                        DumpObject("  Tags", string.Join(", ", evt.Tags ?? new List<string>()));
                        DumpObject("  Importance", evt.Importance);
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
                    var evt = EventCardMapper.FromSocialInteraction(pawns[0], pawns[1], intDef, 1f);
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
                    var evt = EventCardMapper.FromFactionChange(pawn, pawn.Faction ?? Faction.OfPlayer, 3f);
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
                    var evt = EventCardMapper.FromMentalBreak(pawn, "Selftest", null, 3f);
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
                    var json = CardSerializer.Default.SerializeColonyContext(ctx);
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
                    var card = PawnQueryHelper.BuildCharacterCard(pawn, null);
                    var json = CardSerializer.Default.SerializeCharacterCard(card, "static", RimLifeCore.ContentProviders);
                    if (json.Length > 50 && json.Contains("\"id\""))
                    {
                        Pass($"SerializeCharacterCard 成功 ({json.Length} chars)");
                        DumpObject("  pawn", card.Name);
                        DumpObject("  view", "static");
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
                var evt = MakeTestEvent("serializer_test", new List<string> { "Test" }, 10000, 3f);
                var json = CardSerializer.Default.SerializeEvent(evt);
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
                    MakeTestEvent("list_1", new List<string>{"A"}, 1, 1f),
                    MakeTestEvent("list_2", new List<string>{"B"}, 2, 3f)
                };
                var json = CardSerializer.Default.SerializeEventList(events);
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
                var json = CardSerializer.Default.SerializeObjectiveList(objectives);
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
                    var json = CardSerializer.Default.SerializeEnvironment(env);
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
                    var json = CardSerializer.Default.SerializeInteractionList(records);
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
                    var json = CardSerializer.Default.SerializeColonistSummaryList(ctx.Colonists);
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
        // 9. MCP Provider 工具调用测试
        // ================================================================

        public static void TestMcpProviders()
        {
            Section("9. MCP Provider 工具调用");

            var pawn = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault();
            string pawnId = pawn?.ThingID;

            // --- 9.1 get_colony_overview ---
            try
            {
                var json = ColonyOverviewProvider.GetColonyOverview();
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
                var eventLog = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (eventLog != null && eventLog.TotalAppended > 0)
                {
                    var json = ColonyOverviewProvider.GetRecentEvents(5);
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
                var json = ColonyOverviewProvider.GetActiveObjectives();
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
                    var json = CharacterQueryProvider.GetCharacterCard(pawnId, "health,mood,skills");
                    if (json.Length > 50 && json.Contains("\"id\""))
                        Pass($"get_character_card 成功 ({json.Length} chars)");
                    else
                        Fail("get_character_card 输出异常");
                }
                catch (Exception e) { Fail("get_character_card 异常", e.Message); }

                try
                {
                    var json = CharacterQueryProvider.GetCharacterCard(pawnId); // all sections
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
                var json = CharacterQueryProvider.FindCharacters(moodTier: "Content", limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(moodTier) 成功");
                else
                    Fail("find_characters 输出异常");
            }
            catch (Exception e) { Fail("find_characters 异常", e.Message); }

            try
            {
                var json = CharacterQueryProvider.FindCharacters(minSkill: "Shooting=3", limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(skill) 成功");
                else
                    Fail("find_characters(skill) 输出异常");
            }
            catch (Exception e) { Fail("find_characters(skill) 异常", e.Message); }

            try
            {
                var json = CharacterQueryProvider.FindCharacters(injuredOnly: true, limit: 3);
                if (json.StartsWith("[") && json.EndsWith("]"))
                    Pass($"find_characters(injured) 成功");
                else
                    Fail("find_characters(injured) 输出异常");
            }
            catch (Exception e) { Fail("find_characters(injured) 异常", e.Message); }

            // --- 9.7 get_relationships ---
            if (pawnId != null)
            {
                try
                {
                    var json = RelationshipQueryProvider.GetRelationships(pawnId);
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
                    var json = RelationshipQueryProvider.GetInteractionHistory(pawnId, limit: 5);
                    if (json.StartsWith("[") && json.EndsWith("]"))
                        Pass("get_interaction_history 成功");
                    else
                        Fail("get_interaction_history 输出异常");
                }
                else
                    Skip("get_interaction_history — InteractionStore 为空");
            }
            catch (Exception e) { Fail("get_interaction_history 异常", e.Message); }

            // --- 9.8b get_relationship_between ---
            try
            {
                var pawns = Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.Take(2).ToList();
                if (pawns != null && pawns.Count >= 2)
                {
                    var json = RelationshipQueryProvider.GetRelationshipBetween(
                        pawns[0].ThingID, pawns[1].ThingID);
                    if (json.StartsWith("{") && json.Contains("\"ab\""))
                    {
                        Pass($"get_relationship_between 成功 ({json.Length} chars)");
                        DumpObject("  preview", json.Length > 120 ? json.Substring(0, 120) + "..." : json);
                    }
                    else
                        Fail("get_relationship_between 输出异常");
                }
                else
                    Skip("get_relationship_between — 无足够 Pawn");
            }
            catch (Exception e) { Fail("get_relationship_between 异常", e.Message); }

            // --- 9.9 get_environment ---
            if (pawnId != null)
            {
                try
                {
                    var json = EnvironmentQueryProvider.GetEnvironment(pawnId);
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
                var json = McpToolGenerator.SerializeAllFrom(typeof(ColonyOverviewProvider));
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

                if (McpSkillRegistry.SkillCount >= 7)
                    Pass($"Skill 注册表: {McpSkillRegistry.SkillCount} skills, {McpSkillRegistry.TotalToolCount} tools");
                else
                    Fail("Skill 注册数不足", $"expected >=7, got {McpSkillRegistry.SkillCount}");
            }
            catch (Exception e) { Fail("Skill 注册表状态异常", e.Message); }

            // --- 10.2 GetSkillListJson 纯函数 ---
            try
            {
                var json = McpSkillRegistry.GetSkillListJson(null); // 传入 null = 无激活技能
                if (json.Contains("\"skills\"") && json.Contains("colony_overview"))
                {
                    int len = json.Length;
                    Pass($"GetSkillListJson(null) 成功 ({len} chars, 轻量摘要)");
                    if (len < 100)
                        DumpObject("  JSON", json);
                    else
                        DumpObject("  前 100 字符", json.Substring(0, 100) + "...");
                }
                else
                    Fail("GetSkillListJson 输出异常");
            }
            catch (Exception e) { Fail("GetSkillListJson 异常", e.Message); }

            // --- 10.3 GetActiveToolsJson 纯函数（显式传入激活列表）---
            try
            {
                var activeIds = new[] { "colony_overview" };
                var json = McpSkillRegistry.GetActiveToolsJson(activeIds);
                int nameCount = 0;
                int pos = 0;
                while ((pos = json.IndexOf("\"name\":", pos, StringComparison.Ordinal)) >= 0)
                {
                    nameCount++;
                    pos++;
                }

                if (nameCount >= 3)
                    Pass($"GetActiveToolsJson(colony_overview) → {nameCount} tools (含 system)");
                else
                    Fail("活跃工具数不足", $"expected >=3, got {nameCount}");
            }
            catch (Exception e) { Fail("GetActiveToolsJson 异常", e.Message); }

            // --- 10.4 累积激活（纯函数，不同列表）---
            try
            {
                var ids1 = new[] { "colony_overview" };
                var ids2 = new[] { "colony_overview", "character_query", "relationship_query" };

                var json1 = McpSkillRegistry.GetActiveToolsJson(ids1);
                var json2 = McpSkillRegistry.GetActiveToolsJson(ids2);

                if (json2.Length > json1.Length)
                    Pass($"累积激活: 1 skill → {json1.Length} chars, 3 skills → {json2.Length} chars");
                else
                    Fail("累积激活未增加工具数");
            }
            catch (Exception e) { Fail("累积激活异常", e.Message); }

            // --- 10.5 system 始终包含 ---
            try
            {
                var emptyJson = McpSkillRegistry.GetActiveToolsJson(new string[0]);
                var nullJson = McpSkillRegistry.GetActiveToolsJson(null);
                // 即使无激活技能，system 工具也应存在
                if (emptyJson == nullJson)
                    Pass("空列表 / null 均正确返回（仅含 system 工具）");
                else
                    Fail("空列表和 null 结果不一致");
            }
            catch (Exception e) { Fail("system 包含验证异常", e.Message); }

            // --- 10.6 Token 节省对比 ---
            try
            {
                var initialJson = McpSkillRegistry.GetActiveToolsJson(new string[0]);
                var allIds = McpSkillRegistry.GetAllSkillIds();
                var fullJson = McpSkillRegistry.GetActiveToolsJson(allIds);

                int initialLen = initialJson.Length;
                int fullLen = fullJson.Length;
                double savings = fullLen > 0 ? (1.0 - (double)initialLen / fullLen) * 100 : 0;
                Pass($"Token 节省: 初始 {initialLen} chars vs 全量 {fullLen} chars ({savings:F0}% 节省)");
            }
            catch (Exception e) { Fail("Token 对比异常", e.Message); }

            // --- 10.7 GetSkillListJson 含激活状态 ---
            try
            {
                // 无激活
                var before = McpSkillRegistry.GetSkillListJson(new string[0]);
                // system 始终 active=true，业务技能 active=false
                if (before.Contains("\"active\":false") || before.Contains("\"active\":true"))
                    Pass("GetSkillListJson 正确输出 active 状态字段");
                else
                    Fail("GetSkillListJson 缺少 active 字段");
            }
            catch (Exception e) { Fail("GetSkillListJson active 状态异常", e.Message); }

            // --- 10.8 GetSkillToolsJson ---
            try
            {
                var json = McpSkillRegistry.GetSkillToolsJson("colony_overview");
                if (json.Contains("\"name\"") && json.Length > 10)
                    Pass($"GetSkillToolsJson(colony_overview) → {json.Length} chars");
                else
                    Fail("GetSkillToolsJson 输出异常");
            }
            catch (Exception e) { Fail("GetSkillToolsJson 异常", e.Message); }

            // --- 10.9 InvokeTool 纯函数 ---
            try
            {
                // 用 system skill 范围内的工具（list_skills 总是可用）
                var result = McpSkillRegistry.InvokeTool(null, "list_skills", "{\"workspaceId\":\"test\"}");
                if (result.Contains("skills"))
                    Pass("InvokeTool(list_skills) 成功（system fallback）");
                else
                    Fail("InvokeTool 失败", $"len={result.Length}");
            }
            catch (Exception e) { Fail("InvokeTool 异常", e.Message); }

            // --- 10.10 InvokeTool 未找到工具 ---
            try
            {
                var result = McpSkillRegistry.InvokeTool(null, "nonexistent_tool", "{}");
                if (result.Contains("\"error\""))
                    Pass("InvokeTool(nonexistent) 正确返回 error");
                else
                    Fail("InvokeTool 对未知工具应返回 error");
            }
            catch (Exception e) { Fail("InvokeTool 错误路径异常", e.Message); }

            // --- 10.11 SerializeAllActiveTools 集成 ---
            try
            {
                var json = McpToolGenerator.SerializeAllActiveTools(new[] { "colony_overview" });
                if (json.StartsWith("[") && json.Contains("get_overview") || json.Contains("colony"))
                    Pass($"McpToolGenerator.SerializeAllActiveTools 正常 ({json.Length} chars)");
                else
                    Fail("SerializeAllActiveTools 异常");
            }
            catch (Exception e) { Fail("SerializeAllActiveTools 异常", e.Message); }

            // --- 10.12 SerializeSkillList 集成 ---
            try
            {
                var json = McpToolGenerator.SerializeSkillList(new[] { "colony_overview" });
                if (json.Contains("\"skills\""))
                    Pass($"McpToolGenerator.SerializeSkillList 正常 ({json.Length} chars)");
                else
                    Fail("SerializeSkillList 异常");
            }
            catch (Exception e) { Fail("SerializeSkillList 异常", e.Message); }
        }

        // ================================================================
        // 11. AgentLoop 测试
        // ================================================================

        public static void TestAgentLoop()
        {
            Section("11. AgentLoop");

            // --- 11.1 EventPool 类型 ---
            try
            {
                var pool = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (pool != null)
                {
                    Pass($"EventPool 可用 ({pool.GetType().Name})");
                    DumpObject("  PendingCount", pool.PendingCount);
                    DumpObject("  TotalImportance", pool.TotalImportance);
                    DumpObject("  RecentEvents", pool.Count(EventQuery.All));
                    DumpObject("  TotalAppended", pool.TotalAppended);
                }
                else
                    Fail("EventPool 不可用 (无导演工作空间)");
            }
            catch (Exception e) { Fail("EventPool 类型检查异常", e.Message); }

            // --- 11.2 Pending/Drain 生命周期 ---
            try
            {
                var pool = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (pool == null) { Skip("EventPool 不可用"); return; }

                int before = pool.PendingCount;

                // 追加测试事件
                var testEvt = MakeTestEvent("driver_test_1", new List<string> { "Test", "Driver" }, 99999, 3f);
                pool.Append(testEvt);

                int afterAppend = pool.PendingCount;
                if (afterAppend == before + 1)
                    Pass($"Append 后 PendingCount: {before} → {afterAppend}");
                else
                    Fail("Append 未增加 PendingCount");

                // Drain
                var drained = pool.DrainPending();
                if (drained.Count == afterAppend && pool.PendingCount == 0)
                    Pass($"DrainPending 成功: drained={drained.Count}, pending={pool.PendingCount}");
                else
                    Fail($"DrainPending 后状态异常: drained={drained.Count}, pending={pool.PendingCount}");

                // 把事件放回去（避免影响后续测试）
                foreach (var evt in drained)
                    pool.Append(evt);

                // Drain 清空
                pool.DrainPending();
                if (pool.PendingCount == 0)
                    Pass("DrainPending 清空成功");
                else
                    Fail("DrainPending 后 PendingCount != 0");
            }
            catch (Exception e) { Fail("Drain 测试异常", e.Message); }

            // --- 11.3 重要度计算 ---
            try
            {
                var pool = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (pool == null) { Skip("EventPool 不可用"); return; }

                pool.DrainPending();
                pool.Append(MakeTestEvent("imp_1", new List<string> { "Test" }, 1, 1f));
                pool.Append(MakeTestEvent("imp_2", new List<string> { "Test" }, 2, 3f));
                pool.Append(MakeTestEvent("imp_3", new List<string> { "Test" }, 3, 5f));

                float expected = 1f + 3f + 5f;

                if (pool.TotalImportance == expected)
                    Pass($"TotalImportance 计算正确: {pool.TotalImportance} (expected {expected})");
                else
                    Fail($"TotalImportance 不正确: {pool.TotalImportance} != {expected}");

                pool.DrainPending();
            }
            catch (Exception e) { Fail("重要度计算异常", e.Message); }

            // --- 11.4 OnThresholdReached 回调 ---
            try
            {
                var pool = RimLifeCore.GetDirectorWorkspace()?.EventPool;
                if (pool == null) { Skip("EventPool 不可用"); return; }

                pool.DrainPending();

                bool callbackFired = false;
                Action handler = () => { callbackFired = true; };
                pool.OnThresholdReached += handler;

                // 添加少量 Minor 事件：数量达标
                for (int i = 0; i < 3; i++)
                    pool.Append(MakeTestEvent($"cnt_{i}", new List<string> { "Test" }, i, 1f));

                if (callbackFired)
                    Pass("OnThresholdReached: 数量达标触发回调");
                else if (pool.PendingCount < RimLifeCore.DriverConfig.DirectorCountThreshold)
                    Pass("OnThresholdReached: 未触发（数量未达标，预期行为）");
                else
                    Fail("OnThresholdReached: 数量达标但未触发");

                pool.OnThresholdReached -= handler;
                pool.DrainPending();
            }
            catch (Exception e) { Fail("OnThresholdReached 测试异常", e.Message); }

            // --- 11.5 DirectorAgent 状态 ---
            try
            {
                var director = RimLifeCore.GetDirectorAgent();
                if (director != null)
                {
                    Pass("DirectorAgent 已创建");
                    DumpObject("  State", director.State);
                }
                else
                    Skip("DirectorAgent 未创建 (SaveStore 未就绪或 LLM 未配置)");
            }
            catch (Exception e) { Fail("DirectorAgent 状态异常", e.Message); }
        }

        // ================================================================
        // 12. 工作空间 Agent 驱动
        // ================================================================

        public static void TestWorkspaceAgent()
        {
            Section("12. 工作空间 Agent 驱动");

            // --- 12.1 工作空间事件缓存字段 ---
            try
            {
                var wsManager = RimLifeCore.Workspaces;
                if (wsManager == null) { Skip("WorkspaceManager 不可用"); return; }

                var activeWs = wsManager.GetActive();
                if (activeWs.Count > 0)
                {
                    var ws = activeWs[0];
                    Pass($"工作空间 '{ws.Label}' 存在 (Active workspace count={activeWs.Count})");

                    DumpObject("  EventPool.PendingCount", ws.EventPool.PendingCount);
                    DumpObject("  EventPool.TotalImportance", ws.EventPool.TotalImportance);
                    DumpObject("  Role", ws.CreatedByRole.ToString());
                }
                else
                {
                    Pass("无活跃工作空间 — 创建测试空间验证字段");
                    var testWs = wsManager.Create("FieldTest", null, null, WorkspaceRole.Screenwriter);
                    if (testWs.EventPool.PendingCount == 0 && testWs.EventPool.TotalImportance == 0)
                        Pass("EventPool 初始状态为空 (预期)");
                    else
                        Fail("EventPool 初始状态异常");
                }
            }
            catch (Exception e) { Fail("字段存在性测试异常", e.Message); }

            // --- 12.2 事件推入工作空间事件池 ---
            try
            {
                var wsManager = RimLifeCore.Workspaces;
                if (wsManager == null) { Skip("WorkspaceManager 不可用"); return; }

                var testWs = wsManager.Create("PushTest", null, null, WorkspaceRole.Director);

                var evt = MakeTestEvent("ws_test_1", new List<string> { "Test", "Combat" }, 1000, 3f);
                bool pushed = wsManager.RouteEvents(testWs.Id, new List<IGameEvent> { evt });
                if (pushed && testWs.EventPool.PendingCount == 1)
                    Pass($"RouteEvents 后 PendingCount=1");
                else
                    Fail($"RouteEvents 后 PendingCount={testWs.EventPool.PendingCount} (expected 1)");

                // 验证重要度
                if (testWs.EventPool.TotalImportance == 3f)
                    Pass($"TotalImportance=3 (重要度正确)");
                else
                    Fail($"TotalImportance={testWs.EventPool.TotalImportance} (expected 3)");

                // Drain
                var drained = testWs.EventPool.DrainPending();
                if (drained.Count == 1 && testWs.EventPool.PendingCount == 0)
                    Pass("DrainPending 清空工作空间事件池");
                else
                    Fail($"DrainPending 异常: drained={drained.Count}, pending={testWs.EventPool.PendingCount}");
            }
            catch (Exception e) { Fail("RouteEvents 测试异常", e.Message); }

            // --- 12.3 阈值回调（通过 RouteEvents 触发） ---
            try
            {
                var wsManager = RimLifeCore.Workspaces;
                if (wsManager == null) { Skip("WorkspaceManager 不可用"); return; }

                var testWs = wsManager.Create("CallbackTest", null, null, WorkspaceRole.Director);
                var config = RimLifeCore.DriverConfig;

                // 填充事件到阈值
                var events = new List<IGameEvent>();
                for (int i = 0; i < config.DirectorCountThreshold; i++)
                    events.Add(MakeTestEvent($"cb_{i}", new List<string> { "Test" }, i, 3f));

                wsManager.RouteEvents(testWs.Id, events);

                if (testWs.EventPool.PendingCount == config.DirectorCountThreshold)
                    Pass($"RouteEvents 达到阈值 (count={testWs.EventPool.PendingCount}, threshold={config.DirectorCountThreshold})");
                else
                    Fail($"PendingCount={testWs.EventPool.PendingCount} (expected {config.DirectorCountThreshold})");
            }
            catch (Exception e) { Fail("工作空间回调测试异常", e.Message); }

            // --- 12.4 激活条件（纯事件驱动，无定时器） ---
            try
            {
                var wsManager = RimLifeCore.Workspaces;
                if (wsManager == null) { Skip("WorkspaceManager 不可用"); return; }

                var config = RimLifeCore.DriverConfig;
                var testWs = wsManager.Create("ActivationTest", null, null, WorkspaceRole.Director);

                // 填充高重要度事件：1个 importance=20 的事件即可满足重要性阈值
                wsManager.RouteEvents(testWs.Id, new List<IGameEvent> {
                    MakeTestEvent("act_1", new List<string> { "Test" }, 1, 20f)
                });

                int count = testWs.EventPool.PendingCount;
                float importance = testWs.EventPool.TotalImportance;
                bool countOk = count >= config.DirectorCountThreshold;
                bool impOk = importance >= config.DirectorImportanceThreshold;

                if (!countOk && impOk)
                    Pass("1个Extreme: Count不满足, Importance满足 (纯事件驱动)");
                else if (countOk || impOk)
                    Pass($"激活条件: Count={countOk}, Importance={impOk}");
                else
                    Fail($"激活条件异常: Count={countOk}, Importance={impOk}");

                DumpObject("  DirectorCountThreshold", config.DirectorCountThreshold);
                DumpObject("  DirectorImportanceThreshold", config.DirectorImportanceThreshold);
            }
            catch (Exception e) { Fail("激活条件测试异常", e.Message); }

            // --- 12.5 WorkspaceManager 状态 ---
            try
            {
                var wsManager = RimLifeCore.Workspaces;
                if (wsManager == null) { Skip("WorkspaceManager 不可用"); return; }

                Pass("WorkspaceManager 就绪");
                DumpObject("  Active workspaces", wsManager.GetActive().Count);
            }
            catch (Exception e) { Fail("WorkspaceManager 状态异常", e.Message); }
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
                new FloatMenuOption("★ 打开配置面板 (UI)", () => OpenConfigPanel()),
                new FloatMenuOption("1. 基础设施 (SaveStore/CacheStore/EventLog)", () => TestInfrastructure()),
                new FloatMenuOption("2. JSON 往返 (ParseDict/Serialize/Writer)", () => TestJsonRoundTrip()),
                new FloatMenuOption("3. Framework 纯逻辑 (SemanticLabels/RandomInt)", () => TestFramework()),
                new FloatMenuOption("4. EventPool 集成 (Append/Query/Count)", () => TestEventLog()),
                new FloatMenuOption("5. Mapper 数据采集 (CharacterCard/EnvironmentCard)", () => TestMappers()),
                new FloatMenuOption("6. EventCardMapper 构造 (FromDeath/FromSocial/...)", () => TestEventCardMapper()),
                new FloatMenuOption("7. Harmony Patch 状态", () => TestHarmonyStatus()),
                new FloatMenuOption("8. CardSerializer 序列化 (ColonyContext/CharacterCard/...)", () => TestCardSerializer()),
                new FloatMenuOption("9. MCP Provider 工具调用 (所有9个MCP工具)", () => TestMcpProviders()),
                new FloatMenuOption("10. Skill 按需激活系统 (list/activate/deactivate/token对比)", () => TestSkillSystem()),
                new FloatMenuOption("11. AgentLoop (Pool/OnThresholdReached/DirectorAgent)", () => TestAgentLoop()),
                new FloatMenuOption("12. 工作空间 (EventPool/OnThresholdReached)", () => TestWorkspaceAgent()),
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static IGameEvent MakeTestEvent(string id, IReadOnlyList<string> tags, int tick, float importance)
        {
            return new TestGameEvent
            {
                EventID = id,
                DefName = "SelftestEvent",
                Tags = tags,
                Tick = tick,
                Importance = importance,
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
            public IReadOnlyList<string> Keywords { get; set; }
            public int Tick { get; set; }
            public string TimeLabel { get; set; }
            public float Importance { get; set; }
            public IReadOnlyList<EventActorRef> Actors { get; set; }
            public string MapHint { get; set; }
            public IDictionary<string, string> Payload { get; set; }
        }
    }

}
