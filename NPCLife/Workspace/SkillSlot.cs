using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 工作空间内部技能槽。封装 ActiveSkillIds + McpSkillRegistry 的交互逻辑。
    /// 原 WorkspaceManager 上的 ActivateSkill/DeactivateSkill/GetActiveSkillIds 迁入此类。
    /// </summary>
    public class SkillSlot
    {
        private readonly List<string> _activeSkillIds;
        private readonly Action _onModified;

        public SkillSlot(List<string> activeSkillIds, Action onModified)
        {
            _activeSkillIds = activeSkillIds ?? new List<string>();
            _onModified = onModified;
        }

        public IReadOnlyList<string> ActiveSkillIds => _activeSkillIds;

        public string Activate(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return McpSkillRegistry.MakeError("skillId is required");

            if (string.Equals(skillId, McpSkillRegistry.SystemSkillId, StringComparison.OrdinalIgnoreCase))
                return McpSkillRegistry.MakeError($"System skill '{McpSkillRegistry.SystemSkillId}' is always active.");

            string newToolsJson;
            if (!_activeSkillIds.Contains(skillId))
            {
                _activeSkillIds.Add(skillId);
                newToolsJson = McpSkillRegistry.GetSkillToolsJson(skillId);
                _onModified?.Invoke();
            }
            else
            {
                newToolsJson = "[]";
            }

            return McpSkillRegistry.MakeActivateResult(skillId, newToolsJson);
        }

        public string Deactivate(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return McpSkillRegistry.MakeError("skillId is required");

            if (string.Equals(skillId, McpSkillRegistry.SystemSkillId, StringComparison.OrdinalIgnoreCase))
                return McpSkillRegistry.MakeError($"Cannot deactivate system skill '{McpSkillRegistry.SystemSkillId}'.");

            _activeSkillIds.Remove(skillId);
            _onModified?.Invoke();
            return McpSkillRegistry.MakeDeactivateResult(skillId);
        }
    }
}
