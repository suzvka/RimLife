using NPCLife.Framework;
using System;
using System.Collections.Generic;
using NPCLife.Core;
using RimWorld.Planet;
using Verse;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// IAuthorityStore 的存档文件实现。
    /// 通过 WorldComponent 将数据嵌入 RimWorld 存档文件。
    /// </summary>
    public class RimWorldSaveStore : WorldComponent, IAuthorityStore
    {
        private Dictionary<string, string> _data = new Dictionary<string, string>();
        private const string SaveIdKey = "__rimlife_save_guid";

        public RimWorldSaveStore(World world) : base(world)
        {
        }

        // ================================================================
        // IAuthorityStore
        // ================================================================

        public void Store<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string json = JsonParser.SerializeValue(value);
            _data[key] = json;
        }

        public T Retrieve<T>(string key, T fallback = default)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            if (_data.TryGetValue(key, out string json))
            {
                try
                {
                    return JsonParser.DeserializeValue<T>(json);
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimLife.SaveStore] Failed to deserialize key '{key}': {e.Message}");
                }
            }
            return fallback;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _data.ContainsKey(key);
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
                _data.Remove(key);
        }

        // ================================================================
        // WorldComponent 序列化
        // ================================================================

        public override void ExposeData()
        {
            base.ExposeData();

            // 存档 GUID：首次生成，后续持久化
            string saveId = _data.ContainsKey(SaveIdKey) ? _data[SaveIdKey] : null;
            if (Scribe.mode == LoadSaveMode.Saving && string.IsNullOrEmpty(saveId))
            {
                saveId = Guid.NewGuid().ToString("D");
                _data[SaveIdKey] = saveId;
            }

            // 序列化整个字典为 JSON 字符串
            string serialized = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RimLifeCore.FlushToAuthorityStore();
                serialized = JsonParser.SerializeDict(_data);
            }
            Scribe_Values.Look(ref serialized, "rimLifeData", null);

            if (Scribe.mode == LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(serialized))
            {
                _data = JsonParser.ParseDict(serialized) ?? new Dictionary<string, string>();
            }

            // 通知 SaveIdResolver
            if (_data.TryGetValue(SaveIdKey, out string resolvedId))
            {
                SaveIdResolver.SetSaveId(resolvedId);
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            // 确保 SaveIdResolver 在加载后已设置
            if (_data.TryGetValue(SaveIdKey, out string resolvedId))
            {
                SaveIdResolver.SetSaveId(resolvedId);
            }
            // 注册到核心服务定位器
            RimLifeCore.SaveStore = this;
        }

    }
}
