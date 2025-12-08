using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimLife
{
    public static class HealthTagBuilder
    {
        /// <summary>
        /// 将 HealthNarrative 中的信息抽象为标签集合，供模板选择使用。
        /// 这里不暴露任何 Hediff / Def 细节，只输出字符串标签。
        /// </summary>
        public static HashSet<string> BuildTags(HealthNarrative n)
        {
            var tags = new HashSet<string>();
            if (n == null) return tags;

            // 顶层域标签
            tags.Add("health");
            tags.Add("health.injury");

            // 严重度：保留具体值，同时给一个粗粒度 bucket
            string severityName = n.Severity.ToString().ToLowerInvariant();
            tags.Add("health.injury.severity." + severityName);

            int sevLevel = (int)n.Severity; // 你之前在 EstimateHealthSalience 里已经这么用过
            string bucket = sevLevel switch
            {
                <= 1 => "low",
                2 => "medium",
                _ => "high"
            };
            tags.Add("health.injury.severityBucket." + bucket);

            // 出血 / 感染 / 永久伤 / 会消失
            if (n.IsBleeding)
                tags.Add("health.bleeding");

            if (n.IsInfection)
                tags.Add("health.infection");

            if (n.IsPermanent)
                tags.Add("health.injury.permanent");

            if (n.CompDisappears)
                tags.Add("health.injury.temporary");

            // 处理情况
            if (n.Tending == TendingAdj.Untended)
            {
                tags.Add("health.untended");
            }
            else
            {
                tags.Add("health.tended");
                tags.Add("health.tended." + n.Tending.ToString().ToLowerInvariant());
            }

            // 这里你以后可以继续往里加：
            // - 植入物：health.implant
            // - 灵能：health.psylink
            // 只需要改这一处，模板和上层逻辑不动。

            return tags;
        }
    }

}
