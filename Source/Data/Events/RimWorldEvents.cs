using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimLife
{
    // ================================================================
    // RimWorld 具体事件实现
    // ================================================================

    /// <summary>
    /// 袭击 (Incident) 事件的适配。
    /// </summary>
    public class IncidentGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public IncidentGameEvent(IncidentDef def, IncidentParms parms)
        {
            EventID = $"incident_{def?.defName}_{Find.TickManager?.TicksGame ?? 0}";
            DefName = def?.defName ?? "Unknown";
            Tick = Find.TickManager?.TicksGame ?? 0;

            Category = CategorizeIncident(def);

            float threat = parms?.points ?? 0f;
            Severity = threat > 2000f ? "Extreme" : threat > 800f ? "Major" : "Minor";

            var actors = new List<EventActorRef>();
            if (parms?.faction != null)
                actors.Add(EventActorRef.Faction(parms.faction.Name ?? parms.faction.def?.label ?? "Unknown", "Initiator"));

            var payload = new Dictionary<string, string>();
            if (parms?.raidStrategy != null)
                payload["raidStrategy"] = parms.raidStrategy.defName;
            if (parms?.raidArrivalMode != null)
                payload["arrivalMode"] = parms.raidArrivalMode.defName;
            payload["threatPoints"] = threat.ToString("F0");

            Actors = actors;
            Payload = payload;

            MapHint = parms?.spawnCenter.IsValid ?? false
                ? $"Map position ({parms.spawnCenter.x},{parms.spawnCenter.z})"
                : "";
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

    /// <summary>
    /// Pawn 死亡事件。
    /// </summary>
    public class DeathGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public DeathGameEvent(Pawn victim, DamageInfo? dinfo)
        {
            Tick = Find.TickManager?.TicksGame ?? 0;
            EventID = $"death_{victim?.ThingID}_{Tick}";
            DefName = "PawnDeath";
            Category = EventCategory.Health;
            Severity = "Major";

            var actors = new List<EventActorRef>();
            if (victim != null)
            {
                actors.Add(EventActorRef.Pawn(
                    victim.ThingID ?? "?",
                    victim.Name?.ToStringShort ?? victim.LabelShortCap ?? "?",
                    "Victim"));
            }

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

            Actors = actors;
            Payload = payload;
            MapHint = victim?.Map != null ? $"Map:{victim.Map.uniqueID}" : "";
        }
    }

    /// <summary>
    /// 精神崩溃事件。
    /// </summary>
    public class MentalBreakGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public MentalBreakGameEvent(Pawn pawn, MentalState mentalState)
        {
            Tick = Find.TickManager?.TicksGame ?? 0;
            EventID = $"mental_{pawn?.ThingID}_{Tick}";
            DefName = mentalState?.def?.defName ?? "MentalBreak";
            Category = EventCategory.Health;
            Severity = "Major";

            var actors = new List<EventActorRef>();
            if (pawn != null)
            {
                actors.Add(EventActorRef.Pawn(
                    pawn.ThingID ?? "?",
                    pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                    "Victim"));
            }

            var payload = new Dictionary<string, string>();
            if (mentalState?.def != null)
            {
                payload["mentalStateDef"] = mentalState.def.defName;
                payload["mentalStateLabel"] = mentalState.def.label ?? "";
            }

            Actors = actors;
            Payload = payload;
            MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "";
        }
    }

    /// <summary>
    /// 社交互动事件。
    /// </summary>
    public class SocialInteractionGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public SocialInteractionGameEvent(Pawn initiator, Pawn recipient, InteractionDef intDef)
        {
            Tick = Find.TickManager?.TicksGame ?? 0;
            EventID = $"social_{initiator?.ThingID}_{recipient?.ThingID}_{Tick}";
            DefName = intDef?.defName ?? "SocialInteraction";
            Category = EventCategory.Social;
            Severity = "Minor";

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
            {
                payload["interactionLabel"] = intDef.label ?? intDef.defName;
            }

            Actors = actors;
            Payload = payload;
            MapHint = initiator?.Map != null ? $"Map:{initiator.Map.uniqueID}" : "";
        }
    }

    /// <summary>
    /// Quest 状态变化事件。
    /// </summary>
    public class QuestGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public QuestGameEvent(Quest quest, string stateChange)
        {
            Tick = Find.TickManager?.TicksGame ?? 0;
            EventID = $"quest_{quest?.id}_{Tick}";
            DefName = "Quest";
            Category = EventCategory.Quest;
            Severity = "Major";

            var actors = new List<EventActorRef>();
            var payload = new Dictionary<string, string>
            {
                ["questID"] = quest?.id.ToString() ?? "?",
                ["questName"] = quest?.name ?? "Unknown",
                ["stateChange"] = stateChange ?? "Unknown",
                ["questState"] = quest?.State.ToString() ?? "Unknown"
            };

            Actors = actors;
            Payload = payload;
            MapHint = "";
        }
    }

    /// <summary>
    /// Pawn 派系变更事件（殖民者加入/叛逃/被俘/释放）。
    /// </summary>
    public class FactionChangeGameEvent : IGameEvent
    {
        public string EventID { get; }
        public string DefName { get; }
        public EventCategory Category { get; }
        public int Tick { get; }
        public string Severity { get; }
        public IReadOnlyList<EventActorRef> Actors { get; }
        public string MapHint { get; }
        public IDictionary<string, string> Payload { get; }

        public FactionChangeGameEvent(Pawn pawn, Faction newFaction)
        {
            Tick = Find.TickManager?.TicksGame ?? 0;
            EventID = $"factionchange_{pawn?.ThingID}_{Tick}";
            DefName = "FactionChange";
            Category = EventCategory.Social;

            var actors = new List<EventActorRef>();
            if (pawn != null)
            {
                var oldFaction = pawn.Faction;
                var role = DetermineRole(oldFaction, newFaction);
                actors.Add(EventActorRef.Pawn(
                    pawn.ThingID ?? "?",
                    pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                    role));
            }

            var payload = new Dictionary<string, string>
            {
                ["pawnId"] = pawn?.ThingID ?? "?",
                ["pawnName"] = pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? "?",
                ["newFaction"] = newFaction?.Name ?? newFaction?.def?.label ?? "None",
                ["newFactionDef"] = newFaction?.def?.defName ?? "None"
            };

            var playerFaction = Faction.OfPlayer;
            if (newFaction == playerFaction)
            {
                Severity = "Major";
                payload["changeType"] = "Joined";
            }
            else if (pawn?.Faction == playerFaction)
            {
                Severity = "Major";
                payload["changeType"] = "Left";
            }
            else
            {
                Severity = "Minor";
                payload["changeType"] = "FactionSwitch";
            }

            Actors = actors;
            Payload = payload;
            MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "";
        }

        private static string DetermineRole(Faction oldFaction, Faction newFaction)
        {
            var player = Faction.OfPlayer;
            if (newFaction == player) return "Initiator"; // 加入玩家
            if (oldFaction == player) return "Victim";     // 离开玩家
            return "Bystander";
        }
    }
}
