using NPCLife.Framework;
using System.Globalization;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLife 在 NPCLife 基础身份之上追加的指令与 LLM 采样参数。
    /// 基础身份由 NPCLife 的 PromptConfig 静态成员持有，不可编辑；
    /// 本类只持久化"附加"部分，禁止覆盖基座。
    /// </summary>
    public class PromptAdditions
    {
        /// <summary>导演 Agent 附加指令。留空则不追加任何内容。</summary>
        public string DirectorAdditions = "";

        /// <summary>编剧 Agent 附加指令。留空则不追加任何内容。</summary>
        public string ScreenwriterAdditions = "";

        /// <summary>即兴编剧 Agent 附加指令。留空则不追加任何内容。</summary>
        public string ImproviserAdditions = "";

        /// <summary>全局风格指令，运行时追加到所有 Agent 的 system prompt 末尾。</summary>
        public string StyleInstruction = "";

        /// <summary>LLM 采样温度（0~2）。越低越确定性，越高越有创意。</summary>
        public float Temperature = 0.7f;

        public string ToJson()
        {
            var w = new JsonWriter(256);
            if (!string.IsNullOrEmpty(DirectorAdditions))
                w.Prop("directorAdditions", DirectorAdditions);
            if (!string.IsNullOrEmpty(ScreenwriterAdditions))
                w.Prop("screenwriterAdditions", ScreenwriterAdditions);
            if (!string.IsNullOrEmpty(ImproviserAdditions))
                w.Prop("improviserAdditions", ImproviserAdditions);
            if (!string.IsNullOrEmpty(StyleInstruction))
                w.Prop("styleInstruction", StyleInstruction);
            w.Prop("temperature", Temperature, "F2");
            return w.Close();
        }

        public static PromptAdditions FromJson(string json)
        {
            var additions = CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return additions;

            try
            {
                var dict = JsonParser.ParseDict(json);
                if (dict.TryGetValue("directorAdditions", out var da)) additions.DirectorAdditions = da;
                if (dict.TryGetValue("screenwriterAdditions", out var sa)) additions.ScreenwriterAdditions = sa;
                if (dict.TryGetValue("improviserAdditions", out var fa)) additions.ImproviserAdditions = fa;
                if (dict.TryGetValue("styleInstruction", out var si)) additions.StyleInstruction = si;
                if (dict.TryGetValue("temperature", out var t)
                    && float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
                    additions.Temperature = tv;
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return additions;
        }

        public static PromptAdditions CreateDefault() => new PromptAdditions
        {
            DirectorAdditions = "## 导演行为规范\n" +
                "1. 上下文已注入「殖民地快照」「活跃目标」「知识库已有词条」「活跃工作空间列表」。" +
                "禁止调用 get_colony_overview、get_active_objectives、get_current_time、list_known_terms——直接读上下文。\n" +
                "2. learn_term 仅用于记录导演推理出的全新概念。角色基本信息已由 get_character_card 提供，不要为每个角色创建词条。\n" +
                "3. 核心职责：分析事件→创建编剧工作空间→路由事件→结束轮次。不要替编剧查询角色卡、关系、环境——这些是编剧的工作。\n" +
                "4. 路由事件时，从上下文「活跃工作空间列表」中逐字复制 workspace ID（每行以 ID: 开头），严禁凭记忆输入。\n" +
                "5. 每轮工具调用上限：8 次。优先用 route_events 路由事件；若 source 工作空间不可用，改用 create_event。\n" +
                "6. 【并行调用】当需要创建多个编剧工作空间或创建多个独立事件时，在一次响应中并行调用多个 create_workspace / create_event 工具，减少轮次。",

            ScreenwriterAdditions = "## 编剧行为规范\n" +
                "1. 上下文已注入「殖民地快照」和「导演指定聚焦角色卡」（含 static view 完整信息）。" +
                "首轮先读上下文，再决定是否补充查询。\n" +
                "2. 【关键】首轮必须一次性并行发出所有查询（get_character_card×N + get_relationships×N + get_environment×N + list_all_pawns），禁止分批。\n" +
                "3. 查完立即写台词（push_line）。禁止在查询和台词之间插入额外查询。禁止在台词和 finish_round 之间插入额外查询。\n" +
                "4. 【核心】全部台词（push_line）必须在一轮中一次性输出完毕。台词是未知但由你创造的知识——每分一批就要重放一次完整上下文，浪费数百k token。10-20 行 push_line 一起发出。\n" +
                "5. 台词结束后立即调用 finish_round（recap + outcome + directorNote），同样在同一轮中完成。\n" +
                "6. delay 参数：叙述 2s / 动作 1.5s / 对白 0.5-1s。",
        };
    }
}
