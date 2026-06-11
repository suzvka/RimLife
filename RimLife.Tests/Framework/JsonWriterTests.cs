using System.Collections.Generic;
using RimLife.Framework;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// JsonWriter 纯逻辑断言测试。
    /// 注意：由于 .NET Framework 对 struct 的 new T() 初始化行为差异，
    /// 所有测试使用链式调用从 new JsonWriter(256) 直接开始，
    /// 避免局部变量赋值时的 struct 复制问题。
    /// </summary>
    public class JsonWriterTests
    {
        [Fact]
        public void Close_EmptyObject_ReturnsEmptyBraces()
        {
            Assert.Equal("{}", new JsonWriter(256).Close());
        }

        [Fact]
        public void Prop_StringValue_EncodesCorrectly()
        {
            var json = new JsonWriter(256)
                .Prop("name", "Alice")
                .Close();
            Assert.Equal("{\"name\":\"Alice\"}", json);
        }

        [Fact]
        public void Prop_NullStringValue_SkipsField()
        {
            var json = new JsonWriter(256)
                .Prop("skip", (string)null)
                .Prop("age", "30")
                .Close();
            Assert.Equal("{\"age\":\"30\"}", json);
        }

        [Fact]
        public void Prop_BoolValue_WritesUnquoted()
        {
            var json = new JsonWriter(256)
                .Prop("active", true)
                .Prop("deleted", false)
                .Close();
            Assert.Contains("\"active\":true", json);
            Assert.Contains("\"deleted\":false", json);
        }

        [Fact]
        public void Prop_IntValue_WritesUnquoted()
        {
            var json = new JsonWriter(256)
                .Prop("count", 42)
                .Close();
            Assert.Contains("\"count\":42", json);
        }

        [Fact]
        public void Prop_LongValue_WritesUnquoted()
        {
            var json = new JsonWriter(256)
                .Prop("big", 9999999999L)
                .Close();
            Assert.Contains("\"big\":9999999999", json);
        }

        [Fact]
        public void Prop_FloatValue_WritesUnquoted()
        {
            var json = new JsonWriter(256)
                .Prop("pi", 3.14f)
                .Close();
            Assert.Contains("\"pi\":3.14", json);
        }

        [Fact]
        public void Prop_FloatWithFormat_UsesFormat()
        {
            var json = new JsonWriter(256)
                .Prop("ratio", 0.6667f, "F2")
                .Close();
            Assert.Contains("\"ratio\":0.67", json);
        }

        [Fact]
        public void Prop_DoubleValue_WritesUnquoted()
        {
            var json = new JsonWriter(256)
                .Prop("e", 2.718281828)
                .Close();
            Assert.Contains("\"e\":2.718281828", json);
        }

        [Fact]
        public void PropRaw_EmbedsRawJson()
        {
            var json = new JsonWriter(256)
                .PropRaw("nested", "{\"x\":1}")
                .Close();
            Assert.Contains("\"nested\":{\"x\":1}", json);
        }

        [Fact]
        public void PropRaw_NullOrEmpty_SkipsField()
        {
            var json = new JsonWriter(256)
                .PropRaw("a", null)
                .PropRaw("b", "")
                .Prop("c", "ok")
                .Close();
            Assert.Equal("{\"c\":\"ok\"}", json);
        }

        [Fact]
        public void Array_StringValues_WritesJsonArray()
        {
            var json = new JsonWriter(256)
                .Array("tags", new[] { "urgent", "combat", "social" })
                .Close();
            Assert.Contains("\"tags\":[", json);
            Assert.Contains("\"urgent\"", json);
            Assert.Contains("\"combat\"", json);
            Assert.Contains("\"social\"", json);
        }

        [Fact]
        public void Array_NullOrEmpty_SkipsField()
        {
            var json = new JsonWriter(256)
                .Array("x", null)
                .Array("y", new string[0])
                .Prop("z", "ok")
                .Close();
            Assert.Equal("{\"z\":\"ok\"}", json);
        }

        [Fact]
        public void ArrayRaw_EmbedsRawValues()
        {
            var json = new JsonWriter(256)
                .ArrayRaw("coords", new[] { "{\"x\":1,\"y\":2}", "{\"x\":3,\"y\":4}" })
                .Close();
            Assert.Contains("\"coords\":[{\"x\":1,\"y\":2},{\"x\":3,\"y\":4}]", json);
        }

        [Fact]
        public void ArrayRaw_Null_SkipsField()
        {
            var json = new JsonWriter(256)
                .ArrayRaw("x", null)
                .Prop("ok", true)
                .Close();
            Assert.Equal("{\"ok\":true}", json);
        }

        [Fact]
        public void ChainedCalls_ProducesCompleteJson()
        {
            var json = new JsonWriter(256)
                .Prop("id", "evt_001")
                .Prop("tick", 1000)
                .Prop("severity", "Major")
                .Prop("active", true)
                .Close();

            Assert.Contains("\"id\":\"evt_001\"", json);
            Assert.Contains("\"tick\":1000", json);
            Assert.Contains("\"severity\":\"Major\"", json);
            Assert.Contains("\"active\":true", json);
            Assert.StartsWith("{", json);
            Assert.EndsWith("}", json);
        }

        [Fact]
        public void SpecialCharactersInKeys_AreEscaped()
        {
            var json = new JsonWriter(256)
                .Prop("say \"hello\"", "world")
                .Close();
            Assert.Contains("\\\"", json);
        }

        [Fact]
        public void SpecialCharactersInValues_AreEscaped()
        {
            var json = new JsonWriter(256)
                .Prop("msg", "line1\nline2")
                .Close();
            Assert.Contains("\\n", json);
        }

        [Fact]
        public void ToString_ReturnsJsonWithoutClosingBrace()
        {
            var json = new JsonWriter(256)
                .Prop("a", "1")
                .ToString();
            Assert.Contains("{\"a\":\"1\"}", json);
        }
    }
}
