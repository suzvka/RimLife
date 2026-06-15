using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimLife.Core;
using RimLife.Framework;
using RimLife.Framework.Llm;
using RimLife.Infrastructure;
using RimLife.Settings;

namespace RimLife.UI.Models
{
    /// <summary>
    /// LLM 凭证管理器。管理多张 API 凭证卡片的增删改查、模型发现、
    /// 模型选择以及在运行时的模型 fallback 循环。
    ///
    /// 通过 RimWorld ModSettings (RimLifeModSettings) 全局持久化，不绑定存档。
    /// </summary>
    public class LlmCredentialManager
    {
        private LlmCredentialState _state;
        private readonly object _lock = new object();

        // 运行时模型路由状态
        private int _currentModelIndex = 0;
        private readonly object _routeLock = new object();

        // ================================================================
        // 单例
        // ================================================================

        private static LlmCredentialManager _instance;
        public static LlmCredentialManager Instance => _instance ?? (_instance = new LlmCredentialManager());

        private LlmCredentialManager()
        {
            _state = new LlmCredentialState();
        }

        // ================================================================
        // 生命周期
        // ================================================================

        /// <summary>
        /// 初始化：从 RimLifeModSettings 加载全局持久化状态，并注入到 LlmAccessor。
        /// </summary>
        public void Initialize()
        {
            Load();
            SyncToLlmAccessor();
        }

        /// <summary>
        /// 持久化当前状态到 ModSettings（全局，不绑定存档）。
        /// </summary>
        public void Save()
        {
            var settings = RimLifeModSettings.Instance;
            if (settings == null) return;
            lock (_lock)
            {
                try
                {
                    settings.LlmCredentialsJson = SerializeState(_state);
                    settings.SaveNow();
                }
                catch
                {
                    // 持久化失败不应影响运行时
                }
            }
        }

        /// <summary>
        /// 将当前激活的模型配置同步到 LlmAccessor，确保 Agent 可以正常调用 LLM。
        /// 在凭证/模型变更时自动调用，实现前后端配置联动。
        /// </summary>
        private void SyncToLlmAccessor()
        {
            lock (_lock)
            {
                LlmConfig config = null;

                // 优先使用已选择模型列表中的第一个
                if (_state.ActiveModelOrder.Count > 0)
                {
                    config = BuildConfigForModel(_state.ActiveModelOrder[0]);
                }

                // 回退：没有模型列表时，取第一张激活卡片的默认配置
                if (config == null)
                {
                    var firstActive = _state.Cards.FirstOrDefault(c => c.IsActive && c.IsValid());
                    if (firstActive != null)
                        config = firstActive.ToLlmConfig();
                }

                if (config != null && config.IsValid())
                {
                    RimLifeCore.LlmAccessor?.UpdateConfig(config);
                }
            }
        }

        private void Load()
        {
            var settings = RimLifeModSettings.Instance;
            if (settings == null)
            {
                _state = new LlmCredentialState();
                return;
            }

            lock (_lock)
            {
                try
                {
                    string json = settings.LlmCredentialsJson;
                    if (!string.IsNullOrEmpty(json))
                        _state = DeserializeState(json) ?? new LlmCredentialState();
                    else
                        _state = new LlmCredentialState();
                }
                catch
                {
                    _state = new LlmCredentialState();
                }
            }
        }

        // ================================================================
        // 状态访问
        // ================================================================

        /// <summary>获取当前状态快照（只读意图，调用方不应直接修改）。</summary>
        public LlmCredentialState State
        {
            get { lock (_lock) return _state; }
        }

        /// <summary>所有卡片。</summary>
        public List<ApiCredentialCard> Cards
        {
            get { lock (_lock) return _state.Cards; }
        }

        /// <summary>所有已发现的模型。</summary>
        public List<ModelEntry> DiscoveredModels
        {
            get { lock (_lock) return _state.DiscoveredModels; }
        }

        /// <summary>当前激活的模型顺序列表。</summary>
        public List<string> ActiveModelOrder
        {
            get { lock (_lock) return _state.ActiveModelOrder; }
        }

        // ================================================================
        // 卡片 CRUD
        // ================================================================

