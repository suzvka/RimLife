using RimLife.Framework.Mcp;
using System;
using System.Linq;
using Xunit;

namespace RimLife.Tests.Framework
{
    /// <summary>
    /// MCP Skill 注册表自检测试。覆盖 Skill 注册、激活/反激活、工具序列化等核心路径。
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

        /// <summary>无 [McpSkill] 标注的方法应被 RegisterFromType 跳过。</summary>
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
        public void InitializeDefaults_CreatesSevenBusinessSkills()
        {
            McpSkillRegistry.InitializeDefaults();
            Assert.Equal(7, McpSkillRegistry.SkillCount);
        }

        [Fact]
        public void InitializeDefaults_SystemSkillIsActive()
        {
            McpSkillRegistry.InitializeDefaults();
            Assert.True(McpSkillRegistry.IsActive(McpSkillRegistry.SystemSkillId));
            Assert.Equal(1, McpSkillRegistry.ActiveSkillCount);
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
            Assert.Contains("workspace_management", ids);
            Assert.DoesNotContain(McpSkillRegistry.SystemSkillId, ids); // system 不在业务技能中
        }

        // ================================================================
        // 2. 工具注册
        // ================================================================

        [Fact]
        public void RegisterFromType_RegistersToolsWithSkillAttribute()
        {
            SetupRegistry();

            // orphan_tool 无 [McpSkill]，不应被注册
            Assert.Equal(4, McpSkillRegistry.TotalToolCount); // 2 test_colony + 2 test_character
        }

        [Fact]
        public void RegisterFromType_OrphanToolSkipped()
        {
            SetupRegistry();

            // 验证孤儿工具不被包含在活跃工具中
            var activeJson = McpSkillRegistry.GetActiveToolsJson();
            Assert.DoesNotContain("orphan_tool", activeJson);
        }

        // ================================================================
        // 3. 激活 / 反激活
        // ================================================================

