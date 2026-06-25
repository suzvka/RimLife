using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using NPCLife.Framework;

namespace RimLife
{
    /// <summary>
    /// 收集并格式化 Pawn 的心情信息。
    /// </summary>
    /// <remarks>此数据为快照，不保证时序一致性。</remarks>
    public class MoodInfo
    {
        public float MoodLevel { get; }            // 当前心情 (0-1)
        public string MoodTier { get; }              // 心情语义标签
        public string MentalStateLabel { get; }    // 精神崩溃状态 (null if normal)

        // 特质/性格列表
        public IReadOnlyList<TraitEntry> Traits { get; }

        // 当前活跃的想法 (Thoughts)
        public IReadOnlyList<ThoughtEntry> ActiveThoughts { get; }

        private MoodInfo()
        {
            Traits = new List<TraitEntry>();
            ActiveThoughts = new List<ThoughtEntry>();
        }

        private MoodInfo(float moodLevel, string moodTier, string mentalStateLabel, IReadOnlyList<TraitEntry> traits, IReadOnlyList<ThoughtEntry> activeThoughts)
        {
            MoodLevel = moodLevel;
            MoodTier = moodTier;
            MentalStateLabel = mentalStateLabel;
            Traits = traits;
            ActiveThoughts = activeThoughts;
        }

        public static MoodInfo CreateFrom(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike) return new MoodInfo();

            // 心情与精神状态
            var moodLevel = p.needs?.mood?.CurLevelPercentage ?? 0f;
            string mentalStateLabel = null;
            if (p.InMentalState)
            {
                mentalStateLabel = p.MentalState?.def?.label ?? p.MentalState?.InspectLine;
            }

            // 特质
            var traits = new List<TraitEntry>();
            var storyTraits = p.story?.traits?.allTraits;
            if (storyTraits != null)
            {
                foreach (var trait in storyTraits)
                {
                    if (trait == null) continue;
                    traits.Add(new TraitEntry
                    {
                        DefName = trait.def?.defName ?? string.Empty,
                        Label = trait.LabelCap,
                        Degree = trait.Degree
                    });
                }
            }

            // 活跃想法（包括记忆 + 情境）
            var activeThoughts = new List<ThoughtEntry>();
            var allThoughts = new List<Thought>();
            try { p.needs?.mood?.thoughts?.GetAllMoodThoughts(allThoughts); } catch { }
            foreach (var t in allThoughts)
            {
                if (t == null) continue;
                float offset = 0f;
                try { offset = t.MoodOffset(); } catch { }

                float durationRatio = 1f;
                if (t is Thought_Memory mem)
                {
                    int duration = mem.def?.DurationTicks ?? 0;
                    if (duration > 0)
                    {
                        durationRatio = 1f - (mem.age / (float)duration);
                        durationRatio = Mathf.Clamp01(durationRatio);
                    }
                }

                activeThoughts.Add(new ThoughtEntry
                {
                    Label = t.LabelCap,
                    MoodOffset = offset,
                    DurationRatio = durationRatio
                });
            }

            return new MoodInfo(moodLevel, SemanticLabels.MapMoodTier(moodLevel), mentalStateLabel, traits, activeThoughts);
        }

        public string ToPrompt()
        {
            var sb = new StringBuilder(256);
            sb.Append("心情: ");
            sb.Append(MoodTier);

            if (!string.IsNullOrEmpty(MentalStateLabel))
            {
                sb.Append(" [");
                sb.Append(MentalStateLabel);
                sb.Append(']');
            }

            if (Traits != null && Traits.Count > 0)
            {
                var traitStrs = Traits.Select(t => $"{t.Label}[{t.Degree:+0;-0}]");
                sb.Append("; 特性: ");
                sb.Append(string.Join(", ", traitStrs));
            }

            if (ActiveThoughts != null && ActiveThoughts.Count > 0)
            {
                var thoughtStrs = ActiveThoughts.Take(5).Select(t => $"{t.Label}({t.MoodOffset:+0.#;-0.#})");
                sb.Append("; 想法: ");
                sb.Append(string.Join(", ", thoughtStrs));
            }

            return sb.ToString();
        }

        public static Task<MoodInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new MoodInfo());

            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }
    }

    public struct TraitEntry
    {
        public string DefName;      // ID (例如 "Wimp")
        public string Label;        // 显示名
        public int Degree;          // 等级 (部分 Trait 有程度之分，如 Neurotic)
    }

    public struct ThoughtEntry
    {
        public string Label;        // 想法内容 (例如 "Ate without table")
        public float MoodOffset;    // 带来的心情影响 (+5, -3)
        public float DurationRatio; // 剩余时间比例 (可选，用于判断是否刚发生)
    }
}
