namespace RimLife.Core
{
    /// <summary>
    /// Pawn 语义提示词提供者接口。
    /// 游戏侧实现，将 RimWorld Pawn 的各维度数据转化为自然语言文本。
    /// 输出为纯文本字符串（非 JSON），可直接嵌入 prompt 或传给 LLM。
    /// </summary>
    public interface IPawnPromptProvider
    {
        /// <summary>
        /// 获取指定 section 的自然语言描述文本。
        /// </summary>
        /// <param name="pawn">Verse.Pawn 实例（游戏侧 cast）</param>
        /// <param name="sectionName">section 名称：health/mood/skills/needs/activity/gear/backstory/social/perspective/psychology/memory</param>
        /// <param name="includeMemoryDetails">仅 memory section 有效：是否包含完整 STM/LTM 流水</param>
        /// <returns>自然语言描述字符串，不包含则返回 null</returns>
        string GetSectionPrompt(object pawn, string sectionName, bool includeMemoryDetails = false);
    }
}
