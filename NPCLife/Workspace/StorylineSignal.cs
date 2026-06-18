namespace NPCLife.Workspace
{
    /// <summary>
    /// 编剧上报的剧情线推进状态信号类型。
    /// 编剧通过此信号向导演报告推进情况，导演基于信号做结构决策。
    /// 编剧只上报状态，不透露具体叙事内容。
    /// </summary>
    public enum SignalType
    {
        /// <summary>剧情推进正常，无需导演干预。</summary>
        Progressing,

        /// <summary>剧情线已走到自然终点，建议导演 Complete。</summary>
        StorylineComplete,

        /// <summary>剧情出现分叉点，建议导演开分支探索不同方向。</summary>
        NeedsBranch,

        /// <summary>剧情陷入僵局，编剧无法继续，建议 Abandon 或合并到其他线。</summary>
        Stuck,

        /// <summary>两条剧情线在叙事层面已交汇，建议导演合并。</summary>
        ReadyForMerge
    }

    /// <summary>
    /// 编剧上报给导演的结构化推进信号。
    /// 不包含具体叙事内容，仅包含状态类型和简短说明。
    /// 导演看到此信号后做结构决策（分支/合并/关闭）。
    /// </summary>
    public struct StorylineSignal
    {
        /// <summary>信号类型。</summary>
        public SignalType Type;

        /// <summary>上报时刻 (格式化时间字符串，框架原样透传)。</summary>
        public string ReportedAt;

        /// <summary>编剧给导演的简短说明（≤200字，结构化摘要而非剧情细节）。</summary>
        public string Note;

        /// <summary>ReadyForMerge 时：建议合并到的目标工作空间 ID。其他类型为 null。</summary>
        public string SuggestedTargetId;
    }
}
