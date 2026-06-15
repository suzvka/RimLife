using RimLife.Framework.Script;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// ScriptFormat 纯逻辑测试。覆盖 Parse 和 GetFormatSpec 的 Schema 一致性。
    /// </summary>
    public class ScriptFormatTests
    {
        // ================================================================
        // Parse — 正常解析
        // ================================================================

        [Fact]
        public void Parse_EmptyInput_ReturnsEmptyList()
        {
            Assert.Empty(ScriptFormat.Parse(null));
            Assert.Empty(ScriptFormat.Parse(string.Empty));
            Assert.Empty(ScriptFormat.Parse("[]"));
        }

        [Fact]
        public void Parse_DialogueLine_ParsesCorrectly()
        {
            var json = "[{\"s\":\"Pawn_123\",\"t\":\"你好！\",\"d\":0.5,\"type\":\"dialogue\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal("Pawn_123", lines[0].SpeakerId);
            Assert.Equal("你好！", lines[0].Text);
            Assert.Equal(0.5f, lines[0].RelativeTime);
            Assert.Equal(ScriptLineType.Dialogue, lines[0].Type);
        }

        [Fact]
        public void Parse_NarrationLine_ParsesCorrectly()
        {
            var json = "[{\"t\":\"风吹过麦田。\",\"d\":1.0,\"type\":\"narration\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Null(lines[0].SpeakerId);
            Assert.Equal("风吹过麦田。", lines[0].Text);
            Assert.Equal(1.0f, lines[0].RelativeTime);
            Assert.Equal(ScriptLineType.Narration, lines[0].Type);
        }

        [Fact]
        public void Parse_ActionLine_ParsesCorrectly()
        {
            var json = "[{\"t\":\"远处传来一声枪响。\",\"d\":0.3,\"type\":\"action\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(ScriptLineType.Action, lines[0].Type);
        }

        [Fact]
        public void Parse_PauseLine_ParsesCorrectly()
        {
            var json = "[{\"t\":\"\",\"d\":2.0,\"type\":\"pause\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(ScriptLineType.Pause, lines[0].Type);
            Assert.Equal(2.0f, lines[0].RelativeTime);
        }

        [Fact]
        public void Parse_MultipleLines_ParsesAll()
        {
            var json = "[{\"s\":\"Pawn_A\",\"t\":\"Line 1\",\"d\":0,\"type\":\"dialogue\"},{\"t\":\"Line 2\",\"d\":1.5,\"type\":\"narration\"},{\"s\":\"Pawn_B\",\"t\":\"Line 3\",\"d\":0.8,\"type\":\"dialogue\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Equal(3, lines.Count);
            Assert.Equal("Pawn_A", lines[0].SpeakerId);
            Assert.Equal("Pawn_B", lines[2].SpeakerId);
            Assert.Null(lines[1].SpeakerId);
        }

        // ================================================================
        // Parse — 默认值
        // ================================================================

        [Fact]
        public void Parse_MissingType_DefaultsToDialogue()
        {
            var json = "[{\"s\":\"Pawn_X\",\"t\":\"text\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(ScriptLineType.Dialogue, lines[0].Type);
        }

        [Fact]
        public void Parse_MissingDelay_DefaultsToZero()
        {
            var json = "[{\"s\":\"Pawn_X\",\"t\":\"text\",\"type\":\"dialogue\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(0f, lines[0].RelativeTime);
        }

        [Fact]
        public void Parse_MissingSpeaker_DefaultsToNull()
        {
            var json = "[{\"t\":\"text\",\"type\":\"dialogue\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Null(lines[0].SpeakerId);
        }

        // ================================================================
        // Parse — 容错
        // ================================================================

        [Fact]
        public void Parse_MalformedJson_ReturnsEmptyList()
        {
            Assert.Empty(ScriptFormat.Parse("not json at all"));
            Assert.Empty(ScriptFormat.Parse("{broken"));
        }

        [Fact]
        public void Parse_UnknownFields_Ignored()
        {
            var json = "[{\"s\":\"Pawn_X\",\"t\":\"text\",\"unknown_field\":\"ignored\",\"type\":\"narration\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal("Pawn_X", lines[0].SpeakerId);
            Assert.Equal("text", lines[0].Text);
            Assert.Equal(ScriptLineType.Narration, lines[0].Type);
        }

        [Fact]
        public void Parse_InvalidTypeValue_DefaultsToDialogue()
        {
            var json = "[{\"s\":\"Pawn_X\",\"t\":\"text\",\"type\":\"invalid\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(ScriptLineType.Dialogue, lines[0].Type);
        }

        [Fact]
        public void Parse_InvalidDelayValue_DefaultsToZero()
        {
            var json = "[{\"s\":\"Pawn_X\",\"t\":\"text\",\"d\":\"not_a_number\",\"type\":\"dialogue\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Single(lines);
            Assert.Equal(0f, lines[0].RelativeTime);
        }

        // ================================================================
        // Schema 一致性 — GetFormatSpec ↔ Parse
        // ================================================================

        [Fact]
        public void GetFormatSpec_ContainsAllFieldKeys()
        {
            var spec = ScriptFormat.GetFormatSpec();

            // Schema 中所有 JsonKey 都出现在提示词中
            Assert.Contains("\"s\"", spec);
            Assert.Contains("\"t\"", spec);
            Assert.Contains("\"d\"", spec);
            Assert.Contains("\"type\"", spec);
        }

        [Fact]
        public void GetFormatSpec_ContainsTypeHints()
        {
            var spec = ScriptFormat.GetFormatSpec();

            Assert.Contains("string", spec);
            Assert.Contains("number", spec);
            Assert.Contains("dialogue|narration|action|pause", spec);
        }

        [Fact]
        public void GetFormatSpec_ContainsUsageHints()
        {
            var spec = ScriptFormat.GetFormatSpec();

            Assert.Contains("ThingID", spec);
            Assert.Contains("等待秒数", spec);
            Assert.Contains("对话", spec);
            Assert.Contains("旁白", spec);
        }

        [Fact]
        public void GetFormatSpec_ContainsRequiredKeyword()
        {
            var spec = ScriptFormat.GetFormatSpec();

            // t 字段标记为必需
            Assert.Contains("必需", spec);
        }

        [Fact]
        public void GetFormatSpec_ContainsExample()
        {
            var spec = ScriptFormat.GetFormatSpec();

            Assert.Contains("示例", spec);
            Assert.Contains("Pawn_123", spec);
            Assert.Contains("dialogue", spec);
            Assert.Contains("narration", spec);
            Assert.Contains("action", spec);
        }

        // ================================================================
        // Round-trip — Parse 能正确解析 GetFormatSpec 示例格式
        // ================================================================

        [Fact]
        public void Parse_FormatSpecExample_CanBeParsed()
        {
            // 使用与 GetFormatSpec 示例一致的格式
            var json = "[{\"s\":\"Pawn_123\",\"t\":\"嘿，今天天气不错。\",\"d\":0,\"type\":\"dialogue\"},{\"t\":\"风吹过麦田，金色的波浪一层层涌向远方。\",\"d\":1.5,\"type\":\"narration\"}]";
            var lines = ScriptFormat.Parse(json);

            Assert.Equal(2, lines.Count);
            Assert.Equal("Pawn_123", lines[0].SpeakerId);
            Assert.Equal("嘿，今天天气不错。", lines[0].Text);
            Assert.Equal(0f, lines[0].RelativeTime);
            Assert.Equal(ScriptLineType.Dialogue, lines[0].Type);

            Assert.Null(lines[1].SpeakerId);
            Assert.Equal("风吹过麦田，金色的波浪一层层涌向远方。", lines[1].Text);
            Assert.Equal(1.5f, lines[1].RelativeTime);
            Assert.Equal(ScriptLineType.Narration, lines[1].Type);
        }
    }
}
