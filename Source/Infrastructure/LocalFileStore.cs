using RimLife.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using RimLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IPersistentStore 的本地文件实现。
    /// 将缓存数据写入 mod 配置目录下的独立 JSON 文件，按存档 GUID 命名。
    /// 仅实现 Cache/FetchCache/TryFetchCache/FetchOrRebuild/ClearCache（缓存）；
    /// Store 系列方法抛出 NotSupportedException。
    /// </summary>
    public class LocalFileStore : IPersistentStore
    {
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
        // IPersistentStore - 缓存
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
        // IPersistentStore - 权威存储（不支持）
        // ================================================================

        public void Store<T>(string key, T value) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public T Retrieve<T>(string key, T fallback = default) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public bool Contains(string key) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        public void Remove(string key) =>
            throw new NotSupportedException("LocalFileStore does not support authoritative storage. Use RimWorldSaveStore.");

        // ================================================================
        // 文件 I/O
        // ================================================================

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            string saveId = SaveIdResolver.CurrentSaveId;
            if (string.IsNullOrEmpty(saveId))
            {
                _cache = new Dictionary<string, string>();
                return;
            }

            _filePath = Path.Combine(CacheDirectory, $"{saveId}.json");

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

        // JSON 序列化已统一至 Framework.JsonParser
    }
}
