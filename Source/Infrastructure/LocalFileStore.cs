using NPCLife.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using NPCLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// ICacheStore 的本地文件实现。
    /// 将缓存数据写入 mod 配置目录下的独立 JSON 文件，按存档 GUID 命名。
    /// 无存档时使用临时文件 <c>_temp.json</c>，首次保存时通过 <see cref="Rebind"/> 重命名为正式文件。
    /// </summary>
    public class LocalFileStore : ICacheStore
    {
        private const string TempFileName = "_temp.json";

        private Dictionary<string, string> _cache = new Dictionary<string, string>();
        private string _filePath;
        private bool _loaded;

        private static string CacheDirectory =>
            Path.Combine(GenFilePaths.ConfigFolderPath, "RimLife", "Cache");

        public LocalFileStore()
        {
            EnsureLoaded();
        }

        // ================================================================
        // ICacheStore
        // ================================================================

        public void Cache<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            _cache[key] = JsonParser.SerializeValue(value);
            SaveToDisk();
        }

        public T FetchCache<T>(string key, T fallback = default)
        {
            if (TryFetchCache(key, out T value)) return value;
            return fallback;
        }

        public bool TryFetchCache<T>(string key, out T value)
        {
            value = default;
            if (string.IsNullOrEmpty(key)) return false;
            EnsureLoaded();

            if (_cache.TryGetValue(key, out string json))
            {
                try
                {
                    value = JsonParser.DeserializeValue<T>(json);
                    return true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimLife.LocalFileStore] Failed to deserialize cache key '{key}': {e.Message}");
                }
            }
            return false;
        }

        public T FetchOrRebuild<T>(string key, Func<T> factory)
        {
            if (TryFetchCache(key, out T cached)) return cached;

            T result = factory();
            Cache(key, result);
            return result;
        }

        public void ClearCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            if (_cache.Remove(key))
                SaveToDisk();
        }

        public IEnumerable<string> ListCacheKeys()
        {
            EnsureLoaded();
            return _cache.Keys;
        }

        // ================================================================
        // 文件 I/O
        // ================================================================

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            string saveId = SaveIdResolver.CurrentSaveId;
            // 无存档时使用临时文件，有存档时使用 {saveId}.json
            _filePath = string.IsNullOrEmpty(saveId)
                ? Path.Combine(CacheDirectory, TempFileName)
                : Path.Combine(CacheDirectory, $"{saveId}.json");

            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _cache = JsonParser.ParseDict(json) ?? new Dictionary<string, string>();
                }
                else
                {
                    _cache = new Dictionary<string, string>();
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.LocalFileStore] Failed to load cache file '{_filePath}': {e.Message}");
                _cache = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// 重新绑定到指定存档 ID 的缓存文件。
        /// 如果当前使用临时文件且文件存在，将其重命名为正式文件。
        /// 内存数据保持不变，不触发重新加载。
        /// </summary>
        public void Rebind(string saveId)
        {
            if (string.IsNullOrEmpty(saveId)) return;

            string newPath = Path.Combine(CacheDirectory, $"{saveId}.json");

            // 如果当前是临时文件且存在，重命名为正式文件
            if (_filePath != null
                && _filePath.EndsWith(TempFileName)
                && _filePath != newPath
                && File.Exists(_filePath))
            {
                try
                {
                    // 如果目标文件已存在（理论上不应发生），先删除
                    if (File.Exists(newPath))
                        File.Delete(newPath);
                    File.Move(_filePath, newPath);
                    Log.Message($"[RimLife.LocalFileStore] Renamed temp cache to '{saveId}.json'.");
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimLife.LocalFileStore] Failed to rename temp cache: {e.Message}");
                }
            }

            _filePath = newPath;
            _loaded = true;
            // 内存数据不变，不需要重新加载
        }

        /// <summary>
        /// 清理临时缓存文件。应在游戏启动时调用。
        /// </summary>
        public static void CleanupTempCache()
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string tempPath = Path.Combine(CacheDirectory, TempFileName);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    Log.Message("[RimLife.LocalFileStore] Cleaned up temp cache file.");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.LocalFileStore] Failed to cleanup temp cache: {e.Message}");
            }
        }

        private void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_filePath)) return;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string json = JsonParser.SerializeDict(_cache);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.LocalFileStore] Failed to save cache file '{_filePath}': {e.Message}");
            }
        }

    }
}
