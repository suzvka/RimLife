using NPCLife.Cards;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 交互历史存储抽象接口。
    /// append-only 流水，自然膨胀不裁剪，持久化到存档文件。
    /// 语义层 KV 由上层（总导演或工作空间）按需触发计算，写入 CacheStore。
    /// </summary>
    public interface IInteractionStore
    {
        /// <summary>
        /// 追加一条交互流水记录。
        /// </summary>
        void Append(InteractionRecord record);

        /// <summary>
        /// 查询两个角色之间的交互历史。
        /// </summary>
        /// <param name="pawnIdA">角色 A 的 ID。</param>
        /// <param name="pawnIdB">角色 B 的 ID。</param>
        /// <param name="sinceTick">起始 tick（含），null 表示不限。</param>
        /// <param name="limit">最大返回数，null 表示不限。</param>
        /// <returns>按时间正序排列的交互记录。</returns>
        IReadOnlyList<InteractionRecord> Query(string pawnIdA, string pawnIdB, int? sinceTick = null, int? limit = null);

        /// <summary>
        /// 查询与指定角色相关的所有交互历史。
        /// </summary>
        /// <param name="pawnId">角色 ID。</param>
        /// <param name="sinceTick">起始 tick（含），null 表示不限。</param>
        /// <param name="limit">最大返回数，null 表示不限。</param>
        IReadOnlyList<InteractionRecord> QueryByPawn(string pawnId, int? sinceTick = null, int? limit = null);

        /// <summary>
        /// 返回两个角色之间的交互总次数。
        /// </summary>
        int Count(string pawnIdA, string pawnIdB);

        /// <summary>
        /// 累计追加的交互记录总数。
        /// </summary>
        int TotalAppended { get; }

        /// <summary>
        /// 将当前内存中的所有交互记录刷入持久化存储。
        /// 由宿主（RimLife）在 RimWorld 存档钩子中调用。
        /// </summary>
        void Persist();
    }
}
