using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// MCP 钩子提供者接口。游戏适配器侧实现此接口，
    /// 通过 McpSkillRegistry.RegisterFromProvider() 注册后，
    /// 其工具自动成为对应 Skill 下的 MCP 工具。
    /// 
    /// 一个 IMcpHookProvider 对应一个 Skill：
    ///   HookId → SkillId, HookName → Skill 名, HookDescription → Skill 描述
    ///   GetTools() → 该 Skill 下的工具列表
    /// 
    /// 使用示例（适配器侧）：
    ///   public class PawnMemoryHookProvider : IMcpHookProvider
    ///   {
    ///       public string HookId => "pawn_memory";
    ///       public string HookName => "Pawn个体记忆";
    ///       public string HookDescription => "读写Pawn的个体记忆与经历";
    ///       public IReadOnlyList<McpTool> GetTools() => new[] { ... };
    ///   }
    /// </summary>
    public interface IMcpHookProvider
    {
        /// <summary>钩子唯一 ID，兼作 SkillId（如 "pawn_memory"）。</summary>
        string HookId { get; }

        /// <summary>钩子显示名，兼作 Skill 名。</summary>
        string HookName { get; }

        /// <summary>钩子描述，兼作 Skill 描述。</summary>
        string HookDescription { get; }

        /// <summary>返回此提供者暴露的全部 McpTool。</summary>
        IReadOnlyList<McpTool> GetTools();
    }
}
