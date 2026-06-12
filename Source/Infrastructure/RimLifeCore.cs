using RimLife.Core;

namespace RimLife.Infrastructure
{
    /// <summary>
    /// RimLife 核心服务定位器。提供持久化存储、事件日志和交互历史的全局访问。
    /// </summary>
    public static class RimLifeCore
    {
        /// <summary>权威存储（存档文件）。由 RimWorldSaveStore 在初始化时注册。</summary>
        public static IPersistentStore SaveStore { get; internal set; }

        /// <summary>缓存存储（本地文件）。</summary>
        public static IPersistentStore CacheStore { get; } = new LocalFileStore();

        private static IEventLog _eventLog;
        private static readonly object _eventLogLock = new object();

        /// <summary>
        /// 事件日志实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static IEventLog EventLog
        {
            get
            {
                if (_eventLog == null)
                {
                    lock (_eventLogLock)
                    {
                        if (_eventLog == null && SaveStore != null)
                        {
                            _eventLog = new RimWorldEventLog(SaveStore);
                        }
                    }
                }
                return _eventLog;
            }
        }

        private static IInteractionStore _interactionStore;
        private static readonly object _interactionStoreLock = new object();

        /// <summary>
        /// 交互历史存储实例。首次访问时从 SaveStore 延迟创建。
        /// 存档未加载时返回 null。
        /// </summary>
        public static IInteractionStore InteractionStore
        {
            get
            {
                if (_interactionStore == null)
                {
                    lock (_interactionStoreLock)
                    {
                        if (_interactionStore == null && SaveStore != null)
                        {
                            _interactionStore = new InteractionHistoryStore(SaveStore);
                        }
                    }
                }
                return _interactionStore;
            }
        }
    }
}
