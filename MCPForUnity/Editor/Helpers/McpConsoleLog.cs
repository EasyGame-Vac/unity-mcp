// McpConsoleLog.cs
// 内存环形缓冲：记录最近执行的 MCP 命令，供 Console Tab 实时展示。
// 与 McpLogRecord（文件持久化）互补，本类纯内存、零 IO、窗口打开即可用。

using System;
using System.Collections.Generic;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// 单条命令执行记录。
    /// </summary>
    public struct McpConsoleLogEntry
    {
        public DateTime Timestamp;
        public string ToolName;
        public string Action;
        public string Status;   // "SUCCESS" | "ERROR"
        public long ElapsedMs;
        public string Error;
        public string ParamsSummary;
    }

    /// <summary>
    /// 线程安全的内存环形缓冲，保留最近 N 条命令记录。
    /// </summary>
    public static class McpConsoleLog
    {
        public const int MaxEntries = 50;

        private static readonly LinkedList<McpConsoleLogEntry> Buffer = new();
        private static readonly object Lock = new();

        /// <summary>新条目写入时触发（主线程）。</summary>
        public static event Action OnEntryAdded;

        public static void Log(string toolName, string action, string status,
            long elapsedMs, string error = null, string paramsSummary = null)
        {
            var entry = new McpConsoleLogEntry
            {
                Timestamp = DateTime.Now,
                ToolName = toolName ?? "unknown",
                Action = action ?? "",
                Status = status ?? "SUCCESS",
                ElapsedMs = elapsedMs,
                Error = error,
                ParamsSummary = paramsSummary,
            };

            lock (Lock)
            {
                Buffer.AddFirst(entry);
                while (Buffer.Count > MaxEntries)
                    Buffer.RemoveLast();
            }

            OnEntryAdded?.Invoke();
        }

        /// <summary>获取所有条目（最新在前）。</summary>
        public static List<McpConsoleLogEntry> GetEntries()
        {
            lock (Lock)
            {
                return new List<McpConsoleLogEntry>(Buffer);
            }
        }

        public static void Clear()
        {
            lock (Lock)
            {
                Buffer.Clear();
            }
        }
    }
}
