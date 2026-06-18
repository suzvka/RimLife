using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 事件查询参数对象。支持多维筛选与分页。
    /// TagsAny/TagsAll 提供 OR/AND 标签语义。
    /// </summary>
    public class EventQuery
    {
        /// <summary>OR 标签筛选：命中任一标签即匹配。null 表示不限。</summary>
        public IReadOnlyList<string> TagsAny;

        /// <summary>AND 标签筛选：必须包含全部标签。null 表示不限。</summary>
        public IReadOnlyList<string> TagsAll;

        /// <summary>起始 tick（含）。null 表示不限。</summary>
        public int? SinceTick;

        /// <summary>结束 tick（不含）。null 表示不限。</summary>
        public int? UntilTick;

        /// <summary>匹配参与 Actor 的 ID。null 表示不限。</summary>
        public string ActorId;

        /// <summary>最小重要度过滤。null 表示不限。</summary>
        public float? MinImportance;

        /// <summary>最大返回数。null 表示不限。</summary>
        public int? Limit;

        /// <summary>分页偏移（从 0 开始）。null 等价于 0。</summary>
        public int? Offset;

        /// <summary>创建一个匹配所有事件的查询。</summary>
        public static EventQuery All => new EventQuery();

        /// <summary>创建按标签 OR 筛选的查询。</summary>
        public static EventQuery ByAnyTag(params string[] tags) => new EventQuery { TagsAny = tags };

        /// <summary>创建按标签 AND 筛选的查询。</summary>
        public static EventQuery ByAllTags(params string[] tags) => new EventQuery { TagsAll = tags };

        /// <summary>创建按时间范围筛选的查询。</summary>
        public static EventQuery Since(int tick) => new EventQuery { SinceTick = tick };
    }
}
