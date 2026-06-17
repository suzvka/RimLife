using RimLife.Framework.Mcp;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// MCP 工具生成器自检测试。覆盖 McpTypeMapper / McpToolGenerator / McpToolInvoker 核心路径。
    /// </summary>
    public class McpToolGeneratorTests
    {
        // ================================================================
        // 测试用工具方法
        // ================================================================

        [McpTool(Name = "query_events", Description = "查询殖民地近期事件")]
        public static string QueryEvents(
            [McpParam(Description = "事件标签筛选", Required = McpRequired.False)] string tag,
            [McpParam(Description = "返回条数上限")] int limit
        )
        {
            return $"found events with tag={tag ?? "any"}, limit={limit}";
        }

        [McpTool(Description = "获取殖民地概览")]
        public static string GetColonyInfo()
        {
            return "colony: New Hope, pawns: 5";
        }

        public static string NoAttributeMethod(int x)
        {
            return x.ToString();
        }

        // ================================================================
        // McpTypeMapper
        // ================================================================

        [Theory]
        [InlineData(typeof(string), "string")]
        [InlineData(typeof(int), "integer")]
        [InlineData(typeof(long), "integer")]
        [InlineData(typeof(float), "number")]
        [InlineData(typeof(double), "number")]
        [InlineData(typeof(bool), "boolean")]
        [InlineData(typeof(int[]), "array")]
        public void GetSchemaType_BasicTypes_ReturnsCorrectSchemaType(System.Type type, string expected)
        {
            Assert.Equal(expected, McpTypeMapper.GetSchemaType(type));
        }

        [Fact]
        public void GetSchemaType_Enum_ReturnsString()
        {
            Assert.Equal("string", McpTypeMapper.GetSchemaType(typeof(System.StringSplitOptions)));
        }

        // ================================================================
        // McpToolGenerator — GenerateDefinition
        // ================================================================

        [Fact]
        public void GenerateDefinition_WithAttributes_ReadsNameAndDescription()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            var def = McpToolGenerator.GenerateDefinition(method);

            Assert.Equal("query_events", def.Name);
            Assert.Equal("查询殖民地近期事件", def.Description);
            Assert.NotNull(def.InputSchema.Properties);
            Assert.Equal(2, def.InputSchema.Properties.Count);
        }

        [Fact]
        public void GenerateDefinition_ParameterNames_FromAttributeOrFallback()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            var def = McpToolGenerator.GenerateDefinition(method);

            // tag 和 limit 参数名应从 C# 参数名自动推导（未设 Name）
            Assert.True(def.InputSchema.Properties.ContainsKey("tag"));
            Assert.True(def.InputSchema.Properties.ContainsKey("limit"));
        }

        [Fact]
        public void GenerateDefinition_RequiredParams_DetectedCorrectly()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            var def = McpToolGenerator.GenerateDefinition(method);

            // tag 有默认值 → optional；limit 无默认值 → required
            Assert.NotNull(def.InputSchema.Required);
            Assert.Contains("limit", def.InputSchema.Required);
            Assert.DoesNotContain("tag", def.InputSchema.Required);
        }

        [Fact]
        public void GenerateDefinition_NoParams_EmptyProperties()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(GetColonyInfo));
            var def = McpToolGenerator.GenerateDefinition(method);

            Assert.Equal("GetColonyInfo", def.Name);
            Assert.Equal("获取殖民地概览", def.Description);
            Assert.NotNull(def.InputSchema.Properties);
            Assert.Empty(def.InputSchema.Required ?? new System.Collections.Generic.List<string>());
        }

        // ================================================================
        // McpToolGenerator — Serialize
        // ================================================================

        [Fact]
        public void Serialize_ProducesValidMcpJson()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            var json = McpToolGenerator.SerializeMethod(method);

            Assert.Contains("\"name\":\"query_events\"", json);
            Assert.Contains("\"description\":\"查询殖民地近期事件\"", json);
            Assert.Contains("\"function\"", json);
            Assert.Contains("\"parameters\"", json);
            Assert.Contains("\"properties\"", json);
            Assert.Contains("\"required\"", json);
            Assert.Contains("\"limit\"", json);
        }

        [Fact]
        public void Serialize_NoParams_NoRequiredField()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(GetColonyInfo));
            var json = McpToolGenerator.SerializeMethod(method);

            Assert.Contains("\"name\":\"GetColonyInfo\"", json);
            Assert.DoesNotContain("\"required\"", json);
        }

        // ================================================================
        // McpToolGenerator — SerializeAllFrom
        // ================================================================

        [Fact]
        public void SerializeAllFrom_ScansMarkedMethods()
        {
            var json = McpToolGenerator.SerializeAllFrom(typeof(McpToolGeneratorTests));

            Assert.Contains("query_events", json);
            Assert.Contains("GetColonyInfo", json);
            Assert.DoesNotContain("NoAttributeMethod", json);
        }

        // ================================================================
        // McpToolInvoker
        // ================================================================

        [Fact]
        public void Invoke_WithJsonArgs_CallsMethod()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            var result = McpToolInvoker.Invoke(method, null, "{\"tag\":\"Raid\",\"limit\":10}");

            Assert.Contains("Raid", result);
            Assert.Contains("10", result);
        }

        [Fact]
        public void Invoke_NoParams_ReturnsResult()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(GetColonyInfo));
            var result = McpToolInvoker.Invoke(method, null, "{}");

            Assert.Contains("New Hope", result);
        }

        [Fact]
        public void Invoke_MissingRequiredParam_UsesDefault()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(QueryEvents));
            // 缺少 limit，使用 int 默认值 0
            var result = McpToolInvoker.Invoke(method, null, "{\"tag\":\"Raid\"}");

            Assert.Contains("limit=0", result);
        }

        [Fact]
        public void Invoke_EmptyJson_NoError()
        {
            var method = typeof(McpToolGeneratorTests).GetMethod(nameof(GetColonyInfo));
            var result = McpToolInvoker.Invoke(method, null, null);

            Assert.Contains("New Hope", result);
        }
    }
}
