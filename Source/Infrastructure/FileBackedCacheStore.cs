using NPCLife.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using NPCLife.Core;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// ICacheStore 的单文件实现。绑定到指定文件路径，不使用 SaveIdResolver。
    /// 用于主菜单知识库选择器等场景。
    /// </summary>
    public class FileBackedCacheStore : ICacheStore
    {
        private readonly string _filePath;
        private Dictionary<string, string> _cache;
        private bool _loaded;

        public FileBackedCacheStore(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
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
                    Log.Warning($"[RimLife.FileBackedCacheStore] Failed to deserialize cache key '{key}': {e.Message}");
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
                Log.Warning($"[RimLife.FileBackedCacheStore] Failed to load cache file '{_filePath}': {e.Message}");
                _cache = new Dictionary<string, string>();
            }
        }

        private void SaveToDisk()
        {
            try
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                string json = JsonParser.SerializeDict(_cache);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception e)
            {
                Log.Warning($"[RimLife.FileBackedCacheStore] Failed to save cache file '{_filePath}': {e.Message}");
            }
        }
    }
}
