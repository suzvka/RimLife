using System;
using NPCLife.Framework;
using NPCLife.Workspace;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// Agent 角色模型选择配置。
    /// 作为模型选择的唯一真相来源，持久化到 ModSettings（全局设置，不绑定存档）。
    /// 工作空间从本配置同步模型选择，AgentLoop 运行时从工作空间读取。
    ///
    /// 每个字段存储 JSON 字符串，格式: {"cred":"凭证名","model":"模型名"}
    /// 与 WorkspaceState.CurrentModel 格式一致。
    /// </summary>
    public class AgentModelConfig
    {
        /// <summary>导演 Agent 使用的模型。</summary>
        public string DirectorModel;

        /// <summary>剧情编剧 Agent 使用的模型。</summary>
        public string ScreenwriterModel;

        /// <summary>即兴编剧 Agent 使用的模型。</summary>
        public string ImproviserModel;

        /// <summary>
        /// 获取指定角色的模型选择 JSON 字符串。
        /// 未配置时返回 null。
        /// </summary>
        public string GetModel(WorkspaceRole role)
        {
            switch (role)
            {
                case WorkspaceRole.Director: return DirectorModel;
                case WorkspaceRole.Screenwriter: return ScreenwriterModel;
                case WorkspaceRole.Improviser: return ImproviserModel;
                default: return null;
            }
        }

        /// <summary>
        /// 设置指定角色的模型选择。
        /// 传入 null 表示清除选择。
        /// </summary>
        public void SetModel(WorkspaceRole role, string modelJson)
        {
            switch (role)
            {
                case WorkspaceRole.Director: DirectorModel = modelJson; break;
                case WorkspaceRole.Screenwriter: ScreenwriterModel = modelJson; break;
                case WorkspaceRole.Improviser: ImproviserModel = modelJson; break;
            }
        }

        /// <summary>是否有任意角色已配置模型。</summary>
        public bool HasAnyModel()
        {
            return !string.IsNullOrEmpty(DirectorModel)
                || !string.IsNullOrEmpty(ScreenwriterModel)
                || !string.IsNullOrEmpty(ImproviserModel);
        }

        // ================================================================
        // 序列化 / 反序列化
        // ================================================================

        public string ToJson()
        {
            var w = new JsonWriter(256);
            if (!string.IsNullOrEmpty(DirectorModel))
                w.Prop("directorModel", DirectorModel);
            if (!string.IsNullOrEmpty(ScreenwriterModel))
                w.Prop("screenwriterModel", ScreenwriterModel);
            if (!string.IsNullOrEmpty(ImproviserModel))
                w.Prop("improviserModel", ImproviserModel);
            return w.Close();
        }

        public static AgentModelConfig FromJson(string json)
        {
            var config = CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return config;

            try
            {
                var dict = JsonParser.ParseDict(json);
                if (dict.TryGetValue("directorModel", out var dm) && !string.IsNullOrEmpty(dm))
                    config.DirectorModel = dm;
                if (dict.TryGetValue("screenwriterModel", out var sm) && !string.IsNullOrEmpty(sm))
                    config.ScreenwriterModel = sm;
                if (dict.TryGetValue("improviserModel", out var im) && !string.IsNullOrEmpty(im))
                    config.ImproviserModel = im;
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return config;
        }

        public static AgentModelConfig CreateDefault() => new AgentModelConfig();
    }
}