        /// <summary>
        /// 添加一张新的凭证卡片。
        /// </summary>
        public ApiCredentialCard AddCard(string label, string baseUrl, string apiKey, LlmProviderType providerType = LlmProviderType.OpenAI)
        {
            var card = ApiCredentialCard.Create(label, baseUrl, apiKey, providerType);
            lock (_lock)
            {
                _state.Cards.Add(card);
            }
            Save();
            SyncToLlmAccessor();
            return card;
        }

        /// <summary>
        /// 删除指定卡片及其关联的模型条目。
        /// </summary>
        public void RemoveCard(string cardId)
        {
            lock (_lock)
            {
                _state.Cards.RemoveAll(c => c.Id == cardId);
                _state.DiscoveredModels.RemoveAll(m => m.SourceCardId == cardId);
                RebuildActiveModelOrder();
            }
            Save();
            SyncToLlmAccessor();
        }

        /// <summary>
        /// 更新一张已有卡片。
        /// </summary>
        public void UpdateCard(ApiCredentialCard updated)
        {
            lock (_lock)
            {
                for (int i = 0; i < _state.Cards.Count; i++)
                {
                    if (_state.Cards[i].Id == updated.Id)
                    {
                        _state.Cards[i] = updated;
                        break;
                    }
                }
            }
            Save();
            SyncToLlmAccessor();
        }

        /// <summary>
        /// 设置卡片的激活状态。
        /// </summary>
        public void SetCardActive(string cardId, bool isActive)
        {
            lock (_lock)
            {
                var card = _state.Cards.FirstOrDefault(c => c.Id == cardId);
                if (card != null)
                    card.IsActive = isActive;
            }
            Save();
            SyncToLlmAccessor();
        }

        // ================================================================
        // 模型发现
        // ================================================================

        /// <summary>
        /// 异步发现所有激活卡片的可用模型列表。
        /// 对每张激活卡片依次调用 ListModelsAsync(overrideConfig)，不阻塞主线程。
        /// </summary>
        /// <param name="llmService">LLM 服务实例。</param>
        /// <param name="progressCallback">进度回调：(当前卡片序号, 总卡片数, 卡片标签, 已发现模型数)。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>所有发现的模型条目。</returns>
        public async Task<List<ModelEntry>> DiscoverModelsAsync(
            ILlmService llmService,
            Action<int, int, string, int> progressCallback = null,
            CancellationToken ct = default)
        {
            if (llmService == null)
                throw new ArgumentNullException(nameof(llmService));

            List<ApiCredentialCard> activeCards;
            lock (_lock)
            {
                activeCards = _state.Cards.Where(c => c.IsActive && c.IsValid()).ToList();
            }

            var allModels = new List<ModelEntry>();

            for (int i = 0; i < activeCards.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var card = activeCards[i];

                try
                {
                    // 无状态查询：用临时 config，不影响全局配置
                    var tempConfig = card.ToLlmConfig();
                    string[] modelNames = await llmService.ListModelsAsync(tempConfig, ct);

                    foreach (var name in modelNames)
                    {
                        allModels.Add(new ModelEntry
                        {
                            ModelName = name,
                            SourceCardId = card.Id,
                            IsSelected = false,
                            DiscoveredAt = DateTime.UtcNow
                        });
                    }

                    progressCallback?.Invoke(i + 1, activeCards.Count, card.Label, modelNames.Length);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 单张卡查询失败不阻塞其他卡片
                    progressCallback?.Invoke(i + 1, activeCards.Count, card.Label, -1);
                }
            }

            // 合并到状态：替换旧条目，保留用户已勾选的模型
            lock (_lock)
            {
                // 移除旧条目
                var activeCardIds = new HashSet<string>(activeCards.Select(c => c.Id));
                _state.DiscoveredModels.RemoveAll(m => activeCardIds.Contains(m.SourceCardId));

                // 保留已有勾选状态
                var oldSelected = new HashSet<string>(
                    _state.DiscoveredModels.Where(m => m.IsSelected).Select(m => m.ModelName));

                foreach (var model in allModels)
                {
                    if (oldSelected.Contains(model.ModelName))
                        model.IsSelected = true;
                }

                _state.DiscoveredModels.AddRange(allModels);
            }
            Save();

            return allModels;
        }

        // ================================================================
        // 模型选择
        // ================================================================

