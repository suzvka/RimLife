using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;
using RimLife.Framework;

namespace RimLife
{
    /// <summary>
    /// 表示单个任务 (Quest) 的快照。
    /// 注意：此数据为快照，不保证其时序一致性。
    /// </summary>
    public class QuestInfo
    {
        /// <summary>任务唯一标识。</summary>
        public string QuestID { get; }

        /// <summary>任务标题。</summary>
        public string Title { get; }

        /// <summary>任务描述（当前阶段的简短描述）。</summary>
        public string Description { get; }

        /// <summary>任务状态: "Active"/"Completed"/"Failed"/"Other"。</summary>
        public string Status { get; }

        /// <summary>任务各部分的进度。</summary>
        public IReadOnlyList<QuestPartInfo> Parts { get; }

        /// <summary>时间限制 (tick)，null 表示无时限。</summary>
        public int? TimeLimitTick { get; }

        private QuestInfo()
        {
            Parts = new List<QuestPartInfo>();
        }

        private QuestInfo(string questId, string title, string description, string status,
            IReadOnlyList<QuestPartInfo> parts, int? timeLimitTick)
        {
            QuestID = questId;
            Title = title;
            Description = description;
            Status = status;
            Parts = parts;
            TimeLimitTick = timeLimitTick;
        }

        /// <summary>
        /// 获取所有活跃任务的快照列表。必须在主线程上调用。
        /// </summary>
        public static IReadOnlyList<QuestInfo> GetActive()
        {
            var result = new List<QuestInfo>();
            try
            {
                var questManager = Find.QuestManager;
                if (questManager?.QuestsListForReading == null) return result;

                foreach (var quest in questManager.QuestsListForReading)
                {
                    if (quest == null) continue;
                    try
                    {
                        var info = CreateFrom(quest);
                        if (info != null)
                            result.Add(info);
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// 异步获取所有活跃任务。
        /// </summary>
        public static Task<IReadOnlyList<QuestInfo>> GetActiveAsync()
        {
            return MainThreadDispatcher.EnqueueAsync(() => GetActive());
        }

        /// <summary>
        /// 按 ID 查询单个任务。
        /// </summary>
        public static QuestInfo GetByID(int id)
        {
            try
            {
                var questManager = Find.QuestManager;
                if (questManager?.QuestsListForReading == null) return null;

                var quest = questManager.QuestsListForReading.FirstOrDefault(q => q?.id == id);
                if (quest == null) return null;
                return CreateFrom(quest);
            }
            catch
            {
                return null;
            }
        }

        private static QuestInfo CreateFrom(Quest quest)
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

            // 提取任务部分（仅记录数量和基本信息）
            var parts = new List<QuestPartInfo>();
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
                            parts.Add(new QuestPartInfo
                            {
                                PartLabel = part.ToString() ?? "Unknown",
                                IsCompleted = false // QuestPart.Completed API 不可用，后续版本实现
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 获取描述
            string description = "";
            try
            {
                var desc = quest.description;
                if (desc != null)
                {
                    description = desc.ToString();
                    if (description.Length > 200)
                        description = description.Substring(0, 200);
                }
                else
                {
                    description = quest.name ?? "";
                }
            }
            catch { }

            return new QuestInfo(
                quest.id.ToString(),
                quest.name ?? "Unnamed Quest",
                description,
                status,
                parts,
                null // 时间限制需进一步调查 API
            );
        }
    }

    /// <summary>
    /// 任务部分的进度信息。
    /// </summary>
    public struct QuestPartInfo
    {
        /// <summary>部分标签。</summary>
        public string PartLabel;

        /// <summary>是否已完成。</summary>
        public bool IsCompleted;
    }
}
