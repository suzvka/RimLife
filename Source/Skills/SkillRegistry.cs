using System;
using System.Collections.Generic;
using System.Linq;
using NPCLife.Core;
using NPCLife.Workspace;

namespace RimLife.Skills
{
    /// <summary>
    /// 动态技能注册表。管理所有已加载 Skill 模块的清单和角色映射。
    /// 取代 NPCLife 的硬编码 SkillCatalog，支持第三方模块的动态注册。
    /// </summary>
    public static class SkillRegistry
    {
        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly object _lock = new object();

        /// <summary>单个技能的注册信息。</summary>
        public class Entry
        {
            public string Id;
            public string Name;
            public string Description;
            public WorkspaceRole[] DefaultRoles;
            public SkillManifest Manifest;
        }

        /// <summary>所有已注册的技能清单。</summary>
        public static IReadOnlyList<Entry> AllEntries
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        /// <summary>注册一个技能。</summary>
        public static void Register(SkillManifest manifest, string name, string description, string[] targetRoles)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            lock (_lock)
            {
                // 去重：同 ID 只注册一次
                if (_entries.Any(e => string.Equals(e.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
                    return;

                var roles = targetRoles?.Select(SkillRoles.ToNpcRole).ToArray()
                            ?? Array.Empty<WorkspaceRole>();

                _entries.Add(new Entry
                {
                    Id = manifest.Id,
                    Name = name ?? manifest.Id,
                    Description = description ?? string.Empty,
                    DefaultRoles = roles,
                    Manifest = manifest
                });
            }
        }

        /// <summary>注销一个技能。</summary>
        public static void Unregister(string moduleId)
        {
            lock (_lock)
            {
                _entries.RemoveAll(e => string.Equals(e.Id, moduleId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// 获取指定角色的默认技能 ID 列表。替代 SkillCatalog.GetDefaultSkillIds()。
        /// </summary>
        public static IReadOnlyList<string> GetDefaultSkillIds(WorkspaceRole role)
        {
            lock (_lock)
            {
                return _entries
                    .Where(e => e.DefaultRoles.Contains(role))
                    .Select(e => e.Id)
                    .ToArray();
            }
        }

        /// <summary>清空注册表（Shutdown 时调用）。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }
}
