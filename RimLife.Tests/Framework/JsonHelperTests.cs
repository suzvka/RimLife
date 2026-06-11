using RimLife.Framework;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// JsonHelper 纯逻辑断言测试。
    /// 覆盖：Escape / Quote
    /// </summary>
    public class JsonHelperTests
    {
        [Fact]
        public void Escape_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, JsonHelper.Escape(null));
            Assert.Equal(string.Empty, JsonHelper.Escape(string.Empty));
        }

        [Fact]
        public void Escape_PlainString_ReturnsSame()
        {
            Assert.Equal("hello world", JsonHelper.Escape("hello world"));
        }

        [Theory]
        [InlineData("\\", "\\\\")]
        [InlineData("\"", "\\\"")]
        [InlineData("\n", "\\n")]
        [InlineData("\r", "\\r")]
        [InlineData("\t", "\\t")]
        [InlineData("\b", "\\b")]
        [InlineData("\f", "\\f")]
        public void Escape_SpecialChars_EscapesCorrectly(string input, string expected)
        {
            Assert.Equal(expected, JsonHelper.Escape(input));
        }

        [Fact]
        public void Escape_ControlCharacters_EncodesUnicode()
        {
            // char code 0 (NUL) < 32 → unicode escape
            var result = JsonHelper.Escape("\0");
            Assert.Equal("\\u0000", result);

            // char code 31 (US) < 32 → unicode escape
            result = JsonHelper.Escape("\u001f");
            Assert.Equal("\\u001f", result);
        }

        [Fact]
        public void Escape_MixedContent_EscapesAll()
        {
            var input = "say \"hello\"\nnew line";
            var result = JsonHelper.Escape(input);

            Assert.Equal("say \\\"hello\\\"\\nnew line", result);
        }

        [Fact]
        public void Escape_ChineseCharacters_NotEscaped()
        {
            var input = "你好世界";
            Assert.Equal("你好世界", JsonHelper.Escape(input));
        }

        [Fact]
        public void Quote_WrapsInDoubleQuotes()
        {
            Assert.Equal("\"hello\"", JsonHelper.Quote("hello"));
            Assert.Equal("\"\"", JsonHelper.Quote(null));
            Assert.Equal("\"\"", JsonHelper.Quote(string.Empty));
        }

        [Fact]
        public void Quote_EscapesInternalQuotes()
        {
            var result = JsonHelper.Quote("say \"hi\"");
            Assert.Equal("\"say \\\"hi\\\"\"", result);
        }

        [Fact]
        public void Escape_Quote_RoundTrip_WithParser()
        {
            var original = "line1\nline2\t\"quoted\"";
            var escaped = JsonHelper.Escape(original);
            var unescaped = JsonParser.UnescapeJson(escaped);

            Assert.Equal(original, unescaped);
        }
    }
}
