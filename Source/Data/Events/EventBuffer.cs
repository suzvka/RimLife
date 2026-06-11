using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife
{
    /// <summary>
    /// 事件环形缓冲区。存储最近 N 条游戏事件，供总导演 agent 查询。
    /// 线程安全：所有写入均在主线程（Harmony postfix），读取可能来自任何线程。
    /// </summary>
    public class EventBuffer
    {
        /// <summary>全局单例。</summary>
        public static readonly EventBuffer Instance = new EventBuffer();

        private readonly IGameEvent[] _buffer;
        private readonly int _capacity;
        private int _head;      // 下一个写入位置
        private int _count;     // 当前存储的事件数量
        private int _totalPushed; // 累计推入总数（用于生成唯一 ID 后缀）
        private readonly object _lock = new object();

        public EventBuffer(int capacity = 64)
        {
            _capacity = Math.Max(8, capacity);
            _buffer = new IGameEvent[_capacity];
            _head = 0;
            _count = 0;
            _totalPushed = 0;
        }

        /// <summary>
        /// 推入一条新事件。如果缓冲区已满，最旧的事件将被覆盖。
        /// 应在主线程上调用。
        /// </summary>
        public void Push(IGameEvent evt)
        {
            if (evt == null) return;

            lock (_lock)
            {
                _buffer[_head] = evt;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
                _totalPushed++;
            }

            Log.Message($"[RimLife:EventBuffer] +{evt.Category}/{evt.DefName} tick={evt.Tick}");
        }

        /// <summary>
        /// 获取自指定 tick 以来的所有事件（按时间正序）。
        /// </summary>
        public IReadOnlyList<IGameEvent> GetRecent(int sinceTick)
        {
            lock (_lock)
            {
                var result = new List<IGameEvent>();
                for (int i = 0; i < _count; i++)
                {
                    // 从最旧到最新遍历
                    int idx = (_head - _count + i + _capacity) % _capacity;
                    var evt = _buffer[idx];
                    if (evt != null && evt.Tick >= sinceTick)
                    {
                        result.Add(evt);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// 获取所有缓存的事件（按时间正序）。
        /// </summary>
        public IReadOnlyList<IGameEvent> GetAll()
        {
            return GetRecent(int.MinValue);
        }

        /// <summary>
        /// 按类别筛选事件。
        /// </summary>
        public IReadOnlyList<IGameEvent> GetByCategory(EventCategory category)
        {
            lock (_lock)
            {
                var result = new List<IGameEvent>();
                for (int i = 0; i < _count; i++)
                {
                    int idx = (_head - _count + i + _capacity) % _capacity;
                    var evt = _buffer[idx];
                    if (evt != null && evt.Category == category)
                    {
                        result.Add(evt);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// 获取最近一条事件（可能为 null）。
        /// </summary>
        public IGameEvent GetLatest()
        {
            lock (_lock)
            {
                if (_count == 0) return null;
                int lastIdx = (_head - 1 + _capacity) % _capacity;
                return _buffer[lastIdx];
            }
        }

        /// <summary>
        /// 当前缓冲区中的事件数量。
        /// </summary>
        public int Count
        {
            get { lock (_lock) return _count; }
        }

        /// <summary>
        /// 累计推入的事件总数。
        /// </summary>
        public int TotalPushed
        {
            get { lock (_lock) return _totalPushed; }
        }

        /// <summary>
        /// 清空缓冲区。
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                for (int i = 0; i < _capacity; i++)
                    _buffer[i] = null;
                _head = 0;
                _count = 0;
            }
        }
    }
}
