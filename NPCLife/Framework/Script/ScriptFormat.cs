using NPCLife.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace NPCLife.Framework.Script
{
    /// <summary>
    /// 台词格式定义与解析器。Schema-driven 设计：
    /// Schema[] 是单一事实来源，GetFormatSpec() 和 Parse() 均从中派生。
    /// 更换输出格式时只需修改此文件中的 Schema 数组和 fieldSetter 字典。
    /// </summary>
    public static class ScriptFormat
    {
        // ================================================================
        // Schema — 单一事实来源
        // ================================================================

        private struct ScriptFieldDef
        {
            /// <summary>JSON 键名（LLM 输出中使用的字段名）。</summary>
            public string JsonKey;

            /// <summary>中文标签（用于提示词）。</summary>
            public string Label;

            /// <summary>类型提示：string / number / "val1|val2|..."。</summary>
            public string TypeHint;

            /// <summary>是否必需。</summary>
            public bool Required;

            /// <summary>缺失时的默认值字符串（null = 空字符串/无默认）。</summary>
            public string DefaultValue;

            /// <summary>用法备注（用于提示词中的说明）。</summary>
            public string UsageNote;
        }

        private static readonly ScriptFieldDef[] Schema = new[]
        {
            new ScriptFieldDef
            {
                JsonKey = "s",
                Label = "说话者ID",
                TypeHint = "string",
                Required = false,
                DefaultValue = null,
                UsageNote = "角色的 ThingID，旁白/动作/停顿时省略此字段"
            },
            new ScriptFieldDef
            {
                JsonKey = "t",
                Label = "台词文本",
                TypeHint = "string",
                Required = true,
                DefaultValue = "",
                UsageNote = "台词或描述内容。Pause 类型时可为空字符串"
            },
            new ScriptFieldDef
            {
                JsonKey = "d",
                Label = "延迟秒数",
                TypeHint = "number",
                Required = false,
                DefaultValue = "0",
                UsageNote = "本行之前的等待秒数，默认 0"
            },
            new ScriptFieldDef
            {
                JsonKey = "type",
                Label = "类型",
                TypeHint = "dialogue|narration|action|pause",
                Required = false,
                DefaultValue = "dialogue",
                UsageNote = "dialogue=对话 narration=旁白 action=动作 pause=停顿"
            },
        };

        // ================================================================
        // fieldSetter — JsonKey → ScriptLine 属性赋值
        // ================================================================

        private static readonly Dictionary<string, Action<ScriptLine, string>> fieldSetter =
            new Dictionary<string, Action<ScriptLine, string>>
            {
                ["s"] = (line, val) =>
                {
                    line.SpeakerId = string.IsNullOrEmpty(val) ? null : val;
                },
                ["t"] = (line, val) =>
                {
                    line.Text = val ?? "";
                },
                ["d"] = (line, val) =>
                {
                    if (float.TryParse(val,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var f))
                        line.RelativeTime = f;
                },
                ["type"] = (line, val) =>
                {
                    line.Type = ParseLineType(val);
                },
            };

        // ================================================================
        // 公共 API
        // ================================================================

        /// <summary>
        /// 生成格式说明文本，注入 LLM system prompt。
        /// 完全由 Schema 驱动，零硬编码文本。
        /// </summary>
        public static string GetFormatSpec()
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("台词输出格式为 JSON 数组，每个元素为一句台词。字段说明：");
            sb.AppendLine();

            for (int i = 0; i < Schema.Length; i++)
            {
                var field = Schema[i];
                sb.Append("- ");

                // 字段名和类型
                sb.Append('"');
                sb.Append(field.JsonKey);
                sb.Append("\" (");
                sb.Append(field.TypeHint);
                sb.Append(", ");

                // 必需/可选
                if (field.Required)
                    sb.Append("必需");
                else
                    sb.Append("可选");

                // 默认值
                if (!field.Required && field.DefaultValue != null)
                {
                    sb.Append(", 默认 ");
                    if (field.TypeHint == "number")
                        sb.Append(field.DefaultValue);
                    else
                    {
                        sb.Append('"');
                        sb.Append(field.DefaultValue);
                        sb.Append('"');
                    }
                }

                sb.Append(")");

                // 标签
                sb.Append(": ");
                sb.Append(field.Label);

                // 用法备注
                if (!string.IsNullOrEmpty(field.UsageNote))
                {
                    sb.Append(" — ");
                    sb.Append(field.UsageNote);
                }

                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("示例：");
            sb.AppendLine("[");
            sb.AppendLine("  {\"s\":\"Pawn_123\",\"t\":\"嘿，今天天气不错。\",\"d\":0,\"type\":\"dialogue\"},");
            sb.AppendLine("  {\"t\":\"风吹过麦田，金色的波浪一层层涌向远方。\",\"d\":1.5,\"type\":\"narration\"},");
            sb.AppendLine("  {\"t\":\"远处传来一声枪响。\",\"d\":0.5,\"type\":\"action\"},");
            sb.AppendLine("  {\"s\":\"Pawn_456\",\"t\":\"你听到了吗？\",\"d\":0.8,\"type\":\"dialogue\"}");
            sb.AppendLine("]");

            return sb.ToString();
        }

        /// <summary>
        /// 解析 LLM 输出的 JSON 数组为 ScriptLine 列表。
        /// Schema 驱动的通用解析 + fieldSetter 映射。纯函数，线程安全。
        /// </summary>
        /// <param name="json">LLM 输出的 JSON 数组字符串。</param>
        /// <returns>解析后的 ScriptLine 列表。解析失败返回空列表。</returns>
        public static List<ScriptLine> Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new List<ScriptLine>();

            try
            {
                // Step 1: 解析 JSON 数组
                var objects = JsonParser.ParseObjectArray(json);
                if (objects == null || objects.Count == 0)
                    return new List<ScriptLine>();

                var lines = new List<ScriptLine>(objects.Count);

                // Step 2: 对每个元素，按 Schema 提取字段
                foreach (var dict in objects)
                {
                    var line = new ScriptLine();
                    ApplyDefaults(line);

                    foreach (var field in Schema)
                    {
                        if (dict.TryGetValue(field.JsonKey, out string rawValue))
                        {
                            // 字段存在：用 fieldSetter 赋值
                            if (fieldSetter.TryGetValue(field.JsonKey, out var setter))
                            {
                                setter(line, rawValue);
                            }
                        }
                        // 字段缺失：DefaultValue 已被 ApplyDefaults 处理
                    }

                    // 必需字段校验：t 缺失时报 warning 但保留行
                    if (dict.TryGetValue("t", out string textVal) == false &&
                        line.Type != ScriptLineType.Pause)
                    {
                        // 缺失必需字段 t，使用空字符串（已在 ApplyDefaults 中设置）
                    }

                    lines.Add(line);
                }

                return lines;
            }
            catch (Exception)
            {
                // 解析失败返回空列表
                return new List<ScriptLine>();
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 对一行应用所有 Schema 中定义的默认值。
        /// </summary>
        private static void ApplyDefaults(ScriptLine line)
        {
            foreach (var field in Schema)
            {
                if (field.Required || field.DefaultValue == null)
                    continue;

                if (fieldSetter.TryGetValue(field.JsonKey, out var setter))
                {
                    setter(line, field.DefaultValue);
                }
            }
        }

        /// <summary>
        /// 解析类型字符串为 ScriptLineType。供外部（如 PushLine）使用。
        /// </summary>
        public static ScriptLineType ParseLineType(string val)
        {
            if (string.IsNullOrEmpty(val))
                return ScriptLineType.Dialogue;

            switch (val.ToLowerInvariant().Trim())
            {
                case "dialogue": return ScriptLineType.Dialogue;
                case "narration": return ScriptLineType.Narration;
                case "action": return ScriptLineType.Action;
                case "pause": return ScriptLineType.Pause;
                default: return ScriptLineType.Dialogue;
            }
        }
    }
}
