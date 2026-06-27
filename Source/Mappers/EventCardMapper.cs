using NPCLife.Cards;
using NPCLife.Workspace;
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
        public static IGameEvent FromIncident(IncidentDef def, IncidentParms parms, float importance)
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
                Tags = BuildIncidentTags(def),
                Tick = tick,
                Importance = importance,
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
        public static IGameEvent FromDeath(Pawn victim, DamageInfo? dinfo, float importance)
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

            var deathTags = new List<string> { "PawnDeath", "Health" };
            if (dinfo.HasValue && dinfo.Value.Instigator is Pawn)
                deathTags.Add("Combat");

            return new EventCardImpl
            {
                EventID = $"death_{victim?.ThingID}_{tick}",
                DefName = "PawnDeath",
                Tags = deathTags,
                Tick = tick,
                Importance = importance,
                Actors = actors,
                MapHint = victim?.Map != null ? $"Map:{victim.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从精神崩溃创建事件卡。
        /// </summary>
        public static IGameEvent FromMentalBreak(Pawn pawn, string reason, MentalBreakDef breakDef, float importance)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            if (pawn != null)
                actors.Add(EventActorRef.Pawn(
                    pawn.ThingID ?? "?",
                    pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                    "Victim"));

            var payload = new Dictionary<string, string>();
            if (breakDef != null)
            {
                payload["mentalBreakDef"] = breakDef.defName;
                payload["mentalBreakLabel"] = breakDef.label ?? "";
                payload["intensity"] = breakDef.intensity.ToStringSafe();
                payload["reason"] = reason ?? "";
                if (breakDef.mentalState != null)
                {
                    payload["mentalStateDef"] = breakDef.mentalState.defName;
                    payload["mentalStateLabel"] = breakDef.mentalState.label ?? "";
                }
            }

            return new EventCardImpl
            {
                EventID = $"mental_{pawn?.ThingID}_{tick}",
                DefName = breakDef?.defName ?? "MentalBreak",
                Tags = new List<string> { "MentalBreak", "Health" },
                Tick = tick,
                Importance = importance,
                Actors = actors,
                MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从社交互动创建事件卡。
        /// </summary>
        public static IGameEvent FromSocialInteraction(Pawn initiator, Pawn recipient, InteractionDef intDef, float importance)
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
                Tags = new List<string> { "SocialInteraction", "Social" },
                Tick = tick,
                Importance = importance,
                Actors = actors,
                MapHint = initiator?.Map != null ? $"Map:{initiator.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从 Quest 状态变化创建事件卡。
        /// </summary>
        public static IGameEvent FromQuest(Quest quest, string stateChange, float importance)
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
                Tags = new List<string> { "Quest", stateChange ?? "Unknown" },
                Tick = tick,
                Importance = importance,
                Actors = new List<EventActorRef>(),
                MapHint = "",
                Payload = payload
            };
        }

        /// <summary>
        /// 从派系变更创建事件卡。
        /// </summary>
        public static IGameEvent FromFactionChange(Pawn pawn, Faction newFaction, float importance)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var actors = new List<EventActorRef>();
            string changeType;

            var playerFaction = Faction.OfPlayer;
            if (newFaction == playerFaction)
                changeType = "Joined";
            else if (pawn?.Faction == playerFaction)
                changeType = "Left";
            else
                changeType = "FactionSwitch";

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
                Tags = new List<string> { "FactionChange", "Social", changeType },
                Tick = tick,
                Importance = importance,
                Actors = actors,
                MapHint = pawn?.Map != null ? $"Map:{pawn.Map.uniqueID}" : "",
                Payload = payload
            };
        }

        // ================================================================
        // 统一信封映射（替代逐个事件 Hook，覆盖所有 RimWorld 信封事件）
        // ================================================================

        /// <summary>
        /// 从 RimWorld 信封创建事件卡。
        /// Letter 自带叙事文案（label / text），天然适配编剧 agent 消费。
        /// </summary>
        public static IGameEvent FromLetter(LetterDef letterDef, TaggedString label, TaggedString text,
                                              LookTargets lookTargets, Faction relatedFaction)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;

            var tags = BuildLetterTags(letterDef);
            float importance = MapLetterImportance(letterDef);
            var actors = ExtractActorsFromLookTargets(lookTargets, relatedFaction);
            string mapHint = ExtractMapHintFromLookTargets(lookTargets);

            var payload = new Dictionary<string, string>
            {
                ["letterLabel"] = label.ToString() ?? "",
                ["letterText"] = text.ToString() ?? "",
                ["letterDef"] = letterDef?.defName ?? "Unknown",
                ["letterColor"] = GetLetterColorLabel(letterDef)
            };

            if (relatedFaction != null)
                payload["relatedFaction"] = relatedFaction.Name ?? relatedFaction.def?.label ?? "Unknown";

            return new EventCardImpl
            {
                EventID = $"letter_{letterDef?.defName ?? "unknown"}_{tick}",
                DefName = letterDef?.defName ?? "Letter",
                Tags = tags,
                Tick = tick,
                TimeLabel = RimLife.Infrastructure.RimLifeCore.TimeProvider?.Invoke() ?? "",
                Importance = importance,
                Actors = actors,
                MapHint = mapHint,
                Payload = payload
            };
        }

        // ================================================================
        // 定时器脉冲事件
        // ================================================================

        /// <summary>
        /// 创建定时器脉冲合成事件。由 RimWorldAgentDriver 定时器驱动，
        /// 向导演/即兴编剧工作空间注入一条无外部依赖的系统事件。
        /// 重要度固定为 0.5f，避免单次脉冲就触发阈值（需配合 Count 累积）。
        /// </summary>
        /// <param name="role">触发此脉冲的角色（Director 或 Improviser）。</param>
        /// <param name="tick">当前游戏 tick。</param>
        public static IGameEvent CreateTimerPulse(NPCLife.Workspace.WorkspaceRole role, int tick)
        {
            return new EventCardImpl
            {
                EventID = $"timer_pulse_{role.ToString().ToLowerInvariant()}_{tick}",
                DefName = "TimerPulse",
                Tags = new List<string> { "TimerPulse", "System" },
                Tick = tick,
                TimeLabel = RimLife.Infrastructure.RimLifeCore.TimeProvider?.Invoke() ?? "",
                Importance = 0.5f,
                Actors = new List<EventActorRef>(),
                MapHint = "",
                Payload = new Dictionary<string, string>
                {
                    ["sourceRole"] = role.ToString(),
                    ["pulseTick"] = tick.ToString()
                }
            };
        }

        // ================================================================
        // 私有实现
        // ================================================================

        private class EventCardImpl : IGameEvent
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

        private static List<string> BuildIncidentTags(IncidentDef def)
        {
            var tags = new List<string> { "Incident" };
            if (def == null)
            {
                tags.Add("Unknown");
                return tags;
            }

            string name = def.defName ?? "";

            // 从事件 defName 推导叙事标签，供 LLM 按主题分流事件。
            if (name.StartsWith("Raid") || name.Contains("Raid") || name.Contains("Attack"))
                tags.Add("Combat");
            else if (name.Contains("Trade") || name.Contains("Trader"))
                tags.Add("Economy");
            else if (name.Contains("Quest") || name.Contains("GiveQuest"))
                tags.Add("Quest");
            else if (name.Contains("Weather") || name.Contains("Eclipse") || name.Contains("Toxic")
                || name.Contains("Volcanic") || name.Contains("SolarFlare"))
                tags.Add("Nature");
            else if (name.Contains("Wanderer") || name.Contains("Refugee") || name.Contains("Join"))
                tags.Add("Social");
            else if (def.category != null)
            {
                string cat = def.category.defName ?? "";
                if (cat == "ThreatBig" || cat == "ThreatSmall")
                    tags.Add("Combat");
                else if (cat == "Misc")
                    tags.Add("Nature");
                else
                    tags.Add(cat);
            }
            else
            {
                tags.Add("Unknown");
            }

            return tags;
        }

        // ================================================================
        // 信封标签/重要度映射
        // ================================================================

        /// <summary>
        /// 从 LetterDef.defName 推导语义标签列表。
        /// 首标签为原始 defName，后续为领域标签。
        /// </summary>
        private static List<string> BuildLetterTags(LetterDef letterDef)
        {
            var tags = new List<string>();

            string defName = letterDef?.defName ?? "Unknown";
            tags.Add(defName);

            // 从 defName 推导领域标签
            if (defName == "ThreatBig" || defName == "ThreatSmall")
            {
                tags.Add("Combat");
                tags.Add("Raid");
            }
            else if (defName == "Death")
            {
                tags.Add("Death");
                tags.Add("Health");
            }
            else if (defName == "PositiveEvent" || defName == "Good")
            {
                tags.Add("PositiveEvent");
            }
            else if (defName == "NegativeEvent" || defName == "Bad" || defName == "BadUrgent")
            {
                tags.Add("NegativeEvent");
            }
            else if (defName == "NeutralEvent")
            {
                tags.Add("NeutralEvent");
            }
            else
            {
                // 未知 defName 的回退推断：从名称中检测已知领域关键词。
                string lower = defName.ToLowerInvariant();
                if (lower.Contains("threat") || lower.Contains("raid") || lower.Contains("attack"))
                    tags.Add("Combat");
                else if (lower.Contains("death"))
                    tags.Add("Death");
                else if (lower.Contains("positive") || lower.Contains("good"))
                    tags.Add("PositiveEvent");
                else if (lower.Contains("negative") || lower.Contains("bad"))
                    tags.Add("NegativeEvent");
                else
                    tags.Add("NeutralEvent");
            }

            return tags;
        }

        /// <summary>
        /// 从 LetterDef 查表返回固定重要度值。
        /// 重要度对齐 RimWorld 信封颜色等级：红(5) > 橙红(3) > 蓝(2) > 白(1)。
        /// </summary>
        private static float MapLetterImportance(LetterDef letterDef)
        {
            if (letterDef == null) return 1f;

            string defName = letterDef.defName ?? "";

            // 红色信封 (5) — 高危/紧急：袭击、死亡、紧急警告
            if (defName == "ThreatBig" || defName == "Death" || defName == "BadUrgent")
                return 5f;
            // 橙红色信封 (3) — 负面/小威胁：小型袭击、负面事件
            if (defName == "ThreatSmall" || defName == "NegativeEvent" || defName == "Bad")
                return 3f;
            // 蓝色信封 (2) — 正面事件：好事、积极变化
            if (defName == "PositiveEvent" || defName == "Good")
                return 2f;
            // 白色信封 (1) — 中性/信息性信件
            return 1f;
        }

        /// <summary>
        /// 从 LetterDef 返回颜色等级标签，供 Agent 感知事件类型。
        /// 对齐 RimWorld 信封 UI 颜色：red / orangeRed / blue / white。
        /// </summary>
        private static string GetLetterColorLabel(LetterDef letterDef)
        {
            if (letterDef == null) return "white";

            string defName = letterDef.defName ?? "";

            if (defName == "ThreatBig" || defName == "Death" || defName == "BadUrgent")
                return "red";
            if (defName == "ThreatSmall" || defName == "NegativeEvent" || defName == "Bad")
                return "orangeRed";
            if (defName == "PositiveEvent" || defName == "Good")
                return "blue";
            return "white";
        }

        /// <summary>
        /// 从 LookTargets 和 relatedFaction 提取事件 Actor 列表。
        /// </summary>
        private static List<EventActorRef> ExtractActorsFromLookTargets(LookTargets lookTargets, Faction relatedFaction)
        {
            var actors = new List<EventActorRef>();

            // 从 LookTargets 提取 Pawn
            if (lookTargets != null && lookTargets.IsValid)
            {
                if (lookTargets.PrimaryTarget.HasThing)
                {
                    var target = lookTargets.PrimaryTarget;
                    if (target.Thing is Pawn pawn)
                    {
                        actors.Add(EventActorRef.Pawn(
                            pawn.ThingID ?? "?",
                            pawn.Name?.ToStringShort ?? pawn.LabelShortCap ?? "?",
                            "Target"));
                    }
                    else if (target.Thing != null)
                    {
                        actors.Add(new EventActorRef
                        {
                            ID = target.Thing.ThingID ?? "?",
                            Name = target.Thing.LabelShortCap ?? "?",
                            Role = "Target",
                            RefType = "Thing"
                        });
                    }
                }
            }

            // 从 relatedFaction 添加派系
            if (relatedFaction != null)
            {
                actors.Add(EventActorRef.Faction(
                    relatedFaction.Name ?? relatedFaction.def?.label ?? "Unknown",
                    "Initiator"));
            }

            return actors;
        }

        /// <summary>
        /// 从 LookTargets 推导位置提示。
        /// </summary>
        private static string ExtractMapHintFromLookTargets(LookTargets lookTargets)
        {
            if (lookTargets == null || !lookTargets.IsValid)
                return "";

            try
            {
                var target = lookTargets.PrimaryTarget;
                if (target.HasThing && target.Thing?.Map != null)
                    return $"Map:{target.Thing.Map.uniqueID}";
            }
            catch { }

            return "";
        }
    }
}
