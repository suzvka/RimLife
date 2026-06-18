using NPCLife.Framework.Script;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 台词占位符解析接口。
    /// 将 ScriptLine 中的 SpeakerId 解析为显示名，填充 SpeakerName。
    /// 框架层定义契约，Infrastructure 层实现（查找 Pawn → 取其 Name/FullName）。
    /// </summary>
    public interface IScriptLineResolver
    {
        /// <summary>
        /// 批量解析 ScriptLine 列表中的占位符。
        /// 遍历所有行，对 SpeakerId 非空的 Dialogue 行查找显示名。
        /// </summary>
        void Resolve(IReadOnlyList<ScriptLine> lines);
    }
}
