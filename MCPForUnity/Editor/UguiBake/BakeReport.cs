// BakeReport.cs
// UguiBake 烘焙报告系统：收集烘焙流程中的度量、警告与错误，并生成汇总摘要。
// 贯穿 parse → layout → build → bake → validate 各阶段，用于诊断与可观测性。
//
// 使用方式：
//   var report = BakeReport.Begin("Assets/Baked/MyPage.prefab", "dsl");
//   report.LogInfo("parse", "开始解析 DSL");
//   report.LogWarning("layout", "节点尺寸为 0，已自动修正");
//   report.Finish(true, 42);
//   Debug.Log(report.ToSummaryString());
//
// 线程安全实例模式（便于在静态上下文中获取当前报告）：
//   BakeReport.BeginCurrent(prefabPath, "dsl");
//   BakeReport.Current?.LogInfo("build", "构建节点树");
//   BakeReport.EndCurrent();
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>
    /// 单条烘焙日志条目：记录时间戳、级别、阶段、消息及可选耗时。
    /// </summary>
    [Serializable]
    public class BakeReportEntry
    {
        // ──────────────────── 字段 ────────────────────

        /// <summary>ISO 8601 时间戳（UTC）。</summary>
        public string Timestamp;

        /// <summary>日志级别：<c>info</c> | <c>warning</c> | <c>error</c>。</summary>
        public string Level;

        /// <summary>烘焙阶段：<c>parse</c> | <c>layout</c> | <c>build</c> | <c>bake</c> | <c>validate</c>。</summary>
        public string Stage;

        /// <summary>日志消息正文。</summary>
        public string Message;

        /// <summary>该条目记录时距烘焙开始的耗时（毫秒），用于性能分析；0 表示未计时。</summary>
        public float ElapsedMs;
    }

    /// <summary>
    /// 烘焙报告容器：贯穿单次烘焙生命周期的度量与日志集合。
    /// 通过 <see cref="Begin"/> 创建并启动计时，各阶段调用 <c>Log*</c> 记录条目，
    /// 最后调用 <see cref="Finish"/> 停止计时并落定结果。
    /// </summary>
    public class BakeReport
    {
        // ──────────────────── 静态线程安全实例 ────────────────────

        static readonly object _currentLock = new object();
        static BakeReport _current;

        /// <summary>
        /// 当前烘焙报告实例（线程安全）。
        /// 通过 <see cref="BeginCurrent"/> 创建，<see cref="EndCurrent"/> 清除。
        /// </summary>
        public static BakeReport Current
        {
            get
            {
                lock (_currentLock)
                {
                    return _current;
                }
            }
        }

        /// <summary>创建并设置当前烘焙报告实例，同时启动计时。返回新建的报告。</summary>
        public static BakeReport BeginCurrent(string prefabPath, string sourceFormat)
        {
            var report = Begin(prefabPath, sourceFormat);
            lock (_currentLock)
            {
                _current = report;
            }
            return report;
        }

        /// <summary>清除当前烘焙报告实例（线程安全）。</summary>
        public static void EndCurrent()
        {
            lock (_currentLock)
            {
                _current = null;
            }
        }

        // ──────────────────── 实例字段 ────────────────────

        /// <summary>目标预制体 Assets 相对路径（如 Assets/Baked/MyPage.prefab）。</summary>
        public string PrefabPath;

        /// <summary>源格式：<c>dsl</c> | <c>html</c> | <c>json</c>。</summary>
        public string SourceFormat;

        /// <summary>烘焙流程计时器（在 <see cref="Begin"/> 时启动，<see cref="Finish"/> 时停止）。</summary>
        Stopwatch _sw;

        /// <summary>所有日志条目（按记录顺序）。</summary>
        public List<BakeReportEntry> Entries = new List<BakeReportEntry>();

        /// <summary>节点总数（自动派生自 <see cref="NodeCount"/>，调用方无需直接设置）。</summary>
        public int TotalNodes => NodeCount;

        /// <summary>由调用方在 <see cref="Finish"/> 中设置的节点数量。</summary>
        public int NodeCount;

        /// <summary>烘焙是否成功。</summary>
        public bool Success;

        // ──────────────────── 生命周期 ────────────────────

        /// <summary>创建报告并启动计时器。</summary>
        /// <param name="prefabPath">目标预制体路径。</param>
        /// <param name="sourceFormat">源格式（dsl / html / json）。</param>
        public static BakeReport Begin(string prefabPath, string sourceFormat)
        {
            var report = new BakeReport
            {
                PrefabPath = prefabPath,
                SourceFormat = sourceFormat,
            };
            report._sw = Stopwatch.StartNew();
            return report;
        }

        /// <summary>停止计时器并落定烘焙结果。</summary>
        /// <param name="success">烘焙是否成功。</param>
        /// <param name="nodeCount">本次烘焙产出的节点数量。</param>
        public void Finish(bool success, int nodeCount)
        {
            Success = success;
            NodeCount = nodeCount;
            if (_sw != null && _sw.IsRunning)
                _sw.Stop();
        }

        // ──────────────────── 日志记录 ────────────────────

        /// <summary>添加一条日志条目（自动填充时间戳与距开始的耗时）。</summary>
        /// <param name="stage">烘焙阶段（parse / layout / build / bake / validate）。</param>
        /// <param name="level">日志级别（info / warning / error）。</param>
        /// <param name="message">消息正文。</param>
        public void Log(string stage, string level, string message)
        {
            var entry = new BakeReportEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Level = level,
                Stage = stage,
                Message = message,
                ElapsedMs = GetElapsedMs(),
            };
            Entries.Add(entry);
        }

        /// <summary>记录 info 级别日志。</summary>
        public void LogInfo(string stage, string msg) => Log(stage, "info", msg);

        /// <summary>记录 warning 级别日志，同时输出到 Unity 控制台。</summary>
        public void LogWarning(string stage, string msg)
        {
            Log(stage, "warning", msg);
            UnityEngine.Debug.LogWarning($"[UguiBake][{stage}] {msg}");
        }

        /// <summary>记录 error 级别日志，同时输出到 Unity 控制台。</summary>
        public void LogError(string stage, string msg)
        {
            Log(stage, "error", msg);
            UnityEngine.Debug.LogError($"[UguiBake][{stage}] {msg}");
        }

        // ──────────────────── 度量与汇总 ────────────────────

        /// <summary>返回烘焙总耗时（毫秒）。</summary>
        public float GetTotalElapsedMs() => GetElapsedMs();

        /// <summary>获取计时器当前累计毫秒数（计时器为空时返回 0）。</summary>
        float GetElapsedMs()
        {
            if (_sw == null) return 0f;
            return (float)_sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>统计指定级别的条目数量。</summary>
        int CountLevel(string level)
        {
            int count = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Level == level) count++;
            return count;
        }

        /// <summary>
        /// 生成多行汇总字符串：包含预制体路径、源格式、成败、节点总数、总耗时、
        /// 警告 / 错误计数，以及警告与错误明细（含时间戳）。
        /// </summary>
        public string ToSummaryString()
        {
            int warnCount = CountLevel("warning");
            int errorCount = CountLevel("error");
            var sb = new StringBuilder();

            sb.AppendLine("══════════════ UguiBake 烘焙报告 ══════════════");
            sb.AppendLine($"  预制体路径 : {PrefabPath ?? "(未指定)"}");
            sb.AppendLine($"  源格式     : {SourceFormat ?? "(未指定)"}");
            sb.AppendLine($"  结果       : {(Success ? "成功" : "失败")}");
            sb.AppendLine($"  节点总数   : {TotalNodes}");
            sb.AppendLine($"  总耗时     : {GetTotalElapsedMs():F2} ms");
            sb.AppendLine($"  警告数     : {warnCount}");
            sb.AppendLine($"  错误数     : {errorCount}");

            // 输出警告与错误明细
            if (warnCount > 0 || errorCount > 0)
            {
                sb.AppendLine("─────────────── 警告 / 错误明细 ───────────────");
                for (int i = 0; i < Entries.Count; i++)
                {
                    var e = Entries[i];
                    if (e.Level == "warning" || e.Level == "error")
                    {
                        sb.AppendLine($"  [{e.Timestamp}] {e.Level.ToUpper()} [{e.Stage}] {e.Message}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  （无警告或错误）");
            }

            sb.Append("═══════════════════════════════════════════════");
            return sb.ToString();
        }
    }
}
