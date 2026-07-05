namespace RimLife.Skills
{
    /// <summary>
    /// 技能模块接口 —— 第三方 Skill 的唯一集成入口。
    /// 
    /// 实现此接口的类会被 SkillModuleLoader 自动发现并注册到 MCP 工具系统。
    /// 工具方法通过 [SkillTool] 特性声明，无需了解 NPCLife 内部机制。
    /// 
    /// 使用示例：
    /// <code>
    /// public class MySkill : ISkillProvider
    /// {
    ///     public string ModuleId => "my_skill";
    ///     public string ModuleName => "我的技能";
    ///     public string ModuleDescription => "提供自定义功能";
    ///     public string[] TargetRoles => new[] { SkillRoles.Director };
    ///     public string PromptInstruction => "使用说明...";
    ///     
    ///     [SkillTool(Name = "do_thing", Description = "做某事")]
    ///     public string DoThing(string param1, bool flag = false) { ... }
    /// }
    /// </code>
    /// </summary>
    public interface ISkillProvider
    {
        /// <summary>模块唯一 ID（如 "colony_overview"）。与 manifest.json 中的 id 一致。</summary>
        string ModuleId { get; }

        /// <summary>模块显示名（如 "殖民地概览"）。</summary>
        string ModuleName { get; }

        /// <summary>模块功能描述。</summary>
        string ModuleDescription { get; }

        /// <summary>
        /// 该技能默认授权给哪些 Agent 角色。
        /// 使用 SkillRoles 常量，如 new[] { SkillRoles.Director, SkillRoles.Screenwriter }。
        /// </summary>
        string[] TargetRoles { get; }

        /// <summary>
        /// 注入到 Agent system prompt 的技能使用说明。
        /// 当该 Skill 被激活时追加到 prompt 末尾。返回 null 表示无额外说明。
        /// </summary>
        string PromptInstruction { get; }
    }
}
