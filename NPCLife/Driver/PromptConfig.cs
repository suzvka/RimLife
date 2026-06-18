using NPCLife.Framework;
using NPCLife.Framework.Script;
using System.IO;
using System.Reflection;
using System.Text;

namespace NPCLife.Driver
{
    /// <summary>
    /// 提示词与 LLM 采样参数配置。纯 POCO，零外部依赖。
    /// 缓存即真相：所有提示词以缓存数据为准，默认值从 EmbeddedResource 文件加载。
    /// "恢复" = 将缓存字段覆盖为默认值。
    /// </summary>
    public class PromptConfig
    {
        // ================================================================
        // 默认提示词（懒加载自 EmbeddedResource）
        // ================================================================

        private static string _cachedDirectorPrompt;
        private static string _cachedScreenwriterPrompt;
        private static string _cachedFreelancerPrompt;

        /// <summary>导演 Agent 默认系统提示词。</summary>
        public static string DefaultDirectorPrompt =>
            _cachedDirectorPrompt ?? (_cachedDirectorPrompt = LoadPromptResource("NPCLife.Prompts.DirectorPrompt.txt"));

        /// <summary>编剧 Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultScreenwriterPrompt =>
            _cachedScreenwriterPrompt ?? (_cachedScreenwriterPrompt = LoadPromptResource("NPCLife.Prompts.ScreenwriterPrompt.txt"));

        /// <summary>Freelancer Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultFreelancerPrompt =>
            _cachedFreelancerPrompt ?? (_cachedFreelancerPrompt = LoadPromptResource("NPCLife.Prompts.FreelancerPrompt.txt"));

        private static string LoadPromptResource(string resourceName)
        {
            try
            {
                var assembly = typeof(PromptConfig).Assembly;
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return "";
            }
        }

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
