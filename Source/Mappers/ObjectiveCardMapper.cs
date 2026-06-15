using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimLife.Cards;
using RimWorld;
using Verse;

namespace RimLife.Mappers
{
    /// <summary>
    /// 从 RimWorld Quest 系统提取数据并组装 ObjectiveCard。
    /// 当前 Source 固定为 "QuestSystem"，未来可扩展其他目标来源。
    /// </summary>
    public static class ObjectiveCardMapper
    {
        /// <summary>
        /// 获取所有活跃任务的 ObjectiveCard 列表。必须在主线程上调用。
        /// </summary>
        public static IReadOnlyList<ObjectiveCard> GetActive()
        {
            var result = new List<ObjectiveCard>();
            try
            {
                var questManager = Find.QuestManager;
                if (questManager?.QuestsListForReading == null) return result;

                foreach (var quest in questManager.QuestsListForReading)
                {
                    if (quest == null) continue;
                    try
                    {
                        var card = CreateFrom(quest);
                        if (card != null) result.Add(card);
                    }
                    catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] quest card: {e.Message}"); }
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] GetActive: {e.Message}"); }
            return result;
        }

        /// <summary>
        /// 异步获取所有活跃任务。
        /// </summary>
        public static Task<IReadOnlyList<ObjectiveCard>> GetActiveAsync()
        {
            return MainThreadDispatcher.EnqueueAsync(() => GetActive());
        }

        /// <summary>
        /// 按 ID 查询单个任务。
        /// </summary>
        public static ObjectiveCard GetByID(int id)
        {
            try
            {
                var questManager = Find.QuestManager;
                if (questManager?.QuestsListForReading == null) return null;
                var quest = questManager.QuestsListForReading.FirstOrDefault(q => q?.id == id);
                if (quest == null) return null;
                return CreateFrom(quest);
            }
            catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] GetByID({id}): {e.Message}"); return null; }
        }

        // ================================================================
        // 内部映射
        // ================================================================

        private static ObjectiveCard CreateFrom(Quest quest)
        {
            if (quest == null) return null;

            string status;
            switch (quest.State)
            {
                case QuestState.Ongoing: status = "Active"; break;
                case QuestState.EndedSuccess: status = "Completed"; break;
                case QuestState.EndedFailed: status = "Failed"; break;
                default: status = "Other"; break;
            }

            var steps = new List<ObjectiveStepEntry>();
            try
            {
                var questParts = quest.PartsListForReading;
                if (questParts != null)
                {
                    foreach (var part in questParts)
                    {
                        if (part == null) continue;
                        try
                        {
                            steps.Add(new ObjectiveStepEntry
                            {
                                Label = part.ToString() ?? "Unknown",
                                IsCompleted = false
                            });
                        }
                        catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] quest part: {e.Message}"); }
                    }
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] quest parts: {e.Message}"); }

            string description = "";
            try
            {
                var desc = quest.description;
                if (desc != null)
                {
                    description = desc.ToString();
                    if (description.Length > 200) description = description.Substring(0, 200);
                }
                else
                {
                    description = quest.name ?? "";
                }
            }
            catch (Exception e) { Log.Warning($"[RimLife.ObjectiveCardMapper] description: {e.Message}"); }

            return new ObjectiveCard
            {
                ID = quest.id.ToString(),
                Title = quest.name ?? "Unnamed Objective",
                Description = description,
                Status = status,
                Source = "QuestSystem",
                Steps = steps
            };
        }
    }
}
