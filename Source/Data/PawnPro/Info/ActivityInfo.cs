using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimLife
{
    /// <summary>
    /// 表示 Pawn 当前活动的快照。
    /// 注意：此数据是快照，其时间一致性不能得到保证。
    /// </summary>
    public class ActivityInfo
    {
        public string Posture { get; }             // 姿态
        public IReadOnlyList<ActivityEntry> Activities { get; }

        private ActivityInfo()
        {
            Activities = new List<ActivityEntry>();
        }

        private ActivityInfo(string posture, IReadOnlyList<ActivityEntry> activities)
        {
            Posture = posture;
            Activities = activities;
        }

        public static ActivityInfo CreateFrom(Pawn p)
        {
            if (p == null || p.jobs == null) return new ActivityInfo();

            // 姿态
            string posture = null;
            try { posture = p.GetPosture().ToString(); } catch { }

            // 遍历工作队列
            var activities = new List<ActivityEntry>();
            var jobQueue = p.jobs.jobQueue;
            if (jobQueue != null)
            {
                foreach (var job in jobQueue)
                {
                    if (job?.job == null) continue;
                    try
                    {
                        activities.Add(new ActivityEntry
                        {
                            JobDefName = job.job.def?.defName ?? string.Empty,
                            JobReport = job.job.GetReport(p)
                        });
                    }
                    catch { }
                }
            }

            // 添加当前工作
            var curJob = p.CurJob;
            if (curJob != null)
            {
                try
                {
                    activities.Insert(0, new ActivityEntry
                    {
                        JobDefName = curJob.def?.defName ?? string.Empty,
                        JobReport = curJob.GetReport(p)
                    });
                }
                catch { }
            }

            return new ActivityInfo(posture, activities);
        }

        public static Task<ActivityInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new ActivityInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }
    }

    public struct ActivityEntry
    {
        public string JobDefName;       // 工作ID
        public string JobReport;    // 工作描述文本 (例如 "搬运铁")
    }
}
