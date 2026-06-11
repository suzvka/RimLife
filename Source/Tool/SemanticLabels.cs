using RimLife.Framework;
using RimWorld;
using System;
using Verse;

namespace RimLife
{
    /// <summary>
    /// RimWorld 适配层 —— 仅保留需要 RimWorld 类型的重载。
    /// 所有纯函数映射已迁移至 RimLife.Framework.SemanticLabels。
    /// </summary>
    public static class SemanticLabels
    {
        /// <summary>
        /// 将 Faction 关系类型映射为语义标签。
        /// </summary>
        public static string MapFactionRelation(FactionRelationKind kind)
        {
            return Framework.SemanticLabels.MapFactionRelation(kind.ToString());
        }
    }
}
