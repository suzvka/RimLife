using RimLife.Cards;
using RimLife.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace RimLife.Framework.Mcp
{
    /// <summary>
    /// Card DTO → JSON 序列化器。纯静态，零 RimWorld 依赖。
    /// 供各 MCP Provider 调用，将查询结果转为 LLM 可消费的 JSON。
    /// </summary>
    public static class CardSerializer
    {
        // ================================================================
        // IGameEvent
        // ================================================================

        public static string SerializeEvent(IGameEvent evt)
        {
            var w = new JsonWriter(512);
            w.Prop("eventId", evt.EventID);
            w.Prop("defName", evt.DefName);
            w.PropRaw("tags", SerializeStringList(evt.Tags));
            w.Prop("tick", evt.Tick);
            w.Prop("severity", evt.Severity);
            w.Prop("mapHint", evt.MapHint ?? "");

            if (evt.Actors != null && evt.Actors.Count > 0)
            {
                var actorJsons = new List<string>();
                foreach (var a in evt.Actors)
                {
                    var aw = new JsonWriter(128);
                    aw.Prop("id", a.ID);
                    aw.Prop("name", a.Name);
                    aw.Prop("role", a.Role);
                    aw.Prop("refType", a.RefType);
                    actorJsons.Add(aw.Close());
                }
                w.ArrayRaw("actors", actorJsons);
            }

            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                var pw = new JsonWriter(256);
                foreach (var kv in evt.Payload)
                    pw.Prop(kv.Key, kv.Value ?? "");
                w.PropRaw("payload", pw.Close());
            }

            return w.Close();
        }

        public static string SerializeEventList(IReadOnlyList<IGameEvent> events)
        {
            return SerializeObjectList(events, SerializeEvent);
        }

        // ================================================================
        // ColonyContext
        // ================================================================

        public static string SerializeColonyContext(ColonyContext ctx)
        {
            if (ctx == null) return "{}";
            var w = new JsonWriter(1024);

            // 时间
            w.Prop("currentTick", ctx.CurrentTick);
            w.Prop("season", ctx.Season ?? "");
            w.Prop("timeOfDay", ctx.TimeOfDay ?? "");
            w.Prop("quadrum", ctx.Quadrum ?? "");
            w.Prop("year", ctx.Year);
            w.Prop("dayOfQuadrum", ctx.DayOfQuadrum);
            w.Prop("hour", ctx.Hour);

            // 人口
            w.Prop("populationAlive", ctx.PopulationAlive);
            w.Prop("populationDowned", ctx.PopulationDowned);
            w.Prop("populationMentalBreak", ctx.PopulationMentalBreak);

            // 殖民者摘要
            if (ctx.Colonists != null && ctx.Colonists.Count > 0)
            {
                var summaries = new List<string>();
                foreach (var c in ctx.Colonists)
                    summaries.Add(SerializeColonistSummary(c));
                w.ArrayRaw("colonists", summaries);
            }

            // 派系
            if (ctx.FactionRelations != null && ctx.FactionRelations.Count > 0)
            {
                var factions = new List<string>();
                foreach (var f in ctx.FactionRelations)
                {
                    var fw = new JsonWriter(128);
                    fw.Prop("factionName", f.FactionName ?? "");
                    fw.Prop("goodwill", f.Goodwill, "F0");
                    fw.Prop("relationLabel", f.RelationLabel ?? "");
                    factions.Add(fw.Close());
                }
                w.ArrayRaw("factionRelations", factions);
            }

            // 资源
            w.Prop("wealthTotal", ctx.WealthTotal, "F0");
            w.Prop("foodStatus", ctx.FoodStatus ?? "");
            w.Prop("powerStatus", ctx.PowerStatus ?? "");

            // 士气
            w.Prop("moraleAverage", ctx.MoraleAverage, "F2");
            w.Prop("moraleTier", ctx.MoraleTier ?? "");

            // 威胁
            w.Array("activeThreats", ctx.ActiveThreats);

            // 叙事者
            w.Prop("storytellerName", ctx.StorytellerName ?? "");
            w.Prop("difficulty", ctx.Difficulty ?? "");
            w.Prop("techLevel", ctx.TechLevel ?? "");
            w.Prop("colonyStartTick", ctx.ColonyStartTick);

            return w.Close();
        }

        private static string SerializeColonistSummary(ColonistSummary c)
        {
            var w = new JsonWriter(128);
            w.Prop("id", c.ID ?? "");
            w.Prop("name", c.Name ?? "");
            w.Prop("isDowned", c.IsDowned);
            w.Prop("isDead", c.IsDead);
            w.Prop("currentJob", c.CurrentJob ?? "");
            w.Prop("moodTier", c.MoodTier ?? "");
            w.Prop("painTier", c.PainTier ?? "");
            w.Prop("pawnRelation", c.PawnRelation ?? "");
            return w.Close();
        }

        // ================================================================
        // CharacterCard (view 分层)
        // ================================================================

        /// <summary>
        /// 序列化 CharacterCard。view 控制数据层级：
        /// "static"（默认）= 客观属性；"dynamic" = + 视角/记忆快照；"full" = + 完整记忆流水。
        /// </summary>
        public static string SerializeCharacterCard(CharacterCard card, string view,
            IPawnPromptProvider promptProvider)
        {
            if (card == null) return "{}";
            var w = new JsonWriter(4096);

            // 基本元数据（始终包含）
            w.Prop("id", card.ID ?? "");
            w.Prop("name", card.Name ?? "");
            w.Prop("fullName", card.FullName ?? "");
            w.Prop("defName", card.DefName ?? "");
            w.Prop("factionLabel", card.FactionLabel ?? "");
            w.Prop("ageBiologicalYears", card.AgeBiologicalYears, "F1");
            w.Prop("gender", card.Gender ?? "");
            w.Prop("pawnType", card.PawnType ?? "");
            w.Prop("pawnRelation", card.PawnRelation ?? "");
            w.Prop("isDead", card.IsDead);
            w.Prop("isDowned", card.IsDowned);
            w.Prop("isAwake", card.IsAwake);

            // 一次调用获取全部语义文本
            string prompt = promptProvider?.GetCharacterPrompt(card.ID, view);
            w.Prop("prompt", prompt ?? "");

            w.Prop("view", string.IsNullOrEmpty(view) ? "static" : view);
            return w.Close();
        }

        // ================================================================
        // ObjectiveCard
        // ================================================================

        public static string SerializeObjective(ObjectiveCard obj)
        {
            if (obj == null) return "{}";
            var w = new JsonWriter(256);
            w.Prop("id", obj.ID ?? "");
            w.Prop("title", obj.Title ?? "");
            w.Prop("description", obj.Description ?? "");
            w.Prop("status", obj.Status ?? "");
            w.Prop("source", obj.Source ?? "");
            if (obj.DeadlineTick.HasValue)
                w.Prop("deadlineTick", obj.DeadlineTick.Value);

            if (obj.Steps != null && obj.Steps.Count > 0)
            {
                var steps = new List<string>();
                foreach (var s in obj.Steps)
                {
                    var sw = new JsonWriter(64);
                    sw.Prop("label", s.Label ?? "");
                    sw.Prop("isCompleted", s.IsCompleted);
                    steps.Add(sw.Close());
                }
                w.ArrayRaw("steps", steps);
            }
            return w.Close();
        }

        public static string SerializeObjectiveList(IReadOnlyList<ObjectiveCard> objectives)
        {
            return SerializeObjectList(objectives, SerializeObjective);
        }

        // ================================================================
        // EnvironmentCard
        // ================================================================

        public static string SerializeEnvironment(EnvironmentCard env)
        {
            if (env == null) return "{}";
            var w = new JsonWriter(512);
            w.Prop("type", env.Type ?? "");
            w.Prop("temperature", env.Temperature, "F1");
            w.Prop("lightLevel", env.LightLevel, "F2");
            w.Prop("thermalComfort", env.ThermalComfort ?? "");
            w.Prop("lightLabel", env.LightLabel ?? "");

            if (env.Room != null)
            {
                var rw = new JsonWriter(256);
                rw.Prop("roleLabel", env.Room.RoleLabel ?? "");
                rw.Prop("impressiveness", env.Room.BaseStats.Impressiveness, "F1");
                rw.Prop("beauty", env.Room.BaseStats.Beauty, "F1");
                rw.Prop("wealth", env.Room.BaseStats.Wealth, "F1");
                rw.Prop("space", env.Room.BaseStats.Space, "F1");
                rw.Prop("cleanliness", env.Room.BaseStats.Cleanliness, "F1");
                if (env.Room.Tags != null && env.Room.Tags.Count > 0)
                    rw.Array("tags", env.Room.Tags);
                w.PropRaw("room", rw.Close());
            }

            if (!string.IsNullOrEmpty(env.Weather.Label))
            {
                var ww = new JsonWriter(128);
                var we = env.Weather;
                ww.Prop("label", we.Label ?? "");
                ww.Prop("description", we.Description ?? "");
                ww.Prop("isRain", we.IsRain);
                ww.Prop("isSnow", we.IsSnow);
                ww.Prop("windSpeed", we.WindSpeed, "F1");
                w.PropRaw("weather", ww.Close());
            }

            // ThingSummary
            if (env.ThingSummary != null && env.ThingSummary.Count > 0)
            {
                var tw = new JsonWriter(128);
                foreach (var kv in env.ThingSummary)
                    tw.Prop(kv.Key, kv.Value);
                w.PropRaw("thingSummary", tw.Close());
            }

            return w.Close();
        }

        // ================================================================
        // InteractionRecord
        // ================================================================

        public static string SerializeInteraction(InteractionRecord rec)
        {
            var w = new JsonWriter(128);
            w.Prop("tick", rec.Tick);
            w.Prop("initiatorId", rec.InitiatorID ?? "");
            w.Prop("recipientId", rec.RecipientID ?? "");
            w.Prop("interactionDef", rec.InteractionDef ?? "");
            w.Prop("outcome", rec.Outcome ?? "");
            return w.Close();
        }

        public static string SerializeInteractionList(IReadOnlyList<InteractionRecord> records)
        {
            return SerializeObjectList(records, SerializeInteraction);
        }

        // ================================================================
        // ColonistSummary (轻量列表，供 find_characters 用)
        // ================================================================

        public static string SerializeColonistSummaryList(IReadOnlyList<ColonistSummary> colonists)
        {
            return SerializeObjectList(colonists, SerializeColonistSummary);
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        private static string SerializeStringList(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(JsonHelper.Escape(items[i] ?? ""));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private delegate string Serializer<T>(T item);

        private static string SerializeObjectList<T>(IReadOnlyList<T> items, Serializer<T> serialize)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(serialize(items[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
