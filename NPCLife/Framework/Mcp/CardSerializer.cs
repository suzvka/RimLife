using NPCLife.Cards;
using NPCLife.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// Card DTO → JSON 序列化器。零 RimWorld 依赖。
    /// 供各 MCP Provider 调用，将查询结果转为 LLM 可消费的 JSON。
    /// Infrastructure 层可通过 <see cref="Default"/> 静态实例直接使用。
    /// </summary>
    public class CardSerializer : ICardSerializer
    {
        /// <summary>默认单例，供 Infrastructure 层静态调用。</summary>
        public static readonly CardSerializer Default = new CardSerializer();
        // ================================================================
        // IGameEvent
        // ================================================================

        public string SerializeEvent(IGameEvent evt)
        {
            var w = new JsonWriter(512);
            w.Prop("eventId", evt.EventID);
            w.Prop("defName", evt.DefName);
            w.PropRaw("tags", SerializeStringList(evt.Tags));
            if (evt.Keywords != null && evt.Keywords.Count > 0)
                w.PropRaw("keywords", SerializeStringList(evt.Keywords));
            w.Prop("tick", evt.Tick);
            w.Prop("importance", evt.Importance, "F2");
            w.Prop("mapHint", evt.MapHint);

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
                    pw.Prop(kv.Key, kv.Value);
                w.PropRaw("payload", pw.Close());
            }

            SerializeExtensions(w, evt as IExtensibleCard);
            return w.Close();
        }

        public string SerializeEventList(IReadOnlyList<IGameEvent> events)
        {
            return SerializeObjectList(events, SerializeEvent);
        }

        /// <summary>
        /// 将序列化的事件 JSON 反序列化为 EventCardData。
        /// 与 SerializeEvent 互为逆操作，用于从 KV 缓存恢复 IGameEvent 对象。
        /// </summary>
        public IGameEvent DeserializeEvent(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var dict = JsonParser.ParseDict(json);
            if (dict == null || dict.Count == 0) return null;

            var evt = new EventCardData
            {
                EventID = dict.TryGetValue("eventId", out var v) ? v : "",
                DefName = dict.TryGetValue("defName", out v) ? v : "",
                Tags = DeserializeTagList(dict.TryGetValue("tags", out v) ? v : "[]"),
                Keywords = DeserializeTagList(dict.TryGetValue("keywords", out v) ? v : "[]"),
                Tick = dict.TryGetValue("tick", out v) && int.TryParse(v, out var tick) ? tick : 0,
                Importance = dict.TryGetValue("importance", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var imp) ? imp : 0f,
                MapHint = dict.TryGetValue("mapHint", out v) ? v : "",
                Actors = DeserializeActors(dict.TryGetValue("actors", out v) ? v : "[]"),
                Payload = DeserializePayload(dict.TryGetValue("payload", out v) ? v : "{}")
            };
            return evt;
        }

        /// <summary>
        /// 序列化事件 KV 缓存为 JSON 对象。供 WorkspaceState 持久化。
        /// </summary>
        public string SerializeEventCache(Dictionary<string, string> eventCache)
        {
            if (eventCache == null || eventCache.Count == 0) return "{}";
            var w = new JsonWriter(eventCache.Count * 256);
            foreach (var kv in eventCache)
                w.Prop(kv.Key, kv.Value ?? "");
            return w.Close();
        }

        /// <summary>
        /// 反序列化事件 KV 缓存。
        /// </summary>
        public Dictionary<string, string> DeserializeEventCache(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return new Dictionary<string, string>();
            return JsonParser.ParseDict(json);
        }

        // ================================================================
        // ColonyContext
        // ================================================================

        public string SerializeColonyContext(ColonyContext ctx)
        {
            if (ctx == null) return "{}";
            var w = new JsonWriter(1024);

            // 时间
            w.Prop("currentTick", ctx.CurrentTick);
            w.Prop("season", ctx.Season);
            w.Prop("timeOfDay", ctx.TimeOfDay);
            w.Prop("year", ctx.Year);
            w.Prop("hour", ctx.Hour);

            // 人口
            w.Prop("populationAlive", ctx.PopulationAlive);

            // 角色摘要
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
                    fw.Prop("factionName", f.FactionName);
                    fw.Prop("goodwill", f.Goodwill, "F0");
                    fw.Prop("relationLabel", f.RelationLabel);
                    factions.Add(fw.Close());
                }
                w.ArrayRaw("factionRelations", factions);
            }

            // 资源
            w.Prop("foodStatus", ctx.FoodStatus);
            w.Prop("powerStatus", ctx.PowerStatus);

            // 士气
            w.Prop("moraleAverage", ctx.MoraleAverage, "F2");
            w.Prop("moraleTier", ctx.MoraleTier);

            // 威胁
            w.Array("activeThreats", ctx.ActiveThreats);

            // 难度
            w.Prop("difficulty", ctx.Difficulty);
            w.Prop("techLevel", ctx.TechLevel);
            w.Prop("colonyStartTick", ctx.ColonyStartTick);

            SerializeExtensions(w, ctx);
            return w.Close();
        }

        private static string SerializeColonistSummary(ColonistSummary c)
        {
            var w = new JsonWriter(128);
            w.Prop("id", c.ID);
            w.Prop("name", c.Name);
            w.Prop("isDead", c.IsDead);
            w.Prop("currentJob", c.CurrentJob);
            w.Prop("moodTier", c.MoodTier);
            w.Prop("painTier", c.PainTier);
            w.Prop("pawnRelation", c.PawnRelation);
            return w.Close();
        }

        // ================================================================
        // CharacterCard (view 分层)
        // ================================================================

        /// <summary>
        /// 序列化 CharacterCard。view 控制数据层级：
        /// "static"（默认）= 客观属性；"dynamic" = + 视角/记忆快照；"full" = + 完整记忆流水。
        /// 通过 ICharacterContentProvider 钩子收集各 section 内容，组装为结构化 JSON。
        /// </summary>
        public string SerializeCharacterCard(CharacterCard card, string view,
            IReadOnlyList<ICharacterContentProvider> contentProviders)
        {
            if (card == null) return "{}";
            var w = new JsonWriter(4096);

            // 基本元数据（始终包含）
            w.Prop("id", card.ID);
            w.Prop("name", card.Name);
            w.Prop("fullName", card.FullName);
            w.Prop("defName", card.DefName);
            w.Prop("factionLabel", card.FactionLabel);
            w.Prop("gender", card.Gender);
            w.Prop("pawnType", card.PawnType);
            w.Prop("pawnRelation", card.PawnRelation);
            w.Prop("isDead", card.IsDead);
            w.Prop("isAwake", card.IsAwake);

            // 通过钩子收集各 section 内容
            var effectiveView = string.IsNullOrEmpty(view) ? "static" : view;
            if (contentProviders != null && contentProviders.Count > 0)
            {
                var sectionWriter = new JsonWriter(3072);
                bool hasContent = false;
                foreach (var provider in contentProviders)
                {
                    if (provider == null) continue;
                    var content = provider.GetContent(card.ID, effectiveView);
                    if (!string.IsNullOrEmpty(content))
                    {
                        sectionWriter.Prop(provider.SectionName, content);
                        hasContent = true;
                    }
                }
                if (hasContent)
                    w.PropRaw("sections", sectionWriter.Close());
            }

            w.Prop("view", effectiveView);
            SerializeExtensions(w, card);
            return w.Close();
        }

        // ================================================================
        // ObjectiveCard
        // ================================================================

        public string SerializeObjective(ObjectiveCard obj)
        {
            if (obj == null) return "{}";
            var w = new JsonWriter(256);
            w.Prop("id", obj.ID);
            w.Prop("title", obj.Title);
            w.Prop("description", obj.Description);
            w.Prop("status", obj.Status);
            w.Prop("source", obj.Source);
            w.Prop("deadline", obj.Deadline);

            if (obj.Steps != null && obj.Steps.Count > 0)
            {
                var steps = new List<string>();
                foreach (var s in obj.Steps)
                {
                    var sw = new JsonWriter(64);
                    sw.Prop("label", s.Label);
                    sw.Prop("isCompleted", s.IsCompleted);
                    steps.Add(sw.Close());
                }
                w.ArrayRaw("steps", steps);
            }

            SerializeExtensions(w, obj);
            return w.Close();
        }

        public string SerializeObjectiveList(IReadOnlyList<ObjectiveCard> objectives)
        {
            return SerializeObjectList(objectives, SerializeObjective);
        }

        // ================================================================
        // EnvironmentCard
        // ================================================================

        public string SerializeEnvironment(EnvironmentCard env)
        {
            if (env == null) return "{}";
            var w = new JsonWriter(512);
            w.Prop("type", env.Type);
            w.Prop("temperature", env.Temperature, "F1");
            w.Prop("lightLevel", env.LightLevel, "F2");
            w.Prop("thermalComfort", env.ThermalComfort);
            w.Prop("lightLabel", env.LightLabel);

            if (!string.IsNullOrEmpty(env.Weather.Label))
            {
                var ww = new JsonWriter(128);
                var we = env.Weather;
                ww.Prop("label", we.Label);
                ww.Prop("description", we.Description);
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

            SerializeExtensions(w, env);
            return w.Close();
        }

        // ================================================================
        // InteractionRecord
        // ================================================================

        public string SerializeInteraction(InteractionRecord rec)
        {
            var w = new JsonWriter(128);
            w.Prop("tick", rec.Tick);
            w.Prop("initiatorId", rec.InitiatorID);
            w.Prop("recipientId", rec.RecipientID);
            w.Prop("interactionDef", rec.InteractionDef);
            w.Prop("outcome", rec.Outcome);
            return w.Close();
        }

        public string SerializeInteractionList(IReadOnlyList<InteractionRecord> records)
        {
            return SerializeObjectList(records, SerializeInteraction);
        }

        // ================================================================
        // ColonistSummary (轻量列表，供 find_characters 用)
        // ================================================================

        public string SerializeColonistSummaryList(IReadOnlyList<ColonistSummary> colonists)
        {
            return SerializeObjectList(colonists, SerializeColonistSummary);
        }

        // ================================================================
        // 内部辅助
        // ================================================================

        private static List<string> DeserializeTagList(string json)
        {
            return JsonParser.ParseStringArray(json);
        }

        private static List<EventActorRef> DeserializeActors(string json)
        {
            var result = new List<EventActorRef>();
            if (string.IsNullOrEmpty(json) || json == "[]") return result;

            var dicts = JsonParser.ParseObjectArray(json);
            foreach (var dict in dicts)
            {
                result.Add(new EventActorRef
                {
                    ID = dict.TryGetValue("id", out var v) ? v : "?",
                    Name = dict.TryGetValue("name", out v) ? v : "?",
                    Role = dict.TryGetValue("role", out v) ? v : "Bystander",
                    RefType = dict.TryGetValue("refType", out v) ? v : "Pawn"
                });
            }
            return result;
        }

        private static Dictionary<string, string> DeserializePayload(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return new Dictionary<string, string>();
            return JsonParser.ParseDict(json);
        }

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

        /// <summary>
        /// 序列化卡片上的扩展字段。若卡片实现了 IExtensibleCard，
        /// 则将其所有非空扩展字段平铺到顶层 JSON 中。
        /// </summary>
        private static void SerializeExtensions(JsonWriter w, IExtensibleCard card)
        {
            if (card?.ExtensionFields == null || card.ExtensionFields.Count == 0) return;
            foreach (var kv in card.ExtensionFields)
                w.Prop(kv.Key, kv.Value);
        }
    }
}
