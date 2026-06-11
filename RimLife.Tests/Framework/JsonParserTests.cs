using System.Collections.Generic;
using RimLife.Framework;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// JsonParser 纯逻辑断言测试。
    /// 覆盖：ParseDict / ParseObjectArray / UnescapeJson / SerializeDict / SerializeValue / DeserializeValue
    /// </summary>
    public class JsonParserTests
    {
        // ================================================================
        // ParseDict
        // ================================================================

        [Fact]
        public void ParseDict_EmptyJson_ReturnsEmptyDict()
        {
            var result = JsonParser.ParseDict("{}");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseDict_NullOrEmpty_ReturnsEmptyDict()
        {
            Assert.Empty(JsonParser.ParseDict(null));
            Assert.Empty(JsonParser.ParseDict(string.Empty));
        }

        [Fact]
        public void ParseDict_SimpleKeyValues_ParsesAll()
        {
            var json = "{\"name\":\"Alice\",\"age\":\"30\",\"active\":\"true\"}";
            var result = JsonParser.ParseDict(json);

            Assert.Equal(3, result.Count);
            Assert.Equal("Alice", result["name"]);
            Assert.Equal("30", result["age"]);
            Assert.Equal("true", result["active"]);
        }

        [Fact]
        public void ParseDict_EscapedChars_UnescapesCorrectly()
        {
            var json = "{\"msg\":\"hello\\nworld\\t!\"}";
            var result = JsonParser.ParseDict(json);

            Assert.Equal("hello\nworld\t!", result["msg"]);
        }

        [Fact]
        public void ParseDict_NestedObject_PreservesAsRawJson()
        {
            var json = "{\"data\":{\"x\":1,\"y\":2}}";
            var result = JsonParser.ParseDict(json);

            Assert.True(result.ContainsKey("data"));
            Assert.Equal("{\"x\":1,\"y\":2}", result["data"]);
        }

        [Fact]
        public void ParseDict_NestedArray_PreservesAsRawJson()
        {
            var json = "{\"items\":[1,2,3]}";
            var result = JsonParser.ParseDict(json);

            Assert.True(result.ContainsKey("items"));
            Assert.Equal("[1,2,3]", result["items"]);
        }

        [Fact]
        public void ParseDict_BareValues_NumberAndBoolean()
        {
            var json = "{\"count\":42,\"flag\":false}";
            var result = JsonParser.ParseDict(json);

            Assert.Equal("42", result["count"]);
            Assert.Equal("false", result["flag"]);
        }

        [Fact]
        public void ParseDict_ChineseCharacters_HandlesUtf8()
        {
            var json = "{\"标题\":\"你好世界\",\"状态\":\"活跃\"}";
            var result = JsonParser.ParseDict(json);

            Assert.Equal("你好世界", result["标题"]);
            Assert.Equal("活跃", result["状态"]);
        }

        // ================================================================
        // ParseObjectArray
        // ================================================================

        [Fact]
        public void ParseObjectArray_EmptyArray_ReturnsEmptyList()
        {
            Assert.Empty(JsonParser.ParseObjectArray("[]"));
            Assert.Empty(JsonParser.ParseObjectArray(null));
            Assert.Empty(JsonParser.ParseObjectArray(string.Empty));
        }

        [Fact]
        public void ParseObjectArray_TwoObjects_ParsesBoth()
        {
            var json = "[{\"id\":\"1\"},{\"id\":\"2\"}]";
            var result = JsonParser.ParseObjectArray(json);

            Assert.Equal(2, result.Count);
            Assert.Equal("1", result[0]["id"]);
            Assert.Equal("2", result[1]["id"]);
        }

        [Fact]
        public void ParseObjectArray_WithWhitespace_HandlesGracefully()
        {
            var json = "[\n  {\"a\":\"1\"},\n  {\"b\":\"2\"}\n]";
            var result = JsonParser.ParseObjectArray(json);

            Assert.Equal(2, result.Count);
        }

        // ================================================================
        // UnescapeJson
        // ================================================================

        [Fact]
        public void UnescapeJson_NoEscapes_ReturnsSame()
        {
            Assert.Equal("hello", JsonParser.UnescapeJson("hello"));
            Assert.Equal(string.Empty, JsonParser.UnescapeJson(string.Empty));
            Assert.Null(JsonParser.UnescapeJson(null));
        }

        [Theory]
        [InlineData("\\n", "\n")]
        [InlineData("\\r", "\r")]
        [InlineData("\\t", "\t")]
        [InlineData("\\\\", "\\")]
        [InlineData("\\\"", "\"")]
        public void UnescapeJson_StandardEscapes_DecodesCorrectly(string input, string expected)
        {
            Assert.Equal(expected, JsonParser.UnescapeJson(input));
        }

        [Fact]
        public void UnescapeJson_MixedEscapes_DecodesAll()
        {
            var input = "line1\\nline2\\tindented";
            var expected = "line1\nline2\tindented";
            Assert.Equal(expected, JsonParser.UnescapeJson(input));
        }

        // ================================================================
        // SerializeDict
        // ================================================================

        [Fact]
        public void SerializeDict_NullOrEmpty_ReturnsEmptyJson()
        {
            Assert.Equal("{}", JsonParser.SerializeDict(null));
            Assert.Equal("{}", JsonParser.SerializeDict(new Dictionary<string, string>()));
        }

        [Fact]
        public void SerializeDict_TwoEntries_ProducesValidJson()
        {
            var dict = new Dictionary<string, string>
            {
                ["name"] = "Bob",
                ["score"] = "100"
            };
            var json = JsonParser.SerializeDict(dict);

            Assert.Contains("\"name\":\"Bob\"", json);
            Assert.Contains("\"score\":\"100\"", json);
            Assert.StartsWith("{", json);
            Assert.EndsWith("}", json);
        }

        [Fact]
        public void SerializeDict_RoundTrip_EqualsOriginal()
        {
            var original = new Dictionary<string, string>
            {
                ["key"] = "value with spaces",
                ["num"] = "42"
            };
            var json = JsonParser.SerializeDict(original);
            var parsed = JsonParser.ParseDict(json);

            Assert.Equal(original.Count, parsed.Count);
            Assert.Equal(original["key"], parsed["key"]);
            Assert.Equal(original["num"], parsed["num"]);
        }

        // ================================================================
        // SerializeValue / DeserializeValue
        // ================================================================

        [Fact]
        public void SerializeValue_Null_ReturnsNull()
        {
            Assert.Equal("null", JsonParser.SerializeValue<object>(null));
        }

        [Fact]
        public void SerializeValue_Primitives_ReturnsStringForm()
        {
            Assert.Equal("hello", JsonParser.SerializeValue("hello"));
            Assert.Equal("42", JsonParser.SerializeValue(42));
            Assert.Equal("100", JsonParser.SerializeValue(100L));
            Assert.Equal("true", JsonParser.SerializeValue(true));
            Assert.Equal("false", JsonParser.SerializeValue(false));
        }

        [Fact]
        public void SerializeValue_Floats_InvariantCulture()
        {
            var result = JsonParser.SerializeValue(3.14f);
            Assert.Contains(".", result); // 确保使用小数点而非逗号
        }

        [Fact]
        public void DeserializeValue_RoundTrip_AllPrimitives()
        {
            Assert.Equal(42, JsonParser.DeserializeValue<int>("42"));
            Assert.Equal(100L, JsonParser.DeserializeValue<long>("100"));
            Assert.True(JsonParser.DeserializeValue<bool>("true"));
            Assert.False(JsonParser.DeserializeValue<bool>("false"));
            Assert.Equal("text", JsonParser.DeserializeValue<string>("text"));
        }

        [Fact]
        public void DeserializeValue_NullOrNullLiteral_ReturnsDefault()
        {
            Assert.Equal(default(int), JsonParser.DeserializeValue<int>(null));
            Assert.Equal(default(int), JsonParser.DeserializeValue<int>("null"));
            Assert.Null(JsonParser.DeserializeValue<string>(null));
        }

        [Fact]
        public void SerializeDeserialize_RoundTrip_Int()
        {
            var json = JsonParser.SerializeValue(12345);
            var value = JsonParser.DeserializeValue<int>(json);
            Assert.Equal(12345, value);
        }

        [Fact]
        public void SerializeDeserialize_RoundTrip_Float()
        {
            var json = JsonParser.SerializeValue(3.14f);
            var value = JsonParser.DeserializeValue<float>(json);
            Assert.Equal(3.14f, value, 3);
        }

        [Fact]
        public void DeserializeValue_UnsupportedType_ReturnsDefault()
        {
            // 对于不支持的类型应返回 default
            var result = JsonParser.DeserializeValue<byte>("255");
            Assert.Equal(default(byte), result);
        }
    }
}