        /// <summary>
        /// 设置模型是否被用户选中。
        /// </summary>
        public void SetModelSelected(string modelName, bool isSelected)
        {
            lock (_lock)
            {
                var entry = _state.DiscoveredModels.FirstOrDefault(m => m.ModelName == modelName);
                if (entry != null)
                    entry.IsSelected = isSelected;
            }
            // 不立即 Save，等 BuildActiveModelOrder 时统一持久化
        }

        /// <summary>
        /// 手动添加一个模型条目（用于 Anthropic 等不支持列表查询的 API）。
        /// </summary>
        public void AddManualModel(string modelName, string sourceCardId)
        {
            lock (_lock)
            {
                if (_state.DiscoveredModels.Any(m => m.ModelName == modelName && m.SourceCardId == sourceCardId))
                    return;

                _state.DiscoveredModels.Add(new ModelEntry
                {
                    ModelName = modelName,
                    SourceCardId = sourceCardId,
                    IsSelected = true,
                    DiscoveredAt = DateTime.UtcNow
                });
            }
            Save();
            SyncToLlmAccessor();
        }

        /// <summary>
        /// 根据用户勾选的模型重新构建使用顺序。
        /// </summary>
        public void BuildActiveModelOrder()
        {
            lock (_lock)
            {
                RebuildActiveModelOrder();
            }
            Save();
            SyncToLlmAccessor();
        }

        private void RebuildActiveModelOrder()
        {
            _state.ActiveModelOrder = _state.DiscoveredModels
                .Where(m => m.IsSelected)
                .Select(m => m.ModelName)
                .Distinct()
                .ToList();

            // 重置运行时索引
            lock (_routeLock)
            {
                _currentModelIndex = 0;
            }
        }

        // ================================================================
        // 运行时模型路由（fallback + 循环）
        // ================================================================

        /// <summary>
        /// 获取当前应使用的模型配置。
        /// 包含对应卡片的 baseUrl/key 和当前模型名。
        /// 返回 null 表示没有任何可用模型。
        /// </summary>
        public LlmConfig GetCurrentModelConfig()
        {
            lock (_lock)
            {
                lock (_routeLock)
                {
                    if (_state.ActiveModelOrder.Count == 0)
                        return null;

                    int idx = _currentModelIndex % _state.ActiveModelOrder.Count;
                    string modelName = _state.ActiveModelOrder[idx];

                    return BuildConfigForModel(modelName);
                }
            }
        }

        /// <summary>
        /// 前进到下一个模型（当前模型失败时调用）。
        /// 到达列表末尾时循环回到第一个。返回新的 LlmConfig。
        /// </summary>
        public LlmConfig AdvanceToNextModel()
        {
            lock (_lock)
            {
                lock (_routeLock)
                {
                    if (_state.ActiveModelOrder.Count == 0)
                        return null;

                    _currentModelIndex = (_currentModelIndex + 1) % _state.ActiveModelOrder.Count;
                    string modelName = _state.ActiveModelOrder[_currentModelIndex];

                    return BuildConfigForModel(modelName);
                }
            }
        }

        /// <summary>
        /// 重置模型索引到第一个。
        /// </summary>
        public void ResetModelIndex()
        {
            lock (_routeLock)
            {
                _currentModelIndex = 0;
            }
        }

        /// <summary>
        /// 获取当前模型索引（调试用）。
        /// </summary>
        public int CurrentModelIndex
        {
            get { lock (_routeLock) return _currentModelIndex; }
        }

        /// <summary>
        /// 是否有任何可用模型。
        /// </summary>
        public bool HasAnyModel
        {
            get { lock (_lock) return _state.ActiveModelOrder.Count > 0; }
        }

        private LlmConfig BuildConfigForModel(string modelName)
        {
            // 查找模型对应的来源卡片
            var modelEntry = _state.DiscoveredModels.FirstOrDefault(m => m.ModelName == modelName);
            if (modelEntry == null)
                return null;

            var card = _state.Cards.FirstOrDefault(c => c.Id == modelEntry.SourceCardId);
            if (card == null)
                return null;

            return card.ToLlmConfig(modelName);
        }

        // ================================================================
        // JSON 序列化
        // ================================================================

