using System.Collections.Generic;

namespace RimLife.Skills
{
    /// <summary>
    /// Skill 模块的 manifest 数据。从 Skills/{id}/manifest.json 解析。
    /// </summary>
    public class SkillManifest
    {
        /// <summary>模块唯一 ID。</summary>
        public string Id { get; set; }

        /// <summary>模块版本号。</summary>
        public string Version { get; set; }

        /// <summary>依赖的 RimLife Skill API 版本。</summary>
        public string ApiVersion { get; set; }

        /// <summary>
        /// 入口类型。格式: "FullTypeName, AssemblyName"。
        /// 内置模块的 AssemblyName 为 "RimLife" 或 "NPCLife"。
        /// 第三方模块的 AssemblyName 对应 Skills/{id}/ 下的 DLL 文件名（不含 .dll）。
        /// </summary>
        public string EntryPoint { get; set; }

        /// <summary>依赖的其他 Skill 模块 ID 列表。</summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>manifest 文件所在的目录绝对路径（加载时填充，不在 JSON 中）。</summary>
        public string Directory { get; set; }

        /// <summary>快速校验必填字段。</summary>
        public bool IsValid(out string error)
        {
            if (string.IsNullOrEmpty(Id))
            {
                error = "manifest.id is required";
                return false;
            }
            if (string.IsNullOrEmpty(EntryPoint))
            {
                error = $"manifest.entryPoint is required (skill: {Id})";
                return false;
            }
            error = null;
            return true;
        }
    }
}
