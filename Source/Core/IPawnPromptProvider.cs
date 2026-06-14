namespace RimLife.Core
{
    /// <summary>
    /// Pawn 视图层级常量。与 <see cref="ICharacterContentProvider.GetContent"/> 的 view 参数配合使用，
    /// 消除拼写风险。
    /// </summary>
    public static class PawnView
    {
        /// <summary>客观属性（默认）。</summary>
        public const string Static = "static";
        /// <summary>客观属性 + 视角/记忆快照。</summary>
        public const string Dynamic = "dynamic";
        /// <summary>完整数据，含记忆流水。</summary>
        public const string Full = "full";
    }

    /// <summary>
    /// Pawn 社交关系提示词提供者接口。
    /// 游戏侧实现，将 RimWorld Pawn 的社交关系数据转化为自然语言文本。
    /// 框架只持有此钩子用于 RelationshipQueryProvider。
    /// 人物卡的维度数据已迁移至 <see cref="ICharacterContentProvider"/> 钩子模式。
    /// </summary>
    public interface IPawnPromptProvider
    {
        /// <summary>
        /// 仅社交关系文本（供 RelationshipQueryProvider 专用）。
        /// </summary>
        /// <param name="pawnId">角色唯一 ID（ThingID）</param>
        /// <returns>社交关系自然语言描述，无关系时返回 null</returns>
        string GetSocialPrompt(string pawnId);
    }
}
