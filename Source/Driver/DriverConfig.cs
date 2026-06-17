using System.Collections.Generic;

namespace RimLife.Driver
{
    /// <summary>
    /// Agent 驱动配置。纯 POCO，零外部依赖。
    /// 控制事件池的触发阈值、定时器脉冲、重要度权重等参数。
    ///
    /// 每个角色拥有独立的事件数与重要度阈值，按工作空间的 CreatedByRole 匹配。
    /// </summary>
    public class DriverConfig
    {
        // ---- 分角色阈值 ----

        /// <summary>导演专用事件数量阈值：pending 事件数达到此值时触发激活。</summary>
        public int DirectorCountThreshold = 5;

        /// <summary>导演专用重要度阈值：pending 事件总重要度达到此值时触发激活。</summary>
        public int DirectorImportanceThreshold = 15;

        /// <summary>临时编剧专用事件数量阈值。</summary>
        public int FreelancerCountThreshold = 5;

        /// <summary>临时编剧专用重要度阈值。</summary>
        public int FreelancerImportanceThreshold = 15;

        /// <summary>剧情编剧专用事件数量阈值。</summary>
        public int ScreenwriterCountThreshold = 5;

        /// <summary>剧情编剧专用重要度阈值。</summary>
        public int ScreenwriterImportanceThreshold = 15;

        // ---- 定时器脉冲（ticks，0 = 禁用） ----

        /// <summary>导演定时器脉冲间隔（游戏 ticks）。0 表示禁用。
        /// 每间隔触发时向导演工作空间事件池注入一个 TimerPulse 事件。</summary>
        public int DirectorTimerInterval = 0;

        /// <summary>临时编剧定时器脉冲间隔（游戏 ticks）。0 表示禁用。</summary>
        public int FreelancerTimerInterval = 0;

        // ---- 通用配置 ----

        /// <summary>重要度权重映射。Severity 字符串 → 整数值。
        /// 用于计算池中事件的总重要度。</summary>
        public Dictionary<string, int> SeverityWeights = new Dictionary<string, int>
        {
            ["Minor"] = 1,
            ["Major"] = 3,
            ["Extreme"] = 5
        };

        /// <summary>历史环形缓冲区容量。超出时裁剪最旧事件。</summary>
        public int RecentHistoryCapacity = 200;

        /// <summary>Agent 多轮工具调用最大轮数（防死循环）。</summary>
        public int MaxAgentRounds = 10;

        // ---- 查询方法 ----

        /// <summary>
        /// 获取指定严重度的数值权重。
        /// </summary>
        public int GetSeverityWeight(string severity)
        {
            if (string.IsNullOrEmpty(severity)) return 0;
            return SeverityWeights.TryGetValue(severity, out int w) ? w : 0;
        }

        /// <summary>
        /// 获取指定角色的有效事件数量阈值。
        /// 分角色阈值 &gt; 0 时使用，否则回退到全局阈值。
        /// </summary>
        public int GetEffectiveCountThreshold(Workspace.WorkspaceRole role)
        {
            switch (role)
            {
                case Workspace.WorkspaceRole.Director:
                    return DirectorCountThreshold;
                case Workspace.WorkspaceRole.Screenwriter:
                    return ScreenwriterCountThreshold;
                case Workspace.WorkspaceRole.Freelancer:
                    return FreelancerCountThreshold;
                default:
                    return DirectorCountThreshold;
            }
        }

        /// <summary>
        /// 获取指定角色的有效重要度阈值。
        /// </summary>
        public int GetEffectiveImportanceThreshold(Workspace.WorkspaceRole role)
        {
            switch (role)
            {
                case Workspace.WorkspaceRole.Director:
                    return DirectorImportanceThreshold;
                case Workspace.WorkspaceRole.Screenwriter:
                    return ScreenwriterImportanceThreshold;
                case Workspace.WorkspaceRole.Freelancer:
                    return FreelancerImportanceThreshold;
                default:
                    return DirectorImportanceThreshold;
            }
        }

        /// <summary>
        /// 获取指定角色的定时器脉冲间隔（ticks）。0 表示禁用。
        /// </summary>
        public int GetTimerInterval(Workspace.WorkspaceRole role)
        {
            switch (role)
            {
                case Workspace.WorkspaceRole.Director:
                    return DirectorTimerInterval;
                case Workspace.WorkspaceRole.Freelancer:
                    return FreelancerTimerInterval;
                default:
                    return 0; // Screenwriter 不支持定时器
            }
        }

        /// <summary>创建生产环境默认配置。</summary>
        public static DriverConfig CreateDefault() => new DriverConfig();
    }
}
