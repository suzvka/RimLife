using RimLife.Framework;
using RimLife.Framework.Script;

namespace RimLife.Driver
{
    /// <summary>
    /// 提示词与 LLM 采样参数配置。纯 POCO，零外部依赖。
    /// 缓存即真相：所有提示词以缓存数据为准，硬编码默认值仅作种子/恢复源。
    /// "恢复" = 将缓存字段覆盖为硬编码默认值。
    /// </summary>
    public class PromptConfig
    {
        // ================================================================
        // 硬编码默认提示词（不可变常量，用于初始化与恢复）
        // ================================================================

        /// <summary>导演 Agent 默认系统提示词。</summary>
        public const string DefaultDirectorPrompt =
@"你是 RimWorld 殖民地的剧情导演 (Director Agent)。

你的职责：
1. 审查以下积累的事件列表
2. 挑选值得发展为剧情线的事件
3. 使用 route_events 将事件路由到对应工作空间(没有合适的则新建)
4. 未被路由的事件将被丢弃

决策原则：
- 相关事件可合并到同一个工作空间（如同一场袭击中的多个角色受伤）
- 参考编剧 Agent 留言决定推送策略
- 没有显然前因后果的事件可以视作临时事件，推送给临时工 Agent
- 对已有工作空间可用 branch_workspace 创建分支、merge_workspaces 合并

事件路由：
- 每条事件都有 eventId，使用 route_events 将事件推送到对应工作空间
- 如无合适的工作空间，先 create_workspace 再用 route_events";

        /// <summary>编剧 Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public const string DefaultScreenwriterPrompt =
@"你是编剧 (Screenwriter Agent)。

你的职责：
1. 审查推送到本工作空间的事件
2. 根据需要调用角色查询、环境感知等工具获取上下文
3. 使用 push_line 工具逐句撰写台词（可一次并行调用多个 push_line）
4. 台词写完后调用 finish_round 结束本轮，填写 recap/outcome/directorNote

工作原则：
- 优先使用 push_line 逐句输出台词以降低玩家等待延迟
- 多句台词可在一个响应中并行调用多个 push_line，减少 API 往返
- 台词写完后必须调用 finish_round 收尾
- recap (前情提要) 总结本轮叙事起点，outcome 简述剧情发展结果
- directorNote 给导演留言：剧情线是否可以继续、期望接收什么类型的事件等
- 每次激活只推送 1 个轮次
- 如事件不适合本剧情线，可用 route_events 推回导演工作空间";

        /// <summary>Freelancer Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public const string DefaultFreelancerPrompt =
@"你是 RimWorld 殖民地的临时任务代理 (Freelancer Agent)。

你的职责：
1. 处理突发性、独立性的事件（日常对话、随机遭遇、环境变化等）
2. 这些事件不属于任何正在进行的剧情线，你不需要维护跨轮次的剧情上下文
3. 调用角色查询、环境感知等工具获取当前状态
4. 使用 push_line 工具逐句输出台词，写完后调用 finish_round 收尾

工作原则：
- 每次激活都是独立任务，不维护剧情延续性
- 叙事风格保持轻快、即兴、快速响应
- 每次激活只处理当前批次事件，输出 1 个轮次
- recap 只总结本次事件批次，不需要回顾历史
- 多句台词可在一个响应中并行调用多个 push_line
- 台词写完后必须调用 finish_round
- 如事件更适合某条剧情线，用 route_events 推回导演工作空间
- 你不负责汇报剧情线推进状态（那是编剧的职责）";

        // ================================================================
        // 可编辑字段
        // ================================================================

        /// <summary>导演 Agent 完整系统提示词（缓存即真相）。</summary>
        public string DirectorPrompt;

        /// <summary>编剧 Agent 完整系统提示词（缓存即真相，不含动态上下文）。</summary>
        public string ScreenwriterPrompt;

        /// <summary>Freelancer Agent 完整系统提示词（缓存即真相，不含动态上下文）。</summary>
        public string FreelancerPrompt;

        /// <summary>全局风格指令，运行时追加到所有 Agent 的 system prompt 末尾。</summary>
        public string StyleInstruction = "";

        /// <summary>LLM 采样温度（0~2）。越低越确定性，越高越有创意。</summary>
        public float Temperature = 0.7f;

        // ================================================================
        // 恢复
        // ================================================================

        /// <summary>将所有提示词字段恢复为硬编码默认值。</summary>
        public void ResetPromptsToDefaults()
        {
            DirectorPrompt = DefaultDirectorPrompt;
            ScreenwriterPrompt = DefaultScreenwriterPrompt;
            FreelancerPrompt = DefaultFreelancerPrompt;
            StyleInstruction = "";
        }

        /// <summary>将单个角色的提示词恢复为硬编码默认值。</summary>
        public void ResetPrompt(string role)
        {
            switch (role)
            {
                case "director": DirectorPrompt = DefaultDirectorPrompt; break;
                case "screenwriter": ScreenwriterPrompt = DefaultScreenwriterPrompt; break;
                case "freelancer": FreelancerPrompt = DefaultFreelancerPrompt; break;
            }
        }

        /// <summary>获取指定角色的默认提示词。</summary>
        public static string GetDefaultPrompt(string role)
        {
            switch (role)
            {
                case "director": return DefaultDirectorPrompt;
                case "screenwriter": return DefaultScreenwriterPrompt;
                case "freelancer": return DefaultFreelancerPrompt;
                default: return "";
            }
        }

        // ================================================================
        // 序列化 / 反序列化
        // ================================================================

        /// <summary>序列化为 JSON 字符串。</summary>
        public string ToJson()
        {
            var w = new JsonWriter(512);
            if (!string.IsNullOrEmpty(DirectorPrompt))
                w.Prop("directorPrompt", DirectorPrompt);
            if (!string.IsNullOrEmpty(ScreenwriterPrompt))
                w.Prop("screenwriterPrompt", ScreenwriterPrompt);
            if (!string.IsNullOrEmpty(FreelancerPrompt))
                w.Prop("freelancerPrompt", FreelancerPrompt);
            if (!string.IsNullOrEmpty(StyleInstruction))
                w.Prop("styleInstruction", StyleInstruction);
            w.Prop("temperature", Temperature, "F2");
            return w.Close();
        }

        /// <summary>从 JSON 字符串反序列化。解析失败时返回默认配置。</summary>
        public static PromptConfig FromJson(string json)
        {
            var config = CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return config;

            try
            {
                var dict = JsonParser.ParseDict(json);
                if (dict.TryGetValue("directorPrompt", out var dp)) config.DirectorPrompt = dp;
                if (dict.TryGetValue("screenwriterPrompt", out var sp)) config.ScreenwriterPrompt = sp;
                if (dict.TryGetValue("freelancerPrompt", out var fp)) config.FreelancerPrompt = fp;
                if (dict.TryGetValue("styleInstruction", out var si)) config.StyleInstruction = si;
                if (dict.TryGetValue("temperature", out var t) && float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tv))
                    config.Temperature = tv;
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return config;
        }

        /// <summary>创建默认配置（所有提示词填充为硬编码默认值）。</summary>
        public static PromptConfig CreateDefault()
        {
            var config = new PromptConfig();
            config.ResetPromptsToDefaults();
            return config;
        }
    }
}