        private static string SerializeState(LlmCredentialState state)
        {
            if (state == null) return "{}";

            var w = new JsonWriter(2048);

            // Cards
            var cardJsons = new List<string>();
            foreach (var c in state.Cards)
            {
                var cw = new JsonWriter(256);
                cw.Prop("id", c.Id ?? "");
                cw.Prop("label", c.Label ?? "");
                cw.Prop("baseUrl", c.BaseUrl ?? "");
                cw.Prop("apiKey", c.ApiKey ?? "");
                cw.Prop("providerType", c.ProviderType.ToString());
                cw.Prop("isActive", c.IsActive ? "true" : "false");
                cw.Prop("createdAt", c.CreatedAt.ToString("o"));
                cardJsons.Add(cw.Close());
            }
            w.ArrayRaw("cards", cardJsons);

            // DiscoveredModels
            var modelJsons = new List<string>();
            foreach (var m in state.DiscoveredModels)
            {
                var mw = new JsonWriter(128);
                mw.Prop("modelName", m.ModelName ?? "");
                mw.Prop("sourceCardId", m.SourceCardId ?? "");
                mw.Prop("isSelected", m.IsSelected ? "true" : "false");
                mw.Prop("discoveredAt", m.DiscoveredAt.ToString("o"));
                modelJsons.Add(mw.Close());
            }
            w.ArrayRaw("discoveredModels", modelJsons);

            // ActiveModelOrder
            w.Array("activeModelOrder", state.ActiveModelOrder ?? new List<string>());

            return w.Close();
        }

        private static LlmCredentialState DeserializeState(string json)
        {
            var state = new LlmCredentialState();
            if (string.IsNullOrEmpty(json) || json == "{}") return state;

            try
            {
                var dict = JsonParser.ParseDict(json);

                // Cards
                if (dict.TryGetValue("cards", out string cardsJson))
                {
                    var cardDicts = JsonParser.ParseObjectArray(cardsJson);
                    foreach (var cd in cardDicts)
                    {
                        var card = new ApiCredentialCard();
                        if (cd.TryGetValue("id", out string id)) card.Id = id;
                        if (cd.TryGetValue("label", out string label)) card.Label = label;
                        if (cd.TryGetValue("baseUrl", out string baseUrl)) card.BaseUrl = baseUrl;
                        if (cd.TryGetValue("apiKey", out string apiKey)) card.ApiKey = apiKey;
                        if (cd.TryGetValue("providerType", out string pt) && Enum.TryParse<LlmProviderType>(pt, out var pType))
                            card.ProviderType = pType;
                        if (cd.TryGetValue("isActive", out string ia) && bool.TryParse(ia, out bool isActive))
                            card.IsActive = isActive;
                        if (cd.TryGetValue("createdAt", out string ca) && DateTime.TryParse(ca, out var createdAt))
                            card.CreatedAt = createdAt;
                        if (card.IsValid() || !string.IsNullOrEmpty(card.Id))
                            state.Cards.Add(card);
                    }
                }

                // DiscoveredModels
                if (dict.TryGetValue("discoveredModels", out string modelsJson))
                {
                    var modelDicts = JsonParser.ParseObjectArray(modelsJson);
                    foreach (var md in modelDicts)
                    {
                        var entry = new ModelEntry();
                        if (md.TryGetValue("modelName", out string mn)) entry.ModelName = mn;
                        if (md.TryGetValue("sourceCardId", out string sc)) entry.SourceCardId = sc;
                        if (md.TryGetValue("isSelected", out string sel) && bool.TryParse(sel, out bool isSel))
                            entry.IsSelected = isSel;
                        if (md.TryGetValue("discoveredAt", out string da) && DateTime.TryParse(da, out var discAt))
                            entry.DiscoveredAt = discAt;
                        if (!string.IsNullOrEmpty(entry.ModelName))
                            state.DiscoveredModels.Add(entry);
                    }
                }

                // ActiveModelOrder
                if (dict.TryGetValue("activeModelOrder", out string orderJson))
                {
                    var orderList = JsonParser.ParseStringArray(orderJson);
                    if (orderList != null)
                        state.ActiveModelOrder = orderList;
                }
            }
            catch
            {
                // 解析失败返回空状态
            }

            return state;
        }
    }
}
