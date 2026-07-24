// McpConsoleSection.cs
// Console Tab 控制器：提供命令快速测试输入框 + 最近执行日志列表。
// 命令格式：tool_name(key="value", key2=123, key3=true)
// 直接调用 CommandRegistry，不经过网络传输。

using System;
using System.Collections.Generic;
using System.Text;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows
{
    /// <summary>
    /// Console Tab：命令测试 + 执行日志。
    /// </summary>
    public class McpConsoleSection
    {
        private readonly VisualElement _root;
        private TextField _inputField;
        private TextField _outputField;
        private ListView _logListView;
        private Label _statusLabel;
        private List<McpConsoleLogEntry> _logEntries = new();

        public McpConsoleSection(VisualElement container)
        {
            _root = container;
            BuildUI();
            RefreshLog();
            McpConsoleLog.OnEntryAdded += OnLogEntryAdded;
        }

        // ──────────────────── UI 构建 ────────────────────

        private void BuildUI()
        {
            // Section 标题
            var section = new VisualElement();
            section.AddToClassList("section");

            var header = new Label("Console");
            header.AddToClassList("section-title");
            section.Add(header);

            // 输入区
            var inputLabel = new Label("命令输入（格式：tool_name(key=\"value\", ...)）");
            inputLabel.AddToClassList("field-label");
            section.Add(inputLabel);

            _inputField = new TextField();
            _inputField.multiline = true;
            _inputField.style.height = 100;
            _inputField.style.fontSize = 12;
            section.Add(_inputField);

            // 按钮行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 6;
            btnRow.style.marginBottom = 6;

            var execBtn = new Button(OnExecuteClicked) { text = "Execute" };
            execBtn.style.width = 100;
            execBtn.style.height = 28;
            btnRow.Add(execBtn);

            var clearInputBtn = new Button(() => { _inputField.value = ""; }) { text = "Clear Input" };
            clearInputBtn.style.width = 100;
            clearInputBtn.style.height = 28;
            clearInputBtn.style.marginLeft = 8;
            btnRow.Add(clearInputBtn);

            var clearLogBtn = new Button(OnClearLogClicked) { text = "Clear Log" };
            clearLogBtn.style.width = 100;
            clearLogBtn.style.height = 28;
            clearLogBtn.style.marginLeft = 8;
            btnRow.Add(clearLogBtn);

            section.Add(btnRow);

            // 状态行
            _statusLabel = new Label("");
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.color = new Color(0.75f, 0.9f, 0.75f);
            _statusLabel.style.marginBottom = 4;
            section.Add(_statusLabel);

            // 输出区
            var outputLabel = new Label("执行结果");
            outputLabel.AddToClassList("field-label");
            section.Add(outputLabel);

            _outputField = new TextField();
            _outputField.multiline = true;
            _outputField.isReadOnly = true;
            _outputField.style.height = 180;
            _outputField.style.fontSize = 11;
            _outputField.style.whiteSpace = WhiteSpace.Normal;
            section.Add(_outputField);

            // 日志区
            var logHeader = new Label("最近执行记录");
            logHeader.AddToClassList("field-label");
            logHeader.style.marginTop = 12;
            section.Add(logHeader);

            _logListView = new ListView();
            _logListView.style.height = 200;
            _logListView.fixedItemHeight = 22;
            _logListView.makeItem = () =>
            {
                var label = new Label();
                label.style.fontSize = 11;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 4;
                label.style.borderBottomWidth = 1;
                label.style.borderBottomColor = new Color(0.4f, 0.4f, 0.45f, 0.6f);
                return label;
            };
            _logListView.bindItem = (element, index) =>
            {
                if (index < 0 || index >= _logEntries.Count) return;
                var entry = _logEntries[index];
                var label = (Label)element;
                string statusIcon = entry.Status == "SUCCESS" ? "\u2713" : "\u2717";
                string time = entry.Timestamp.ToString("HH:mm:ss");
                string action = string.IsNullOrEmpty(entry.Action) ? "" : $".{entry.Action}";
                label.text = $"{statusIcon} [{time}] {entry.ToolName}{action} ({entry.ElapsedMs}ms)";
                label.style.color = entry.Status == "SUCCESS"
                    ? new Color(0, 0, 0)
                    : new Color(1f, 0.45f, 0.4f);
            };
            _logListView.itemsSource = _logEntries;
            section.Add(_logListView);

            _root.Add(section);
        }

        // ──────────────────── 命令执行 ────────────────────

        private async void OnExecuteClicked()
        {
            string input = _inputField.value?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                _statusLabel.text = "请输入命令。";
                _statusLabel.style.color = new Color(1f, 0.85f, 0.4f);
                return;
            }

            if (!TryParseCommand(input, out string commandName, out JObject parameters, out string parseError))
            {
                _statusLabel.text = $"解析失败: {parseError}";
                _statusLabel.style.color = new Color(1f, 0.55f, 0.5f);
                _outputField.value = parseError;
                return;
            }

            _statusLabel.text = $"执行中: {commandName} ...";
            _statusLabel.style.color = new Color(0.75f, 0.82f, 1f);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await CommandRegistry.InvokeCommandAsync(commandName, parameters);
                sw.Stop();

                string json = result != null
                    ? JsonConvert.SerializeObject(result, Formatting.Indented)
                    : "(null)";
                _outputField.value = json;
                _statusLabel.text = $"执行成功: {commandName} ({sw.ElapsedMilliseconds}ms)";
                _statusLabel.style.color = new Color(0.7f, 1f, 0.7f);

                // 写入内存日志
                string action = parameters?["action"]?.ToString() ?? "";
                McpConsoleLog.Log(commandName, action, "SUCCESS", sw.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                sw.Stop();
                _outputField.value = $"Error: {e.Message}\n\n{e.StackTrace}";
                _statusLabel.text = $"执行失败: {commandName} ({sw.ElapsedMilliseconds}ms)";
                _statusLabel.style.color = new Color(1f, 0.6f, 0.55f);

                string action = parameters?["action"]?.ToString() ?? "";
                McpConsoleLog.Log(commandName, action, "ERROR", sw.ElapsedMilliseconds, e.Message);
            }
        }

        // ──────────────────── 命令解析 ────────────────────

        /// <summary>
        /// 解析 tool_name(key="value", key2=123, key3=true) 格式。
        /// 支持多行、嵌套引号转义。
        /// </summary>
        private static bool TryParseCommand(string input, out string commandName,
            out JObject parameters, out string error)
        {
            commandName = null;
            parameters = new JObject();
            error = null;

            // 提取函数名：第一个 ( 之前的部分
            int parenStart = input.IndexOf('(');
            if (parenStart < 0)
            {
                // 没有括号，当作无参命令
                commandName = input.Trim();
                if (string.IsNullOrEmpty(commandName))
                {
                    error = "命令名为空。";
                    return false;
                }
                return true;
            }

            commandName = input.Substring(0, parenStart).Trim();
            if (string.IsNullOrEmpty(commandName))
            {
                error = "命令名为空。";
                return false;
            }

            // 提取括号内的参数部分
            int parenEnd = input.LastIndexOf(')');
            if (parenEnd < parenStart)
            {
                error = "缺少右括号 ')'。";
                return false;
            }

            string argsStr = input.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
            if (string.IsNullOrEmpty(argsStr))
                return true; // 无参

            // 解析 key=value 对
            // 支持：key="string", key=123, key=true, key=false, key=null
            var pairs = SplitArgs(argsStr);
            foreach (var pair in pairs)
            {
                int eqIdx = pair.IndexOf('=');
                if (eqIdx < 0)
                {
                    error = $"参数格式错误（缺少 '='）: \"{pair}\"";
                    return false;
                }

                string key = pair.Substring(0, eqIdx).Trim().Trim('"', '\'');
                string val = pair.Substring(eqIdx + 1).Trim();

                parameters[key] = ParseValue(val);
            }

            return true;
        }

        /// <summary>按顶层逗号分割参数（忽略引号内的逗号）。</summary>
        private static List<string> SplitArgs(string argsStr)
        {
            var result = new List<string>();
            int depth = 0;
            bool inQuote = false;
            char quoteChar = '"';
            var current = new StringBuilder();

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];

                if (inQuote)
                {
                    current.Append(c);
                    if (c == quoteChar && (i == 0 || argsStr[i - 1] != '\\'))
                        inQuote = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inQuote = true;
                    quoteChar = c;
                    current.Append(c);
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                    current.Append(c);
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                    current.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    var s = current.ToString().Trim();
                    if (!string.IsNullOrEmpty(s))
                        result.Add(s);
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            var last = current.ToString().Trim();
            if (!string.IsNullOrEmpty(last))
                result.Add(last);

            return result;
        }

        /// <summary>解析单个值：字符串、数字、布尔、null。</summary>
        private static JToken ParseValue(string val)
        {
            if (string.IsNullOrEmpty(val))
                return JValue.CreateNull();

            // 带引号的字符串
            if ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                (val.StartsWith("'") && val.EndsWith("'")))
            {
                string inner = val.Substring(1, val.Length - 2);
                inner = inner.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");
                return new JValue(inner);
            }

            // null
            if (val == "null" || val == "None")
                return JValue.CreateNull();

            // bool
            if (val == "true" || val == "True")
                return new JValue(true);
            if (val == "false" || val == "False")
                return new JValue(false);

            // int
            if (long.TryParse(val, out long longVal))
                return new JValue(longVal);

            // float
            if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dblVal))
                return new JValue(dblVal);

            // 兜底当字符串
            return new JValue(val);
        }

        // ──────────────────── 日志 ────────────────────

        private void OnLogEntryAdded()
        {
            // 可能在非主线程触发，延迟到主线程刷新
            EditorApplication.delayCall += RefreshLog;
        }

        private void RefreshLog()
        {
            _logEntries.Clear();
            _logEntries.AddRange(McpConsoleLog.GetEntries());
            _logListView?.RefreshItems();
        }

        private void OnClearLogClicked()
        {
            McpConsoleLog.Clear();
            RefreshLog();
            _statusLabel.text = "日志已清空。";
            _statusLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
