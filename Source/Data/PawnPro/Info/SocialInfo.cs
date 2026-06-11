using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 表示 Pawn 的社交关系网快照（独立模块，双向查询）。
    /// 注意：此数据为快照，不保证其时序一致性。
    /// </summary>
    public class SocialInfo
    {
        /// <summary>所有直接社交关系。</summary>
        public IReadOnlyList<SocialRelation> Relations { get; }

        /// <summary>殖民地对他的平均好感度。</summary>
        public float ColonyOpinionAverage { get; }

        private SocialInfo()
        {
            Relations = new List<SocialRelation>();
        }

        private SocialInfo(IReadOnlyList<SocialRelation> relations, float colonyOpinionAverage)
        {
            Relations = relations;
            ColonyOpinionAverage = colonyOpinionAverage;
        }

        /// <summary>
        /// 从 Pawn 创建社交关系快照。必须在主线程上调用。
        /// </summary>
        public static SocialInfo CreateFrom(Pawn p)
        {
            if (p?.relations == null) return new SocialInfo();

            // 直接关系
            var relations = new List<SocialRelation>();
            var directRelations = p.relations.DirectRelations;
            if (directRelations != null)
            {
                foreach (var dr in directRelations)
                {
                    if (dr?.otherPawn == null) continue;
                    try
                    {
                        var other = dr.otherPawn;
                        float opinion = p.relations.OpinionOf(other);
                        bool reciprocal = other.relations?.DirectRelations?
                            .Any(r => r.otherPawn == p && r.def == dr.def) ?? false;

                        relations.Add(new SocialRelation
                        {
                            OtherID = other.ThingID ?? "?",
                            OtherName = other.Name?.ToStringShort ?? other.LabelShortCap ?? "?",
                            RelationType = dr.def?.defName ?? "Unknown",
                            Opinion = opinion,
                            OpinionTier = SemanticLabels.MapOpinionTier(opinion),
                            IsReciprocal = reciprocal
                        });
                    }
                    catch { }
                }
            }

            // 殖民地对他的平均好感
            float colonyAvg = CalculateColonyOpinionAverage(p);

            return new SocialInfo(relations, colonyAvg);
        }

        /// <summary>
        /// 异步创建社交关系快照。
        /// </summary>
        public static Task<SocialInfo> CreateFromAsync(Pawn p)
        {
            if (p == null) return Task.FromResult(new SocialInfo());
            return MainThreadDispatcher.EnqueueAsync(() => CreateFrom(p));
        }

        /// <summary>
        /// 计算殖民地对指定 Pawn 的平均好感度。
        /// </summary>
        private static float CalculateColonyOpinionAverage(Pawn p)
        {
            try
            {
                var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned;
                if (colonists == null || colonists.Count == 0) return 0f;

                float sum = 0f;
                int count = 0;
                foreach (var c in colonists)
                {
                    if (c == p) continue;
                    if (c?.relations == null) continue;
                    try
                    {
                        sum += c.relations.OpinionOf(p);
                        count++;
                    }
                    catch { }
                }

                return count > 0 ? sum / count : 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }

    // --- 配套数据结构 ---

    /// <summary>
    /// 单条社交关系快照。
    /// </summary>
    public struct SocialRelation
    {
        /// <summary>对方的 ThingID。</summary>
        public string OtherID;

        /// <summary>对方姓名。</summary>
        public string OtherName;

        /// <summary>关系类型 DefName (例如 "Spouse", "Lover", "Friend")。</summary>
        public string RelationType;

        /// <summary>我对他的好感度数值。</summary>
        public float Opinion;

        /// <summary>好感度语义标签。</summary>
        public string OpinionTier;

        /// <summary>是否为双向关系。</summary>
        public bool IsReciprocal;
    }

    /// <summary>
    /// 单条互动记录（保留结构定义供后续扩展）。
    /// </summary>
    public struct InteractionRecord
    {
        /// <summary>发生时刻 (tick)。</summary>
        public int Tick;

        /// <summary>互动 DefName (例如 "DeepTalk", "Insulted")。</summary>
        public string InteractionDef;

        /// <summary>对方的 ThingID。</summary>
        public string OtherID;

        /// <summary>结果: "Positive"/"Negative"/"Neutral"。</summary>
        public string Outcome;
    }
}
