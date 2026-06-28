using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using RimLife.Data;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife.Infrastructure.Mcp
{
    /// <summary>
    /// 关系网络 Skill 的 Hook Provider。
    /// 提供社交关系、牵绊动物、机械体从属关系的异构查询。
    /// 遵循"只收集已知信息，失败则忽略"原则，不同 Pawn 类型返回不同字段组合。
    /// </summary>
    public class RelationshipQueryProvider : IMcpHookProvider
    {
        public string HookId => "relationship_query";
        public string HookName => "关系网络";
        public string HookDescription => "查询角色社交关系、牵绊动物、机械体从属、交互历史流水";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(RelationshipQueryProvider).GetMethod(nameof(GetRelationships))),
                McpTool.FromMethod(typeof(RelationshipQueryProvider).GetMethod(nameof(GetInteractionHistory))),
                McpTool.FromMethod(typeof(RelationshipQueryProvider).GetMethod(nameof(GetRelationshipBetween))),
            };
        }

        // ───────────── 内部采集辅助 ─────────────

        /// <summary>写入 Pawn 基本身份信息。</summary>
        private static void WriteIdentity(JsonWriter w, Pawn p)
        {
            w.Prop("id", p.ThingID);
            w.Prop("name", p.Name?.ToStringShort ?? p.LabelShortCap ?? "?");
        }

        /// <summary>采集社交关系（DirectRelations + 殖民地平均好感）。</summary>
        private static void CollectSocial(JsonWriter w, Pawn pawn)
        {
            try
            {
                if (pawn.relations == null) return;

                var directRels = pawn.relations.DirectRelations;
                if (directRels != null && directRels.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var dr in directRels)
                    {
                        if (dr?.otherPawn == null) continue;
                        try
                        {
                            float opinion = pawn.relations.OpinionOf(dr.otherPawn);
                            string tier = SemanticLabels.MapOpinionTier(opinion);
                            var rw = new JsonWriter(96);
                            rw.Prop("name", dr.otherPawn.Name?.ToStringShort ?? "?");
                            rw.Prop("type", dr.def?.defName ?? "Unknown");
                            rw.Prop("opinion", tier);
                            items.Add(rw.Close());
                        }
                        catch { }
                    }
                    if (items.Count > 0) w.ArrayRaw("social", items.ToArray());
                }

                try
                {
                    var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                    if (colonists != null && colonists.Count > 0)
                    {
                        float sum = 0f;
                        int cnt = 0;
                        foreach (var c in colonists)
                        {
                            if (c == pawn || c?.relations == null) continue;
                            try { sum += c.relations.OpinionOf(pawn); cnt++; } catch { }
                        }
                        if (cnt > 0) w.Prop("colonyOpinion", SemanticLabels.MapOpinionTier(sum / cnt));
                    }
                }
                catch { }
            }
            catch { }
        }

        /// <summary>采集牵绊动物（从 DirectRelations 中查找 Bond）。</summary>
        private static void CollectBonds(JsonWriter w, Pawn pawn)
        {
            try
            {
                if (pawn.relations?.DirectRelations == null) return;
                var bondDef = PawnRelationDefOf.Bond;
                var bonds = new List<string>();

                foreach (var dr in pawn.relations.DirectRelations)
                {
                    if (dr?.def != bondDef || dr.otherPawn == null) continue;
                    try
                    {
                        var bw = new JsonWriter(80);
                        bw.Prop("id", dr.otherPawn.ThingID);
                        bw.Prop("name", dr.otherPawn.Name?.ToStringShort ?? dr.otherPawn.LabelShortCap ?? "?");
                        bonds.Add(bw.Close());
                    }
                    catch { }
                }

                if (bonds.Count > 0) w.ArrayRaw("bonded", bonds.ToArray());
            }
            catch { }
        }

        /// <summary>
        /// 采集机械体关系：overseer（当前 Pawn 的监管者）和 subordinates（当前 Pawn 监管的机械体）。
        /// </summary>
        private static void CollectMechanoid(JsonWriter w, Pawn pawn)
        {
            // Overseer（当前 Pawn 是机械体，查找其监管者）
            try
            {
                if (pawn.RaceProps.IsMechanoid)
                {
                    Pawn overseer = pawn.GetOverseer();
                    if (overseer != null)
                    {
                        var ow = new JsonWriter(96);
                        ow.Prop("id", overseer.ThingID);
                        ow.Prop("name", overseer.Name?.ToStringShort ?? overseer.LabelShortCap ?? "?");
                        w.PropRaw("overseer", ow.Close());
                    }
                }
            }
            catch { }

            // Subordinates（当前 Pawn 是 mechanitor，查找其下属机械体）
            try
            {
                var maps = Find.Maps;
                if (maps == null) return;

                var subs = new List<string>();
                foreach (var map in maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;
                    foreach (var m in map.mapPawns.AllPawnsSpawned)
                    {
                        if (m == null || m.Dead || m == pawn) continue;
                        try
                        {
                            if (!m.RaceProps.IsMechanoid) continue;
                            if (m.GetOverseer() != pawn) continue;
                            var sw = new JsonWriter(80);
                            sw.Prop("id", m.ThingID);
                            sw.Prop("name", m.Name?.ToStringShort ?? m.LabelShortCap ?? "?");
                            subs.Add(sw.Close());
                        }
                        catch { }
                        if (subs.Count >= 10) break;
                    }
                    if (subs.Count >= 10) break;
                }

                if (subs.Count > 0) w.ArrayRaw("subordinates", subs.ToArray());
            }
            catch { }
        }

        // ───────────── MCP 工具方法 ─────────────

        /// <summary>
        /// 获取指定角色的关系网络。
        /// 包含社交关系、牵绊动物、机械体从属等，按 Pawn 类型异构返回。
        /// </summary>
        [McpTool(Name = "get_relationships",
                 Description = "获取指定角色的关系网络：社交关系、牵绊动物、机械体从属。返回结构化 JSON，字段因角色类型而异。")]
        public static string GetRelationships(
            [McpParam(Description = "角色唯一 ID")] string pawnId)
        {
            try
            {
                var pawn = PawnQueryHelper.FindPawnById(pawnId);
                if (pawn == null) return "{}";

                var w = new JsonWriter(1024);
                WriteIdentity(w, pawn);
                CollectSocial(w, pawn);
                CollectBonds(w, pawn);
                CollectMechanoid(w, pawn);

                return w.Close();
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.RelationshipQueryProvider] get_relationships({pawnId}) failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 获取指定角色近期的社交互动流水记录。
        /// </summary>
        [McpTool(Name = "get_interaction_history",
                 Description = "获取指定角色近期的社交互动流水记录，用于理解角色间动态。")]
        public static string GetInteractionHistory(
            [McpParam(Description = "角色唯一 ID")] string pawnId,
            [McpParam(Description = "起始 tick（含），默认 5000 ticks 前",
                      Required = McpRequired.False)] int? sinceTick = null,
            [McpParam(Description = "最大返回数，默认 20")] int limit = 20)
        {
            try
            {
                var store = RimLifeCore.InteractionStore;
                if (store == null) return "[]";

                var records = store.QueryByPawn(pawnId, sinceTick, limit);
                return CardSerializer.Default.SerializeInteractionList(records);
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.RelationshipQueryProvider] get_interaction_history({pawnId}) failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取两个角色之间的双向关系摘要。
        /// 包含：社交关系、牵绊、从属、双向好感、兼容度、互动频率、近期互动概要。
        /// 所有字段均已语义化，零裸数值。
        /// </summary>
        [McpTool(Name = "get_relationship_between",
                 Description = "获取两个角色之间的关系摘要：社交关系、牵绊、从属、双向好感、兼容度、互动频率。所有信息已语义化。")]
        public static string GetRelationshipBetween(
            [McpParam(Description = "角色 A 的唯一 ID")] string pawnIdA,
            [McpParam(Description = "角色 B 的唯一 ID")] string pawnIdB)
        {
            try
            {
                var pawnA = PawnQueryHelper.FindPawnById(pawnIdA);
                var pawnB = PawnQueryHelper.FindPawnById(pawnIdB);
                if (pawnA == null || pawnB == null) return "{}";

                string nameA = pawnA.Name?.ToStringShort ?? pawnA.LabelShortCap ?? "?";
                string nameB = pawnB.Name?.ToStringShort ?? pawnB.LabelShortCap ?? "?";

                var w = new JsonWriter(1024);

                // Identity
                var wa = new JsonWriter(64);
                WriteIdentity(wa, pawnA);
                w.PropRaw("a", wa.Close());

                var wb = new JsonWriter(64);
                WriteIdentity(wb, pawnB);
                w.PropRaw("b", wb.Close());

                // Bond check (A→B or B→A)
                try
                {
                    var bondDef = PawnRelationDefOf.Bond;
                    bool bonded =
                        (pawnA.relations?.DirectRelations?.Any(r => r.def == bondDef && r.otherPawn == pawnB) ?? false) ||
                        (pawnB.relations?.DirectRelations?.Any(r => r.def == bondDef && r.otherPawn == pawnA) ?? false);
                    if (bonded) w.Prop("bonded", true);
                }
                catch { }

                // Overseer / subordinate check
                try
                {
                    if (pawnA.RaceProps.IsMechanoid && pawnA.GetOverseer() == pawnB)
                        w.Prop("overseer", nameB);
                    else if (pawnB.RaceProps.IsMechanoid && pawnB.GetOverseer() == pawnA)
                        w.Prop("overseer", nameA);
                }
                catch { }

                // Direct social relations between A and B
                var relationTypes = new List<string>();
                bool reciprocal = false;
                var directAB = pawnA.relations?.DirectRelations;
                if (directAB != null)
                {
                    foreach (var dr in directAB)
                    {
                        if (dr?.otherPawn != pawnB) continue;
                        string relType = dr.def?.defName ?? "Unknown";
                        relationTypes.Add(relType);
                        reciprocal = pawnB.relations?.DirectRelations?
                            .Any(r => r.otherPawn == pawnA && r.def == dr.def) ?? false;
                    }
                }
                w.Prop("relation", relationTypes.Count > 0
                    ? string.Join(", ", relationTypes) : "none");
                w.Prop("reciprocal", reciprocal);

                // Opinion tiers
                float opinionAB = pawnA.relations?.OpinionOf(pawnB) ?? 0f;
                w.Prop("ab", SemanticLabels.MapOpinionTier(opinionAB));
                float opinionBA = pawnB.relations?.OpinionOf(pawnA) ?? 0f;
                w.Prop("ba", SemanticLabels.MapOpinionTier(opinionBA));

                // Compatibility (bidirectional, symmetric)
                try
                {
                    float compat = pawnA.relations?.CompatibilityWith(pawnB) ?? 0f;
                    w.Prop("compatibility", MapCompatibilityTier(compat));
                }
                catch { }

                // Interaction history summary
                string freqTier = "none";
                string recentText = "";
                var store = RimLifeCore.InteractionStore;
                if (store != null)
                {
                    int count = store.Count(pawnIdA, pawnIdB);
                    freqTier = MapInteractionFrequency(count);

                    if (count > 0)
                    {
                        var records = store.Query(pawnIdA, pawnIdB, limit: 5);
                        recentText = string.Join(", ", records.Select(r =>
                            $"{r.InteractionDef}({r.Outcome})"));
                    }
                }
                w.Prop("interactions", freqTier);
                if (!string.IsNullOrEmpty(recentText))
                    w.Prop("recent", recentText);

                return w.Close();
            }
            catch (Exception e)
            {
                RimLifeCore.Logger?.Warning($"[RimLife.RelationshipQueryProvider] get_relationship_between({pawnIdA}, {pawnIdB}) failed: {e.Message}");
                return "{}";
            }
        }

        // ───────────── 语义化映射 ─────────────

        private static string MapCompatibilityTier(float compat)
        {
            // CompatibilityWith 典型范围 -5 ~ 5，但特质叠加可能超出，故 clamp 到 [-5, 5]
            compat = Math.Max(-5f, Math.Min(5f, compat));
            if (compat >= 3f) return "Great";
            if (compat >= 1f) return "Good";
            if (compat >= -1f) return "Average";
            if (compat >= -3f) return "Poor";
            return "Incompatible";
        }

        private static string MapInteractionFrequency(int count)
        {
            if (count <= 0) return "none";
            if (count <= 3) return "rare";
            if (count <= 10) return "occasional";
            return "frequent";
        }
    }
}
