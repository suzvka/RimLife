using NPCLife.Framework.Script;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 台词消费回调接口。游戏侧实现此接口以接收框架推送的台词。
    /// 框架在 MainThreadDispatcher 上调用，保证主线程安全。
    /// </summary>
    public interface IScriptConsumer
    {
        /// <summary>
        /// 框架推送一批解析完成的台词行。
        /// 游戏侧负责基于 Tick 的时间轴调度显示。
        /// </summary>
        /// <param name="workspaceId">来源工作空间 ID。</param>
        /// <param name="roundSeq">本轮在工作空间中的序号。</param>
        /// <param name="lines">已解析并填充 SpeakerName 的台词行列表。</param>
        void OnScriptLinesReady(string workspaceId, int roundSeq,
            IReadOnlyList<ScriptLine> lines);
    }
}
