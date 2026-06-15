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
    /// 维护两份数据：结构化条目列表（用于导出）和增量文本缓冲区（用于终端显示）。
    /// </summary>
    public static class LogBuffer
    {
        private static readonly object _lock = new object();
        private static List<LogEntry> _entries = new List<LogEntry>();
        private static readonly System.Text.StringBuilder _textBuffer = new System.Text.StringBuilder();
        private const int MaxEntries = 500; // 最大保留条目数

        /// <summary>
        /// 添加日志条目。同时追加格式化文本到增量缓冲区。
        /// </summary>
        public static void Add(string message, LogMessageType type)
        {
            lock (_lock)
            {
                _entries.Add(new LogEntry(message, type));

                // 增量追加格式化文本到显示缓冲区
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var typeTag = GetRichTypeTag(type);
                _textBuffer.AppendLine($"<color=#666666>[{timestamp}]</color> {typeTag} {message}");

                // 超出容量时移除最旧的条目
                if (_entries.Count > MaxEntries)
                {
                    var removed = _entries.Count - MaxEntries;
                    _entries.RemoveRange(0, removed);
                    // 重建文本缓冲区（无法高效地从 StringBuilder 头部删除）
                    RebuildTextBuffer();
                }
            }
        }

        /// <summary>
        /// 获取增量文本缓冲区内容（用于终端直接显示）。
        /// </summary>
        public static string GetText()
        {
            lock (_lock)
            {
                return _textBuffer.ToString();
            }
        }

        /// <summary>
        /// 获取所有日志条目（只读副本，用于导出等批量操作）。
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
                _textBuffer.Clear();
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

        private static void RebuildTextBuffer()
        {
            _textBuffer.Clear();
            foreach (var entry in _entries)
            {
                var timestamp = entry.Timestamp.ToString("HH:mm:ss");
                var typeTag = GetRichTypeTag(entry.Type);
                _textBuffer.AppendLine($"<color=#666666>[{timestamp}]</color> {typeTag} {entry.Message}");
            }
        }

        private static string GetRichTypeTag(LogMessageType type)
        {
            return type switch
            {
                LogMessageType.Info => "<color=#B0B0B0>[INFO]</color>",
                LogMessageType.Warning => "<color=#FFCC33>[WARN]</color>",
                LogMessageType.Error => "<color=#FF4D4D>[ERROR]</color>",
                LogMessageType.Message => "<color=#E0E0E0>[MSG]</color>",
                _ => ""
            };
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
            LogBuffer.Add(message, LogMessageType.Info);
        }

        /// <summary>
        /// 写入 Warning 级别日志。
        /// </summary>
        public static void Warning(string message)
        {
            LogBuffer.Add(message, LogMessageType.Warning);
        }

        /// <summary>
        /// 写入 Error 级别日志。
        /// </summary>
        public static void Error(string message)
        {
            LogBuffer.Add(message, LogMessageType.Error);
        }

        /// <summary>
        /// 写入 Message 级别日志（普通消息）。
        /// </summary>
        public static void Message(string message)
        {
            LogBuffer.Add(message, LogMessageType.Message);
        }
    }
}
