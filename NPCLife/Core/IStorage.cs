using System;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 权威存储抽象接口（存档文件）。数据不可丢失，缺失视为异常。
    /// 实现：RimWorldSaveStore。
    /// </summary>
    public interface IAuthorityStore
    {
        /// <summary>写入权威数据。</summary>
        void Store<T>(string key, T value);

        /// <summary>读取权威数据。若缺失返回 fallback（数据损坏）。</summary>
        T Retrieve<T>(string key, T fallback = default);

        /// <summary>权威数据是否存在。</summary>
        bool Contains(string key);

        /// <summary>删除权威数据。</summary>
        void Remove(string key);
    }

    /// <summary>
    /// 缓存存储抽象接口（本地文件）。数据可再生，缺失属正常情况。
    /// 实现：LocalFileStore。
    /// </summary>
    public interface ICacheStore
    {
        /// <summary>写入缓存。</summary>
        void Cache<T>(string key, T value);

        /// <summary>读取缓存。若缺失返回 fallback（正常情况，调用方自行重建）。</summary>
        T FetchCache<T>(string key, T fallback = default);

        /// <summary>尝试读取缓存。返回是否命中。</summary>
        bool TryFetchCache<T>(string key, out T value);

        /// <summary>
        /// 读取缓存，若缺失则调用 factory 生成并自动缓存。
        /// 缓存读取的推荐入口——调用方只需提供重建逻辑。
        /// </summary>
        T FetchOrRebuild<T>(string key, Func<T> factory);

        /// <summary>清除指定缓存。</summary>
        void ClearCache(string key);

        /// <summary>列出所有缓存的 key。排序不保证。</summary>
        IEnumerable<string> ListCacheKeys();
    }
}
