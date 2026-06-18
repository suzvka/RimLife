using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Infrastructure.Llm
{
    /// <summary>
    /// 凭证注册表实现。管理"模型代号 → API 凭证三元组"映射。
    /// 
    /// 通过 RimLifeModSettings.LlmCredentialsJson 持久化全局配置（不绑定存档）。
    /// 
    /// 运行时 Agent 通过 TryGetCredential / GetActiveCredentials 获取凭证，
    /// UI 通过 SetAlias / RemoveAlias / SetActiveAliases 管理配置。
    /// </summary>
    public class CredentialRegistry : ICredentialRegistry
    {
        // ---- 内部状态 ----

        private readonly object _lock = new object();
        private readonly Dictionary<string, LlmCredential> _aliases
            = new Dictionary<string, LlmCredential>(StringComparer.OrdinalIgnoreCase);
        private List<string> _activeAliases = new List<string>();

        // ---- 多线程安全委托 ----

        private readonly Func<string> _serializeState;
        private readonly Action<string> _persistAction;

        /// <summary>
        /// 创建凭证注册表实例。
        /// </summary>
        /// <param name="serializeState">将当前内部状态序列化为 JSON 字符串。</param>
        /// <param name="persistAction">将 JSON 字符串持久化到存储后端。</param>
        /// <param name="initialJson">初始 JSON 状态（从存储后端加载）。</param>
        public CredentialRegistry(
            Func<string> serializeState,
            Action<string> persistAction,
            string initialJson = null)
        {
            _serializeState = serializeState ?? throw new ArgumentNullException(nameof(serializeState));
            _persistAction = persistAction ?? throw new ArgumentNullException(nameof(persistAction));

            if (!string.IsNullOrEmpty(initialJson))
            {
                DeserializeState(initialJson);
            }
        }

        // ================================================================
        // 别名管理
        // ================================================================

        public void SetAlias(string alias, LlmCredential credential)
        {
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("Alias cannot be empty.", nameof(alias));
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            lock (_lock)
            {
                _aliases[alias] = credential.Clone();
            }
            Persist();
        }

        public void RemoveAlias(string alias)
        {
            lock (_lock)
            {
                _aliases.Remove(alias);
                _activeAliases.Remove(alias);
            }
            Persist();
        }

        public bool TryGetCredential(string alias, out LlmCredential credential)
        {
            lock (_lock)
            {
                if (_aliases.TryGetValue(alias, out var found) && found.IsValid())
                {
                    credential = found.Clone();
                    return true;
                }
            }
            credential = null;
            return false;
        }

        public IReadOnlyList<string> GetAllAliases()
        {
            lock (_lock)
            {
                return _aliases.Keys.ToList();
            }
        }

        public bool HasAnyCredential
        {
            get
            {
                lock (_lock)
                {
                    return _aliases.Values.Any(c => c.IsValid());
                }
            }
        }

        // ================================================================
        // 激活顺序（fallback 链路）
        // ================================================================

        public void SetActiveAliases(IReadOnlyList<string> aliases)
        {
            lock (_lock)
            {
                // 只保留实际存在的代号，保持传入顺序
                _activeAliases = aliases
                    ?.Where(a => _aliases.ContainsKey(a))
                    .ToList() ?? new List<string>();
            }
            Persist();
        }

        public IReadOnlyList<string> GetActiveAliases()
        {
            lock (_lock)
            {
                return _activeAliases.ToList();
            }
        }

        public IReadOnlyList<LlmCredential> GetActiveCredentials()
        {
            lock (_lock)
            {
                var result = new List<LlmCredential>();
                foreach (var alias in _activeAliases)
                {
                    if (_aliases.TryGetValue(alias, out var cred) && cred.IsValid())
                    {
                        result.Add(cred.Clone());
                    }
                }
                return result;
            }
        }

        // ================================================================
        // 模型发现
        // ================================================================

        public async Task<IReadOnlyDictionary<string, string[]>> DiscoverModelsAsync(
            ILlmService llmService,
            Action<int, int, string, int> progressCallback = null,
            CancellationToken ct = default)
        {
            if (llmService == null)
                throw new ArgumentNullException(nameof(llmService));

            List<KeyValuePair<string, LlmCredential>> allCredentials;
            lock (_lock)
            {
                allCredentials = _aliases
                    .Where(kv => kv.Value.IsValid())
                    .Select(kv => new KeyValuePair<string, LlmCredential>(kv.Key, kv.Value.Clone()))
                    .ToList();
            }

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            int total = allCredentials.Count;

            for (int i = 0; i < allCredentials.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var kv = allCredentials[i];
                var alias = kv.Key;
                var credential = kv.Value;

                // 查询时不带 modelName（某些 API 需要 modelName 但列表查询不需要）
                var listCredential = credential.Clone();
                listCredential.ModelName = credential.ModelName; // 仍传，部分兼容 API 需要

                try
                {
                    string[] modelNames = await llmService.ListModelsAsync(listCredential, ct);
                    result[alias] = modelNames ?? new string[0];
                    progressCallback?.Invoke(i + 1, total, alias, modelNames?.Length ?? 0);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    result[alias] = new string[0];
                    progressCallback?.Invoke(i + 1, total, alias, -1);
                }
            }

            return result;
        }

        public void AddManualModel(string alias, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            lock (_lock)
            {
                if (!_aliases.TryGetValue(alias, out var existing))
                    return;

                // 手动添加模型：更新该代号对应凭证的 modelName
                existing.ModelName = modelName;
                _aliases[alias] = existing.Clone();
            }
            Persist();
        }

        // ================================================================
        // 持久化
        // ================================================================

        private void Persist()
        {
            try
            {
                string json;
                lock (_lock)
                {
                    json = SerializeState();
                }
                _persistAction(json);
            }
            catch
            {
                // 持久化失败不应影响运行时
            }
        }

        private string SerializeState()
        {
            var w = new JsonWriter(2048);

            // Aliases
            var aliasJsons = new List<string>();
            foreach (var kv in _aliases)
            {
                var cred = kv.Value;
                var cw = new JsonWriter(256);
                cw.Prop("alias", kv.Key);
                cw.Prop("baseUrl", cred.BaseUrl ?? "");
                cw.Prop("apiKey", cred.ApiKey ?? "");
                cw.Prop("modelName", cred.ModelName ?? "");
                cw.Prop("providerType", cred.ProviderType.ToString());
                cw.Prop("timeoutSeconds", cred.TimeoutSeconds);
                aliasJsons.Add(cw.Close());
            }
            w.ArrayRaw("aliases", aliasJsons);

            // ActiveAliases
            w.Array("activeAliases", _activeAliases);

            return w.Close();
        }

        private void DeserializeState(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}")
                return;

            try
            {
                var dict = JsonParser.ParseDict(json);

                // Aliases
                if (dict.TryGetValue("aliases", out string aliasesJson))
                {
                    var aliasDicts = JsonParser.ParseObjectArray(aliasesJson);
                    foreach (var ad in aliasDicts)
                    {
                        if (!ad.TryGetValue("alias", out string alias) || string.IsNullOrEmpty(alias))
                            continue;

                        var cred = new LlmCredential();
                        if (ad.TryGetValue("baseUrl", out string bu)) cred.BaseUrl = bu;
                        if (ad.TryGetValue("apiKey", out string ak)) cred.ApiKey = ak;
                        if (ad.TryGetValue("modelName", out string mn)) cred.ModelName = mn;
                        if (ad.TryGetValue("providerType", out string pt)
                            && Enum.TryParse<LlmProviderType>(pt, out var pType))
                            cred.ProviderType = pType;
                        if (ad.TryGetValue("timeoutSeconds", out string ts)
                            && int.TryParse(ts, out var tsVal))
                            cred.TimeoutSeconds = tsVal;

                        _aliases[alias] = cred;
                    }
                }

                // ActiveAliases
                if (dict.TryGetValue("activeAliases", out string activeJson))
                {
                    var activeList = JsonParser.ParseStringArray(activeJson);
                    if (activeList != null)
                    {
                        _activeAliases = activeList
                            .Where(a => _aliases.ContainsKey(a))
                            .ToList();
                    }
                }
            }
            catch
            {
                // 解析失败，保持空状态
            }
        }
    }
}