        [Fact]
        public void ActivateSkill_AddsToActiveSet()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.ActivateSkill("test_colony");
            Assert.Contains("\"activated\"", result);
            Assert.Contains("test_colony", result);
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
            Assert.Equal(2, McpSkillRegistry.ActiveSkillCount); // system + test_colony
        }

        [Fact]
        public void ActivateSkill_AlreadyActive_NoError()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            var result = McpSkillRegistry.ActivateSkill("test_colony"); // 重复激活
            Assert.DoesNotContain("\"error\"", result);
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
            Assert.Equal(2, McpSkillRegistry.ActiveSkillCount);
        }

        [Fact]
        public void ActivateSkill_UnknownSkill_ReturnsError()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.ActivateSkill("nonexistent_skill");
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void ActivateSkill_AccumulatesCorrectly()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            McpSkillRegistry.ActivateSkill("test_character");
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
            Assert.True(McpSkillRegistry.IsActive("test_character"));
            Assert.Equal(3, McpSkillRegistry.ActiveSkillCount); // system + 2
        }

        [Fact]
        public void DeactivateSkill_RemovesFromActiveSet()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            McpSkillRegistry.DeactivateSkill("test_colony");
            Assert.False(McpSkillRegistry.IsActive("test_colony"));
            Assert.Equal(1, McpSkillRegistry.ActiveSkillCount);
        }

        [Fact]
        public void DeactivateSkill_System_CannotBeDeactivated()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.DeactivateSkill(McpSkillRegistry.SystemSkillId);
            Assert.Contains("\"error\"", result);
            Assert.True(McpSkillRegistry.IsActive(McpSkillRegistry.SystemSkillId));
        }

        [Fact]
        public void DeactivateSkill_UnknownSkill_ReturnsError()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.DeactivateSkill("nonexistent");
            Assert.Contains("\"error\"", result);
        }

        // ================================================================
        // 4. 工具序列化
        // ================================================================

        [Fact]
        public void GetActiveToolsJson_Initial_OnlySystemTools()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill(McpSkillRegistry.SystemSkillId); // 确保 system 激活
            var json = McpSkillRegistry.GetActiveToolsJson();
            // 初始应该为空（因为测试类中没有注册 system 工具）
            // 这里仅验证格式合法
            Assert.True(json.StartsWith("[") || json == "[]");
        }

        [Fact]
        public void GetActiveToolsJson_AfterActivate_ContainsSkillTools()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            var json = McpSkillRegistry.GetActiveToolsJson();
            Assert.Contains("get_overview", json);
            Assert.Contains("get_events", json);
            Assert.DoesNotContain("get_character", json); // test_character 未激活
        }

        [Fact]
        public void GetActiveToolsJson_AfterDeactivate_RemovesTools()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            McpSkillRegistry.ActivateSkill("test_character");
            McpSkillRegistry.DeactivateSkill("test_colony");

            var json = McpSkillRegistry.GetActiveToolsJson();
            Assert.DoesNotContain("get_overview", json);
            Assert.Contains("get_character", json);
            Assert.Contains("find_characters", json);
        }

        // ================================================================
        // 5. Skill 列表 JSON
        // ================================================================

        [Fact]
        public void GetSkillListJson_ContainsAllSkills()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var json = McpSkillRegistry.GetSkillListJson();
            Assert.Contains("\"skills\"", json);
            Assert.Contains("test_colony", json);
            Assert.Contains("test_character", json);
        }

        [Fact]
        public void GetSkillListJson_ReflectsActiveState()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            // 激活前
            var before = McpSkillRegistry.GetSkillListJson();
            Assert.Contains("\"active\":false", before);

            // 激活后
            McpSkillRegistry.ActivateSkill("test_colony");
            var after = McpSkillRegistry.GetSkillListJson();
            Assert.Contains("\"active\":true", after);
        }

        // ================================================================
        // 6. Reset
        // ================================================================

        [Fact]
        public void Reset_ClearsAllExceptSystem()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            McpSkillRegistry.ActivateSkill("test_character");
            McpSkillRegistry.Reset();

            Assert.True(McpSkillRegistry.IsActive(McpSkillRegistry.SystemSkillId));
            Assert.False(McpSkillRegistry.IsActive("test_colony"));
            Assert.False(McpSkillRegistry.IsActive("test_character"));
            Assert.Equal(1, McpSkillRegistry.ActiveSkillCount);
        }

        // ================================================================
        // 6.5. 预载管理
        // ================================================================

        [Fact]
        public void AddPreload_AddsToPreloadSetAndActivates()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.AddPreload("test_colony");
            Assert.Contains("\"action\":\"preloaded\"", result);
            Assert.True(McpSkillRegistry.IsPreloaded("test_colony"));
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
            Assert.Contains("test_colony", McpSkillRegistry.GetPreloadSkillIds());
        }

        [Fact]
        public void AddPreload_AlreadyPreloaded_ReturnsAlreadyPreloaded()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.AddPreload("test_colony");
            var result = McpSkillRegistry.AddPreload("test_colony");
            Assert.Contains("\"action\":\"already_preloaded\"", result);
            Assert.True(McpSkillRegistry.IsPreloaded("test_colony"));
        }

        [Fact]
        public void AddPreload_UnknownSkill_ReturnsError()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.AddPreload("nonexistent_skill");
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void RemovePreload_RemovesFromPreloadSet()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.AddPreload("test_colony");
            var result = McpSkillRegistry.RemovePreload("test_colony");
            Assert.Contains("\"action\":\"unpreloaded\"", result);
            Assert.False(McpSkillRegistry.IsPreloaded("test_colony"));
            Assert.DoesNotContain("test_colony", McpSkillRegistry.GetPreloadSkillIds());
        }

        [Fact]
        public void RemovePreload_DoesNotDeactivate()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.AddPreload("test_colony");
            McpSkillRegistry.RemovePreload("test_colony");
            // 解除预载不反激活
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
        }

        [Fact]
        public void RemovePreload_System_CannotBeRemoved()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.RemovePreload(McpSkillRegistry.SystemSkillId);
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void RemovePreload_UnknownSkill_ReturnsError()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var result = McpSkillRegistry.RemovePreload("nonexistent");
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void ApplyPreloads_BulkActivatesSkills()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            int count = McpSkillRegistry.ApplyPreloads(new[] { "test_colony", "test_character" });
            Assert.Equal(2, count);
            Assert.True(McpSkillRegistry.IsActive("test_colony"));
            Assert.True(McpSkillRegistry.IsActive("test_character"));
            Assert.True(McpSkillRegistry.IsPreloaded("test_colony"));
            Assert.True(McpSkillRegistry.IsPreloaded("test_character"));
        }

        [Fact]
        public void ApplyPreloads_IgnoresUnknownSkills()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            int count = McpSkillRegistry.ApplyPreloads(new[] { "test_colony", "unknown_skill" });
            Assert.Equal(1, count);
            Assert.True(McpSkillRegistry.IsPreloaded("test_colony"));
        }

        [Fact]
        public void ApplyPreloads_EmptyList_ReturnsZero()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            int count = McpSkillRegistry.ApplyPreloads();
            Assert.Equal(0, count);
        }

        [Fact]
        public void GetSkillListJson_ContainsPreloadedField()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.AddPreload("test_colony");
            var json = McpSkillRegistry.GetSkillListJson();
            Assert.Contains("\"preloaded\":true", json);
            Assert.Contains("\"preloadedSkillIds\"", json);
            Assert.Contains("test_colony", json);
        }

        // ================================================================
        // 7. McpToolGenerator 集成
        // ================================================================

        [Fact]
        public void SerializeAllActiveTools_ReturnsValidJson()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            McpSkillRegistry.ActivateSkill("test_colony");
            var json = McpToolGenerator.SerializeAllActiveTools();
            Assert.StartsWith("[", json);
            Assert.Contains("get_overview", json);
        }

        [Fact]
        public void SerializeSkillList_ReturnsValidJson()
        {
            SetupRegistry();
            McpSkillRegistry.Reset();

            var json = McpToolGenerator.SerializeSkillList();
            Assert.Contains("\"skills\"", json);
        }
    }
}
