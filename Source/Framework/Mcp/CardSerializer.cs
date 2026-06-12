using RimLife.Cards;
using System.Collections.Generic;
using System.Text;

namespace RimLife.Framework.Mcp
{
    /// <summary>
    /// Card DTO → JSON 序列化器。纯静态，零 RimWorld 依赖。
    /// 供 DirectorMcpTools 调用，将查询结果转为 LLM 可消费的 JSON。
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
        // CharacterCard (可选子模块)
        // ================================================================

        /// <summary>
        /// 序列化 CharacterCard。sections 为空或 null 时包含全部子模块。
        /// sections 格式："health,mood,skills"（逗号分隔）。
        /// </summary>
        public static string SerializeCharacterCard(CharacterCard card, string sections)
        {
            if (card == null) return "{}";
            var w = new JsonWriter(2048);

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

            // 解析 sections
            var requested = ParseSections(sections);

            bool health = requested.Count == 0 || requested.Contains("health");
            bool mood = requested.Count == 0 || requested.Contains("mood");
            bool skills = requested.Count == 0 || requested.Contains("skills");
            bool needs = requested.Count == 0 || requested.Contains("needs");
            bool activity = requested.Count == 0 || requested.Contains("activity");
            bool gear = requested.Count == 0 || requested.Contains("gear");
            bool backstory = requested.Count == 0 || requested.Contains("backstory");
            bool social = requested.Count == 0 || requested.Contains("social");
            bool perspective = requested.Count == 0 || requested.Contains("perspective");
            bool psychology = requested.Count == 0 || requested.Contains("psychology");

            var activeSections = new List<string>();
            if (health && card.Health != null) { w.PropRaw("health", SerializeHealth(card.Health)); activeSections.Add("health"); }
            if (mood && card.Mood != null) { w.PropRaw("mood", SerializeMood(card.Mood)); activeSections.Add("mood"); }
            if (skills && card.Skills != null) { w.PropRaw("skills", SerializeSkills(card.Skills)); activeSections.Add("skills"); }
            if (needs && card.Needs != null) { w.PropRaw("needs", SerializeNeeds(card.Needs)); activeSections.Add("needs"); }
            if (activity && card.Activity != null) { w.PropRaw("activity", SerializeActivity(card.Activity)); activeSections.Add("activity"); }
            if (gear && card.Gear != null) { w.PropRaw("gear", SerializeGear(card.Gear)); activeSections.Add("gear"); }
            if (backstory && card.Backstory != null) { w.PropRaw("backstory", SerializeBackstory(card.Backstory)); activeSections.Add("backstory"); }
            if (social && card.Social != null) { w.PropRaw("social", SerializeSocial(card.Social)); activeSections.Add("social"); }
            if (perspective && card.Perspective != null) { w.PropRaw("perspective", SerializePerspective(card.Perspective)); activeSections.Add("perspective"); }
            if (psychology && card.Psychology != null) { w.PropRaw("psychology", SerializePsychology(card.Psychology)); activeSections.Add("psychology"); }

            // Sections 数组（始终写入，即使为空）
            var secSb = new StringBuilder("[");
            for (int i = 0; i < activeSections.Count; i++)
            {
                if (i > 0) secSb.Append(',');
                secSb.Append('"');
                secSb.Append(JsonHelper.Escape(activeSections[i]));
                secSb.Append('"');
            }
            secSb.Append(']');
            w.PropRaw("sections", secSb.ToString());
            return w.Close();
        }

