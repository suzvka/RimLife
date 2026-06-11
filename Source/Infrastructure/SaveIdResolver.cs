using System;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// 提供当前存档的稳定唯一标识。
    /// GUID 生成并持久化在 RimWorldSaveStore 的 WorldComponent 中，
    /// 玩家重命名存档文件不影响标识稳定性。
    /// </summary>
    public static class SaveIdResolver
    {
        private static string _currentSaveId;

        /// <summary>
        /// 当前加载存档的 GUID。存档未加载时返回 null。
        /// </summary>
        public static string CurrentSaveId => _currentSaveId;

        /// <summary>
        /// 由 RimWorldSaveStore 在 ExposeData 中调用，设置或恢复存档 GUID。
        /// </summary>
        internal static void SetSaveId(string id)
        {
            _currentSaveId = id;
        }

        /// <summary>
        /// 存档卸载时清除。
        /// </summary>
        internal static void Clear()
        {
            _currentSaveId = null;
        }
    }
}
