using System;
using NPCLife.Framework;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 运行时度量查询 MCP 工具集。提供 metrics_snapshot 工具，
    /// 供 Agent 和开发者查询工具频率、Token 消耗、知识库命中率等运行时度量。
    /// 属于 system skill，对所有 workspace 隐式可用。
    /// </summary>
    [McpSkill(McpSkillRegistry.SystemSkillId)]
    public static class MetricsMcpTools
    {
        /// <summary>
        /// 获取当前运行时度量快照，含 Token 消耗、工具调用频率、知识库命中率、
        /// Agent 循环统计和工作空间操作计数。
        /// </summary>
        [McpTool(Name = "metrics_snapshot",
                 Description = "获取当前运行时度量快照。包含 Token 消耗(按角色分桶)、" +
                               "MCP 工具调用频率和错误数、知识库词汇访问率和 L1 命中率、" +
                               "Agent 循环统计(激活次数/平均轮数/事件数)和工作空间操作计数。" +
                               "用于性能审计和成本分析。")]
        public static string Snapshot()
        {
            try
            {
                var snap = RuntimeMetrics.GetSnapshot();
                return snap.ToJson();
            }
            catch (Exception e)
            {
                return "{\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 重置所有运行时度量计数器为 0。
        /// </summary>
        [McpTool(Name = "metrics_reset",
                 Description = "重置所有运行时度量计数器为 0。用于测试或开始新的度量周期。" +
                               "不会影响已持久化的度量数据。")]
        public static string Reset()
        {
            try
            {
                RuntimeMetrics.Reset();
                return "{\"success\":true,\"message\":\"Metrics reset.\"}";
            }
            catch (Exception e)
            {
                return "{\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }
    }
}
