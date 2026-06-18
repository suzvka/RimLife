namespace NPCLife.Core
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
    /// 角色人物卡内容提供者接口（钩子模式）。
    /// 游戏侧实现多个此接口的实例，分别提供健康、心情、技能等各维度的自然语言描述。
    /// 框架收集所有 Provider 的产出后按 SectionName 组装为结构化 JSON。
    /// </summary>
    public interface ICharacterContentProvider
    {
        /// <summary>
        /// Section 名称，作为 JSON 中的 key。如 "health"、"mood"、"skills"。
        /// </summary>
        string SectionName { get; }

        /// <summary>
        /// 获取指定角色的该 section 内容。
        /// </summary>
        /// <param name="pawnId">角色唯一 ID（ThingID）。</param>
        /// <param name="view">数据层级：static（默认）/ dynamic / full。
        /// Provider 可根据 view 决定返回内容的详细程度，返回 null 或空字符串表示该层级不需要此 section。</param>
        /// <returns>自然语言描述文本，null 或空字符串表示该 section 无内容。</returns>
        string GetContent(string pawnId, string view);
    }
}
