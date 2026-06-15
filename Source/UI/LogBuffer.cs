using System;
using System.Collections.Generic;
using Verse;

namespace RimLife.UI
{
    /// <summary>
    /// 日志条目数据结构。
    /// </summary>
    public struct LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogMessageType Type { get; set; }
        
        public LogEntry(string message, LogMessageType type)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Type = type;
        }
    }

    /// <summary>
    /// 日志消息类型。
    /// </summary>
    public enum LogMessageType
    {
        Info,
        Warning,
        Error,
        Message
    }

    /// <summary>
    /// 日志缓冲区。线程安全，支持最大容量限制。
    /// </summary>
    public static class LogBuffer
    {
        private static readonly object _lock = new object();
        private static List<LogEntry> _entries = new List<LogEntry>();
        private const int MaxEntries = 500; // 最大保留条目数

        /// <summary>
        /// 添加日志条目。
        /// </summary>
        public static void Add(string message, LogMessageType type)
        {
            lock (_lock)
            {
                _entries.Add(new LogEntry(message, type));
                
                // 超出容量时移除最旧的条目
                if (_entries.Count > MaxEntries)
                {
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
                }
            }
        }

        /// <summary>
        /// 获取所有日志条目（只读副本）。
        /// </summary>
        public static List<LogEntry> GetEntries()
        {
            lock (_lock)
            {
                return new List<LogEntry>(_entries);
            }
        }

        /// <summary>
        /// 清空日志缓冲区。
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// 当前条目数量。
        /// </summary>
        public static int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }
    }

    /// <summary>
    /// 自定义日志监听器。
    /// 拦截 Verse.Log 的输出并重定向到 LogBuffer。
    /// </summary>
    public static class RimLifeLogger
    {
        /// <summary>
        /// 写入 Info 级别日志。
        /// </summary>
        public static void Info(string message)
        {
            LogBuffer.Add($"[INFO] {message}", LogMessageType.Info);
            // 同时输出到游戏标准日志（可选，用于调试）
            // Log.Message(message);
        }

        /// <summary>
        /// 写入 Warning 级别日志。
        /// </summary>
        public static void Warning(string message)
        {
            LogBuffer.Add($"[WARN] {message}", LogMessageType.Warning);
            // Log.Warning(message);
        }

        /// <summary>
        /// 写入 Error 级别日志。
        /// </summary>
        public static void Error(string message)
        {
            LogBuffer.Add($"[ERROR] {message}", LogMessageType.Error);
            // Log.Error(message);
        }

        /// <summary>
        /// 写入 Message 级别日志（普通消息）。
        /// </summary>
        public static void Message(string message)
        {
            LogBuffer.Add(message, LogMessageType.Message);
            // Log.Message(message);
        }
    }
}