        private static HashSet<string> ParseSections(string sections)
        {
            var result = new HashSet<string>();
            if (string.IsNullOrEmpty(sections)) return result;
            foreach (var s in sections.Split(new char[] { ',' }))
            {
                var trimmed = s.Trim().ToLowerInvariant();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        // --- Health ---
        private static string SerializeHealth(HealthSection h)
        {
            var w = new JsonWriter(512);
            w.Prop("summaryPain", h.SummaryPain, "F2");
            w.Prop("summaryBleedRate", h.SummaryBleedRate, "F2");
            w.Prop("painTier", h.PainTier ?? "");
            w.Prop("bleedTier", h.BleedTier ?? "");

            if (h.Capacities != null && h.Capacities.Count > 0)
            {
                var cw = new JsonWriter(256);
                foreach (var kv in h.Capacities)
                    cw.Prop(kv.Key, kv.Value, "F2");
                w.PropRaw("capacities", cw.Close());
            }

            if (h.CapacityTiers != null && h.CapacityTiers.Count > 0)
            {
                var tw = new JsonWriter(256);
                foreach (var kv in h.CapacityTiers)
                    tw.Prop(kv.Key, kv.Value ?? "");
                w.PropRaw("capacityTiers", tw.Close());
            }

            if (h.Injuries != null && h.Injuries.Count > 0)
            {
                var injuries = new List<string>();
                foreach (var i in h.Injuries)
                {
                    var iw = new JsonWriter(128);
                    iw.Prop("label", i.Label ?? "");
                    iw.Prop("part", i.Part ?? "");
                    iw.Prop("severity", i.Severity, "F2");
                    iw.Prop("isBleeding", i.IsBleeding);
                    iw.Prop("isPermanent", i.IsPermanent);
                    iw.Prop("isInfection", i.IsInfection);
                    iw.Prop("tendQuality", i.TendQuality, "F2");
                    iw.Prop("ageTicks", i.AgeTicks);
                    iw.Prop("immunity", i.Immunity, "F2");
                    iw.Prop("compDisappears", i.CompDisappears);
                    injuries.Add(iw.Close());
                }
                w.ArrayRaw("injuries", injuries);
            }

            return w.Close();
        }

        // --- Mood ---
        private static string SerializeMood(MoodSection m)
        {
            var w = new JsonWriter(512);
            w.Prop("moodLevel", m.MoodLevel, "F2");
            w.Prop("moodTier", m.MoodTier ?? "");
            if (!string.IsNullOrEmpty(m.MentalStateLabel))
                w.Prop("mentalStateLabel", m.MentalStateLabel);

            if (m.Traits != null && m.Traits.Count > 0)
            {
                var traits = new List<string>();
                foreach (var t in m.Traits)
                {
                    var tw = new JsonWriter(64);
                    tw.Prop("defName", t.DefName ?? "");
                    tw.Prop("label", t.Label ?? "");
                    tw.Prop("degree", t.Degree);
                    traits.Add(tw.Close());
                }
                w.ArrayRaw("traits", traits);
            }

            if (m.ActiveThoughts != null && m.ActiveThoughts.Count > 0)
            {
                var thoughts = new List<string>();
                foreach (var t in m.ActiveThoughts)
                {
                    var tw = new JsonWriter(64);
                    tw.Prop("label", t.Label ?? "");
                    tw.Prop("moodOffset", t.MoodOffset, "F1");
                    tw.Prop("durationRatio", t.DurationRatio, "F2");
                    thoughts.Add(tw.Close());
                }
                w.ArrayRaw("activeThoughts", thoughts);
            }

            return w.Close();
        }

        // --- Skills ---
        private static string SerializeSkills(SkillsSection s)
        {
            var w = new JsonWriter(512);
            if (s.AllSkills != null && s.AllSkills.Count > 0)
            {
                var skills = new List<string>();
                foreach (var sk in s.AllSkills)
                {
                    var sw = new JsonWriter(64);
                    sw.Prop("defName", sk.DefName ?? "");
                    sw.Prop("label", sk.Label ?? "");
                    sw.Prop("level", sk.Level);
                    sw.Prop("passion", sk.Passion ?? "");
                    sw.Prop("hasPassion", sk.HasPassion);
                    sw.Prop("totallyDisabled", sk.TotallyDisabled);
                    skills.Add(sw.Close());
                }
                w.ArrayRaw("allSkills", skills);
            }
            return w.Close();
        }

        // --- Needs ---
        private static string SerializeNeeds(NeedsSection n)
        {
            var w = new JsonWriter(512);
            if (n.AllNeeds != null && n.AllNeeds.Count > 0)
            {
                var needs = new List<string>();
                foreach (var nd in n.AllNeeds)
                {
                    var nw = new JsonWriter(64);
                    nw.Prop("defName", nd.DefName ?? "");
                    nw.Prop("label", nd.Label ?? "");
                    nw.Prop("curLevel", nd.CurLevel, "F2");
                    nw.Prop("thresholdLow", nd.ThresholdLow, "F2");
                    nw.Prop("isCritical", nd.IsCritical);
                    nw.Prop("needUrgency", nd.NeedUrgency ?? "");
                    needs.Add(nw.Close());
                }
                w.ArrayRaw("allNeeds", needs);
            }
            return w.Close();
        }

        // --- Activity ---
        private static string SerializeActivity(ActivitySection a)
        {
            var w = new JsonWriter(256);
            w.Prop("posture", a.Posture ?? "");
            if (a.Activities != null && a.Activities.Count > 0)
            {
                var acts = new List<string>();
                foreach (var ac in a.Activities)
                {
                    var aw = new JsonWriter(64);
                    aw.Prop("jobDefName", ac.JobDefName ?? "");
                    aw.Prop("jobReport", ac.JobReport ?? "");
                    acts.Add(aw.Close());
                }
                w.ArrayRaw("activities", acts);
            }
            return w.Close();
        }

        // --- Gear ---
        private static string SerializeGear(GearSection g)
        {
            var w = new JsonWriter(512);
            if (g.WornGear != null && g.WornGear.Count > 0)
                w.ArrayRaw("wornGear", SerializeGearItems(g.WornGear));
            if (g.Inventory != null && g.Inventory.Count > 0)
                w.ArrayRaw("inventory", SerializeGearItems(g.Inventory));
            return w.Close();
        }

        private static List<string> SerializeGearItems(IReadOnlyList<GearItem> items)
        {
            var result = new List<string>();
            foreach (var gi in items)
            {
                var gw = new JsonWriter(64);
                gw.Prop("name", gi.Name ?? "");
                gw.Prop("quality", gi.Quality ?? "");
                gw.Prop("durability", gi.Durability, "F2");
                gw.Prop("conditionLabel", gi.ConditionLabel ?? "");
                gw.Prop("count", gi.Count);
                result.Add(gw.Close());
            }
            return result;
        }

        // --- Backstory ---
        private static string SerializeBackstory(BackstorySection b)
        {
            var w = new JsonWriter(256);
            if (b.Childhood.HasValue)
            {
                var cw = new JsonWriter(128);
                cw.Prop("title", b.Childhood.Value.Title ?? "");
                cw.Prop("description", b.Childhood.Value.Description ?? "");
                w.PropRaw("childhood", cw.Close());
            }
            if (b.Adulthood.HasValue)
            {
                var aw = new JsonWriter(128);
                aw.Prop("title", b.Adulthood.Value.Title ?? "");
                aw.Prop("description", b.Adulthood.Value.Description ?? "");
                w.PropRaw("adulthood", aw.Close());
            }
            return w.Close();
        }

        // --- Social ---
        public static string SerializeSocial(SocialSection s)
        {
            var w = new JsonWriter(512);
            w.Prop("colonyOpinionAverage", s.ColonyOpinionAverage, "F1");
            if (s.Relations != null && s.Relations.Count > 0)
            {
                var rels = new List<string>();
                foreach (var r in s.Relations)
                {
                    var rw = new JsonWriter(64);
                    rw.Prop("otherID", r.OtherID ?? "");
                    rw.Prop("otherName", r.OtherName ?? "");
                    rw.Prop("relationType", r.RelationType ?? "");
                    rw.Prop("opinion", r.Opinion, "F0");
                    rw.Prop("opinionTier", r.OpinionTier ?? "");
                    rw.Prop("isReciprocal", r.IsReciprocal);
                    rels.Add(rw.Close());
                }
                w.ArrayRaw("relations", rels);
            }
            return w.Close();
        }

        // --- Perspective ---
        private static string SerializePerspective(PerspectiveSection p)
        {
            var w = new JsonWriter(256);
            if (p.VisiblePawnSnapshots != null && p.VisiblePawnSnapshots.Count > 0)
            {
                var snaps = new List<string>();
                foreach (var s in p.VisiblePawnSnapshots)
                {
                    var sw = new JsonWriter(64);
                    sw.Prop("id", s.ID ?? "");
                    sw.Prop("name", s.Name ?? "");
                    sw.Prop("defName", s.DefName ?? "");
                    sw.Prop("distance", s.Distance, "F1");
                    snaps.Add(sw.Close());
                }
                w.ArrayRaw("visiblePawnSnapshots", snaps);
            }
            return w.Close();
        }

        // --- Psychology ---
        private static string SerializePsychology(PsychologySection p)
        {
            var w = new JsonWriter(256);
            w.Prop("openness", p.Openness ?? "");
            w.Prop("conscientiousness", p.Conscientiousness ?? "");
            w.Prop("extraversion", p.Extraversion ?? "");
            w.Prop("agreeableness", p.Agreeableness ?? "");
            w.Prop("neuroticism", p.Neuroticism ?? "");
            w.PropRaw("baseVector", SerializeBigFiveVector(p.BaseVector));
            w.PropRaw("totalVector", SerializeBigFiveVector(p.TotalVector));
            return w.Close();
        }

        private static string SerializeBigFiveVector(BigFiveVector v)
        {
            var w = new JsonWriter(64);
            w.Prop("openness", v.Openness);
            w.Prop("conscientiousness", v.Conscientiousness);
            w.Prop("extraversion", v.Extraversion);
            w.Prop("agreeableness", v.Agreeableness);
            w.Prop("neuroticism", v.Neuroticism);
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
