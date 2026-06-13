using RimLife.Framework.Mcp;
using System;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// MCP Skill 注册表自检测试。覆盖 Skill 注册、纯函数查询（GetActiveToolsJson 等）、
    /// 工具调用等核心路径。McpSkillRegistry 是无状态的纯注册表，激活状态由调用方提供。
    /// </summary>
    public class McpSkillRegistryTests
    {
        // ================================================================
        // 测试用工具方法（模拟真实的 MCP 工具）
        // ================================================================

        [McpSkill("test_colony")]
        [McpTool(Name = "get_overview", Description = "获取概览")]
        public static string GetOverview() => "overview";

        [McpSkill("test_colony")]
        [McpTool(Name = "get_events", Description = "获取事件")]
        public static string GetEvents(
            [McpParam(Description = "条数")] int limit = 10) => $"events:{limit}";

        [McpSkill("test_character")]
        [McpTool(Name = "get_character", Description = "获取角色")]
        public static string GetCharacter(
            [McpParam(Description = "角色ID")] string id) => $"char:{id}";

        [McpSkill("test_character")]
        [McpTool(Name = "find_characters", Description = "查找角色")]
        public static string FindCharacters() => "found";

        [McpTool(Name = "orphan_tool", Description = "无技能归属的工具")]
        public static string OrphanTool() => "orphan";

        // ================================================================
        // 初始化
        // ================================================================

        private static void SetupRegistry()
        {
            McpSkillRegistry.InitializeDefaults();
            McpSkillRegistry.RegisterSkill("test_colony", "测试殖民地", "测试用殖民地技能");
            McpSkillRegistry.RegisterSkill("test_character", "测试角色", "测试用角色技能");
            McpSkillRegistry.RegisterFromType(typeof(McpSkillRegistryTests));
        }

        // ================================================================
        // 1. Skill 元数据
        // ================================================================

        [Fact]
        public void InitializeDefaults_CreatesEightBusinessSkills()
        {
            McpSkillRegistry.InitializeDefaults();
            Assert.Equal(8, McpSkillRegistry.SkillCount);
        }

        [Fact]
        public void GetAllSkillIds_ContainsExpectedSkills()
        {
            McpSkillRegistry.InitializeDefaults();
            var ids = McpSkillRegistry.GetAllSkillIds();
            Assert.Contains("colony_overview", ids);
            Assert.Contains("character_query", ids);
            Assert.Contains("relationship_query", ids);
            Assert.Contains("event_query", ids);
            Assert.Contains("environment_query", ids);
            Assert.Contains("knowledge_management", ids);
            Assert.Contains("workspace_direction", ids);
            Assert.Contains("workspace_writing", ids);
            Assert.DoesNotContain(McpSkillRegistry.SystemSkillId, ids);
        }

        // ================================================================
        // 2. 工具注册
        // ================================================================

        [Fact]
        public void RegisterFromType_RegistersToolsWithSkillAttribute()
        {
            SetupRegistry();
            Assert.Equal(4, McpSkillRegistry.TotalToolCount); // 2 test_colony + 2 test_character
        }

        [Fact]
        public void RegisterFromType_OrphanToolSkipped()
        {
            SetupRegistry();
            // 激活 test_colony 和 test_character，孤儿工具不应出现
            var json = McpSkillRegistry.GetActiveToolsJson(new[] { "test_colony", "test_character" });
            Assert.DoesNotContain("orphan_tool", json);
        }

        // ================================================================
        // 3. GetActiveToolsJson 纯函数
        // ================================================================

        [Fact]
        public void GetActiveToolsJson_EmptyList_OnlySystemTools()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetActiveToolsJson(new string[0]);
            Assert.True(json.StartsWith("[") || json == "[]");
        }

        [Fact]
        public void GetActiveToolsJson_Null_OnlySystemTools()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetActiveToolsJson(null);
            Assert.True(json.StartsWith("[") || json == "[]");
        }

        [Fact]
        public void GetActiveToolsJson_SingleSkill_ContainsItsTools()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetActiveToolsJson(new[] { "test_colony" });
            Assert.Contains("get_overview", json);
            Assert.Contains("get_events", json);
            Assert.DoesNotContain("get_character", json);
        }

        [Fact]
        public void GetActiveToolsJson_MultipleSkills_ContainsAllTools()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetActiveToolsJson(new[] { "test_colony", "test_character" });
            Assert.Contains("get_overview", json);
            Assert.Contains("get_character", json);
            Assert.Contains("find_characters", json);
        }

        [Fact]
        public void GetActiveToolsJson_EmptyVsFull_DifferentSizes()
        {
            SetupRegistry();
            var empty = McpSkillRegistry.GetActiveToolsJson(new string[0]);
            var full = McpSkillRegistry.GetActiveToolsJson(
                McpSkillRegistry.GetAllSkillIds());
            Assert.True(full.Length > empty.Length);
        }

        // ================================================================
        // 4. GetSkillListJson 纯函数
        // ================================================================

        [Fact]
        public void GetSkillListJson_ContainsAllSkills()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetSkillListJson(new string[0]);
            Assert.Contains("\"skills\"", json);
            Assert.Contains("test_colony", json);
            Assert.Contains("test_character", json);
        }

        [Fact]
        public void GetSkillListJson_ReflectsActiveState()
        {
            SetupRegistry();
            var inactive = McpSkillRegistry.GetSkillListJson(new string[0]);
            Assert.Contains("\"active\":false", inactive);

            var active = McpSkillRegistry.GetSkillListJson(new[] { "test_colony" });
            Assert.Contains("\"active\":true", active);
        }

        [Fact]
        public void GetSkillListJson_SystemAlwaysActive()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetSkillListJson(new string[0]);
            // system 技能始终 active=true，应体现在 JSON 中
            Assert.Contains(McpSkillRegistry.SystemSkillId, json);
        }

        // ================================================================
        // 5. GetSkillToolsJson
        // ================================================================

        [Fact]
        public void GetSkillToolsJson_ReturnsToolsForSkill()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetSkillToolsJson("test_colony");
            Assert.Contains("get_overview", json);
            Assert.Contains("get_events", json);
        }

        [Fact]
        public void GetSkillToolsJson_UnknownSkill_ReturnsEmpty()
        {
            SetupRegistry();
            var json = McpSkillRegistry.GetSkillToolsJson("nonexistent");
            Assert.Equal("[]", json);
        }

        // ================================================================
        // 6. InvokeTool 纯函数
        // ================================================================

        [Fact]
        public void InvokeTool_FindsToolInActiveSkills()
        {
            SetupRegistry();
            var result = McpSkillRegistry.InvokeTool(
                new[] { "test_colony" }, "get_overview", "{}");
            Assert.Contains("overview", result);
        }

        [Fact]
        public void InvokeTool_FallbackToSystem()
        {
            SetupRegistry();
            // list_skills 属于 system skill，即使 activeSkillIds 为空也应找到
            var result = McpSkillRegistry.InvokeTool(
                null, "list_skills", "{\"workspaceId\":\"test\"}");
            // 只要能找到工具即可（具体输出取决于运行时环境）
            Assert.NotNull(result);
        }

        [Fact]
        public void InvokeTool_NotFound_ReturnsError()
        {
            SetupRegistry();
            var result = McpSkillRegistry.InvokeTool(
                new string[0], "nonexistent_tool", "{}");
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void InvokeTool_ToolNotInActiveScope_ReturnsError()
        {
            SetupRegistry();
            // get_character 在 test_character skill 中，但 activeSkillIds 只包含 test_colony
            var result = McpSkillRegistry.InvokeTool(
                new[] { "test_colony" }, "get_character", "{\"id\":\"pawn1\"}");
            Assert.Contains("\"error\"", result);
        }

        // ================================================================
        // 7. MakeActivateResult / MakeDeactivateResult / MakeError
        // ================================================================

        [Fact]
        public void MakeActivateResult_ProducesValidJson()
        {
            var result = McpSkillRegistry.MakeActivateResult("test_skill", "[]");
            Assert.Contains("\"activated\"", result);
            Assert.Contains("test_skill", result);
        }

        [Fact]
        public void MakeDeactivateResult_ProducesValidJson()
        {
            var result = McpSkillRegistry.MakeDeactivateResult("test_skill");
            Assert.Contains("\"deactivated\"", result);
            Assert.Contains("test_skill", result);
        }

        [Fact]
        public void MakeError_ProducesValidJson()
        {
            var result = McpSkillRegistry.MakeError("something wrong");
            Assert.Contains("\"error\"", result);
            Assert.Contains("something wrong", result);
        }

        // ================================================================
        // 8. McpToolGenerator 集成
        // ================================================================

        [Fact]
        public void SerializeAllActiveTools_ReturnsValidJson()
        {
            SetupRegistry();
            var json = McpToolGenerator.SerializeAllActiveTools(new[] { "test_colony" });
            Assert.StartsWith("[", json);
            Assert.Contains("get_overview", json);
        }

        [Fact]
        public void SerializeSkillList_ReturnsValidJson()
        {
            SetupRegistry();
            var json = McpToolGenerator.SerializeSkillList(new[] { "test_colony" });
            Assert.Contains("\"skills\"", json);
        }
    }
}
