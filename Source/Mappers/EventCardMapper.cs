using RimLife.Cards;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld 游戏对象创建 IGameEvent 实例。
    /// 承担全部 RimWorld 耦合，上层只需调用此 Mapper 的静态方法。
    /// </summary>
    public static class EventCardMapper
    {
        /// <summary>
        /// 从 Incident 创建事件卡。
        /// </summary>
        public static IGameEvent FromIncident(IncidentDef def, IncidentParms parms)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            float threat = parms?.points ?? 0f;

            var actors = new List<EventActorRef>();
            if (parms?.faction != null)
                actors.Add(EventActorRef.Faction(parms.faction.Name ?? parms.faction.def?.label ?? "Unknown", "Initiator"));

            var payload = new Dictionary<string, string>();
            if (parms?.raidStrategy != null)
                payload["raidStrategy"] = parms.raidStrategy.defName;
            if (parms?.raidArrivalMode != null)
                payload["arrivalMode"] = parms.raidArrivalMode.defName;
            payload["threatPoints"] = threat.ToString("F0");

            return new EventCardImpl
            {
                EventID = $"incident_{def?.defName}_{tick}",
                DefName = def?.defName ?? "Unknown",
                Category = CategorizeIncident(def),
                Tick = tick,
                Severity = threat > 2000f ? "Extreme" : threat > 800f ? "Major" : "Minor",
                Actors = actors,
                MapHint = parms?.spawnCenter.IsValid ?? false
                    ? $"Map position ({parms.spawnCenter.x},{parms.spawnCenter.z})"
                    : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从 Pawn 死亡创建事件卡。
        /// </summary>
        public static IGameEvent FromDeath(Pawn victim, DamageInfo? dinfo)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            if (victim != null)
                actors.Add(EventActorRef.Pawn(
                    victim.ThingID ?? "?",
                    victim.Name?.ToStringShort ?? victim.LabelShortCap ?? "?",
                    "Victim"));

            var payload = new Dictionary<string, string>();
            if (dinfo.HasValue)
            {
                payload["damageType"] = dinfo.Value.Def?.defName ?? "Unknown";
                payload["damageAmount"] = dinfo.Value.Amount.ToString("F0");
                if (dinfo.Value.Instigator is Pawn instigator)
                {
                    actors.Add(EventActorRef.Pawn(
                        instigator.ThingID ?? "?",
                        instigator.Name?.ToStringShort ?? instigator.LabelShortCap ?? "?",
                        "Initiator"));
                }
            }

            return new EventCardImpl
            {
                EventID = $"death_{victim?.ThingID}_{tick}",
                DefName = "PawnDeath",
                Category = EventCategory.Health,
                Tick = tick,
                Severity = "Major",
                Actors = actors,
                MapHint = victim?.Map != null ? $"Map:{victim.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从精神崩溃创建事件卡。
        /// </summary>
        public static IGameEvent FromMentalBreak(Pawn pawn, MentalState mentalState)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            if (pawn != null)
                actors.Add(EventActorRef.Pawn(
                    pawn.ThingID ?? "?",
                    pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                    "Victim"));

            var payload = new Dictionary<string, string>();
            if (mentalState?.def != null)
            {
                payload["mentalStateDef"] = mentalState.def.defName;
                payload["mentalStateLabel"] = mentalState.def.label ?? "";
            }

            return new EventCardImpl
            {
                EventID = $"mental_{pawn?.ThingID}_{tick}",
                DefName = mentalState?.def?.defName ?? "MentalBreak",
                Category = EventCategory.Health,
                Tick = tick,
                Severity = "Major",
                Actors = actors,
                MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从社交互动创建事件卡。
        /// </summary>
        public static IGameEvent FromSocialInteraction(Pawn initiator, Pawn recipient, InteractionDef intDef)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            if (initiator != null)
                actors.Add(EventActorRef.Pawn(
                    initiator.ThingID ?? "?",
                    initiator.Name?.ToStringShort ?? "?",
                    "Initiator"));
            if (recipient != null)
                actors.Add(EventActorRef.Pawn(
                    recipient.ThingID ?? "?",
                    recipient.Name?.ToStringShort ?? "?",
                    "Target"));

            var payload = new Dictionary<string, string>();
            if (intDef != null)
                payload["interactionLabel"] = intDef.label ?? intDef.defName;

            return new EventCardImpl
            {
                EventID = $"social_{initiator?.ThingID}_{recipient?.ThingID}_{tick}",
                DefName = intDef?.defName ?? "SocialInteraction",
                Category = EventCategory.Social,
                Tick = tick,
                Severity = "Minor",
                Actors = actors,
                MapHint = initiator?.Map != null ? $"Map:{initiator.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从 Quest 状态变化创建事件卡。
        /// </summary>
        public static IGameEvent FromQuest(Quest quest, string stateChange)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var payload = new Dictionary<string, string>
            {
                ["questID"] = quest?.id.ToString() ?? "?",
                ["questName"] = quest?.name ?? "Unknown",
                ["stateChange"] = stateChange ?? "Unknown",
                ["questState"] = quest?.State.ToString() ?? "Unknown"
            };

            return new EventCardImpl
            {
                EventID = $"quest_{quest?.id}_{tick}",
                DefName = "Quest",
                Category = EventCategory.Quest,
                Tick = tick,
                Severity = "Major",
                Actors = new List<EventActorRef>(),
                MapHint = "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从派系变更创建事件卡。
        /// </summary>
        public static IGameEvent FromFactionChange(Pawn pawn, Faction newFaction)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            string severity;
            string changeType;

            var playerFaction = Faction.OfPlayer;
            if (newFaction == playerFaction)
            {
                severity = "Major";
                changeType = "Joined";
            }
            else if (pawn?.Faction == playerFaction)
            {
                severity = "Major";
                changeType = "Left";
            }
            else
            {
                severity = "Minor";
                changeType = "FactionSwitch";
            }

            string role;
            var oldFaction = pawn?.Faction;
            if (newFaction == playerFaction) role = "Initiator";
            else if (oldFaction == playerFaction) role = "Victim";
            else role = "Bystander";

            if (pawn != null)
                actors.Add(EventActorRef.Pawn(
                    pawn.ThingID ?? "?",
                    pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                    role));

            var payload = new Dictionary<string, string>
            {
                ["pawnId"] = pawn?.ThingID ?? "?",
                ["pawnName"] = pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? "?",
                ["newFaction"] = newFaction?.Name ?? newFaction?.def?.label ?? "None",
                ["newFactionDef"] = newFaction?.def?.defName ?? "None",
                ["changeType"] = changeType
            };

            return new EventCardImpl
            {
                EventID = $"factionchange_{pawn?.ThingID}_{tick}",
                DefName = "FactionChange",
                Category = EventCategory.Social,
                Tick = tick,
                Severity = severity,
                Actors = actors,
                MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        // ================================================================
        // 私有实现
        // ================================================================

        private class EventCardImpl : IGameEvent
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

        private static EventCategory CategorizeIncident(IncidentDef def)
        {
            if (def == null) return EventCategory.Anomaly;

            string name = def.defName ?? "";
            if (name.StartsWith("Raid") || name.Contains("Raid") || name.Contains("Attack"))
                return EventCategory.Combat;
            if (name.Contains("Trade") || name.Contains("Trader"))
                return EventCategory.Economy;
            if (name.Contains("Quest") || name.Contains("GiveQuest"))
                return EventCategory.Quest;
            if (name.Contains("Weather") || name.Contains("Eclipse") || name.Contains("Toxic")
                || name.Contains("Volcanic") || name.Contains("SolarFlare"))
                return EventCategory.Nature;
            if (name.Contains("Wanderer") || name.Contains("Refugee") || name.Contains("Join"))
                return EventCategory.Social;

            if (def.category != null)
            {
                string cat = def.category.defName ?? "";
                if (cat == "ThreatBig" || cat == "ThreatSmall") return EventCategory.Combat;
                if (cat == "Misc") return EventCategory.Nature;
            }

            return EventCategory.Anomaly;
        }
    }
}
