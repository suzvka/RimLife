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
            
        };
    }
}
