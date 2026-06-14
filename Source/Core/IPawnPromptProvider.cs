namespace RimLife.Core
{
    /// <summary>
    /// Pawn 视图层级常量。与 <see cref="IPawnPromptProvider.GetCharacterPrompt"/> 的 view 参数配合使用，
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
    /// Pawn 语义提示词提供者接口。
    /// 游戏侧实现，将 RimWorld Pawn 的各维度数据转化为自然语言文本。
    /// 框架只持有此钩子，入参 pawnId + view，出参一段文本，
    /// 不感知 section 的名字、数量、顺序。
    /// </summary>
    public interface IPawnPromptProvider
    {
        /// <summary>
        /// 根据 view 层级返回角色完整语义描述文本。
        /// </summary>
        /// <param name="pawnId">角色唯一 ID（ThingID）</param>
        /// <param name="view">数据层级，使用 <see cref="PawnView"/> 常量（Static / Dynamic / Full）</param>
        /// <returns>自然语言描述字符串，可直接嵌入 prompt 或传给 LLM</returns>
        string GetCharacterPrompt(string pawnId, string view);

        /// <summary>
        /// 仅社交关系文本（供 RelationshipQueryProvider 专用）。
        /// </summary>
        /// <param name="pawnId">角色唯一 ID（ThingID）</param>
        /// <returns>社交关系自然语言描述，无关系时返回 null</returns>
        string GetSocialPrompt(string pawnId);
    }
}
