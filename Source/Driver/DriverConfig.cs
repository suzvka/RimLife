using System.Collections.Generic;

namespace RimLife.Driver
{
    /// <summary>
    /// Agent 驱动配置。纯 POCO，零外部依赖。
    /// 控制事件池的触发阈值、定时器脉冲、重要度权重等参数。
    /// </summary>
    public class DriverConfig
    {
        /// <summary>事件数量阈值：pending 事件数达到此值时触发激活。</summary>
        public int CountThreshold = 5;

        /// <summary>重要度阈值：pending 事件总重要度达到此值时触发激活。</summary>
        public int ImportanceThreshold = 15;

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

        /// <summary>
        /// 获取指定严重度的数值权重。
        /// </summary>
        public int GetSeverityWeight(string severity)
        {
            if (string.IsNullOrEmpty(severity)) return 0;
            return SeverityWeights.TryGetValue(severity, out int w) ? w : 0;
        }

        /// <summary>创建生产环境默认配置。</summary>
        public static DriverConfig CreateDefault() => new DriverConfig();
    }
}
