// IndentDslParser.cs
// 缩进 DSL 解析器：将轻量缩进树格式转为 HtmlDomNode，复用现有 LayoutEngine + NodeBuilder。
//
// DSL 语法示例：
//   # 注释
//   div SettingsPage 942x2048 bg:#1a1f2e flex:col center
//     img bg stretch bg:#141824
//     div @topBar stretch-h bg:#0d1117 h:80 flex:center
//       text txt.Title "系统设置" color:#e0e6ed fs:36
//     div @contentLayer center flex:col gap:24 w:600
//       slider slider.Volume value:0.8 bg:#2d3748 h:36 r:18
//       toggle toggle.Fullscreen checked "全屏模式" fs:20
//       select dropdown.Quality "低画质,中画质,高画质" bg:#2d3748 h:48
//       btn btn.Save "保存设置" bg:#3182ce color:#fff fs:22 h:56 r:12
//
// 比 HTML 减少 ~65% token，布局引擎自动计算坐标。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>
    /// 缩进 DSL 解析器：轻量 UI 描述格式，复用 HtmlToUguiParser 的布局引擎。
    /// </summary>
    public static class IndentDslParser
    {
        const int IndentUnit = 2; // 每级缩进 2 空格

        // ──────────────────── 公开 API ────────────────────

        /// <summary>解析结果：UIDataNode 根节点 + 警告列表（未知类型/属性、可疑缩进等）。</summary>
        public class DslParseResult
        {
            public UIDataNode Root;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>将缩进 DSL 解析为 UIDataNode JSON（错误带行号抛出）。</summary>
        public static string Parse(string dsl, int defaultWidth = 942, int defaultHeight = 2048)
        {
            var node = ParseToNode(dsl, defaultWidth, defaultHeight);
            return JsonConvert.SerializeObject(node, Formatting.Indented);
        }

        /// <summary>将缩进 DSL 解析为 UIDataNode 对象。</summary>
        public static UIDataNode ParseToNode(string dsl, int defaultWidth = 942, int defaultHeight = 2048)
        {
            return ParseWithDiagnostics(dsl, defaultWidth, defaultHeight).Root;
        }

        /// <summary>
        /// 解析并返回警告列表。警告不阻断解析，但应回传给 AI 以便自愈
        /// （如未知类型被降级为 div、未加引号的文本被拼接等）。
        /// </summary>
        public static DslParseResult ParseWithDiagnostics(string dsl, int defaultWidth = 942, int defaultHeight = 2048)
        {
            if (string.IsNullOrWhiteSpace(dsl))
                throw new ArgumentException("DSL 内容不能为空", nameof(dsl));

            var warnings = new List<string>();

            // 1. 解析 DSL 行 → DslLine 树（自动检测缩进单位）
            var rootLine = ParseDslLines(dsl, warnings);
            if (rootLine == null)
                throw new InvalidOperationException("DSL 无有效节点行（仅包含注释或空行）");

            // 2. DslLine 树 → HtmlDomNode 树（复用现有 DOM 模型）
            var domRoot = BuildDomTree(rootLine, warnings);

            // 3. 解析根节点尺寸
            var rootStyles = CssParser.ParseInlineStyle(domRoot.GetAttribute("style", ""));
            int rootWidth = Mathf.RoundToInt(CssParser.ParseLengthPx(rootStyles, "width", defaultWidth));
            int rootHeight = Mathf.RoundToInt(CssParser.ParseLengthPx(rootStyles, "height", defaultHeight));

            // 4. 复用现有布局引擎 + 节点构建器
            var layoutRoot = LayoutEngine.Layout(domRoot, rootWidth, rootHeight);
            return new DslParseResult
            {
                Root = NodeBuilder.Build(layoutRoot, rootWidth, rootHeight),
                Warnings = warnings
            };
        }

        // ════════════════════════════════════════════════════════════════
        // DSL 行解析
        // ════════════════════════════════════════════════════════════════

        class DslLine
        {
            public int Level;
            public int LineNo;        // 源文件行号（1 起始），用于错误定位
            public string Type;       // div/img/text/btn/input/toggle/slider/dropdown/scroll
            public string Name;       // data-u-name
            public string TextContent;
            public int? RootWidth;
            public int? RootHeight;
            public string Layout;     // stretch/center/stretch-h/stretch-v/absolute
            public Dictionary<string, string> Props = new Dictionary<string, string>();
            public List<DslLine> Children = new List<DslLine>();
        }

        static DslLine ParseDslLines(string dsl, List<string> warnings)
        {
            var lines = dsl.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var nodes = new List<DslLine>();

            // 第一遍：解析行内容，记录原始缩进（空格数或 Tab 数）
            var indents = new List<int>();   // 与 nodes 一一对应
            var tabbed = new List<bool>();

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.TrimStart();

                // 空行 / 注释
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                // 统计前导空格与 Tab
                int spaces = 0, tabs = 0;
                foreach (char c in raw)
                {
                    if (c == ' ') spaces++;
                    else if (c == '\t') tabs++;
                    else break;
                }
                if (spaces > 0 && tabs > 0)
                    warnings.Add($"第 {i + 1} 行混用了 Tab 与空格缩进，建议统一（已按 Tab 处理）");

                var node = ParseSingleLine(trimmed, i + 1, warnings);
                if (node == null) continue;
                node.LineNo = i + 1;

                nodes.Add(node);
                indents.Add(tabs > 0 ? tabs : spaces);
                tabbed.Add(tabs > 0);
            }

            if (nodes.Count == 0)
                return null;

            // 第二遍：自动检测缩进单位 = 最小的非零空格缩进（默认 2）
            int unit = IndentUnit;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!tabbed[i] && indents[i] > 0 && indents[i] < unit)
                    unit = indents[i];
            }

            // 换算层级，校验整数倍
            for (int i = 0; i < nodes.Count; i++)
            {
                if (tabbed[i])
                {
                    nodes[i].Level = indents[i];
                }
                else
                {
                    if (indents[i] > 0 && indents[i] % unit != 0)
                        warnings.Add($"第 {nodes[i].LineNo} 行缩进为 {indents[i]} 空格，不是单位 {unit} 的整数倍，层级可能错位");
                    nodes[i].Level = indents[i] / unit;
                }
            }

            // 根据缩进级别构建树（含多根/跳级校验）
            return BuildTree(nodes);
        }

        static DslLine ParseSingleLine(string line, int lineNo, List<string> warnings)
        {
            var tokens = Tokenize(line);
            if (tokens.Count == 0)
                return null;

            var node = new DslLine();

            int idx = 0;

            // 第一个 token = 类型
            node.Type = tokens[idx++].ToLowerInvariant();

            // 第二个 token = 名称（可选，但推荐）
            if (idx < tokens.Count && !IsPropertyToken(tokens[idx]) && !IsSizeToken(tokens[idx]) && !IsLayoutToken(tokens[idx]) && !IsFlagToken(tokens[idx]))
            {
                node.Name = tokens[idx++];
            }

            // 剩余 tokens：尺寸 / 布局 / 属性 / 文本
            var bareTexts = new List<string>();
            while (idx < tokens.Count)
            {
                string tok = tokens[idx++];

                // 尺寸 WxH（仅根节点）
                var mSize = Regex.Match(tok, @"^(\d+)x(\d+)$", RegexOptions.IgnoreCase);
                if (mSize.Success)
                {
                    node.RootWidth = int.Parse(mSize.Groups[1].Value);
                    node.RootHeight = int.Parse(mSize.Groups[2].Value);
                    continue;
                }

                // 布局关键字
                if (IsLayoutToken(tok))
                {
                    node.Layout = tok.ToLowerInvariant();
                    continue;
                }

                // 标志位
                if (IsFlagToken(tok))
                {
                    node.Props[tok.ToLowerInvariant()] = "true";
                    continue;
                }

                // 属性 key:value
                int colon = tok.IndexOf(':');
                if (colon > 0)
                {
                    string key = tok.Substring(0, colon).ToLowerInvariant();
                    string val = tok.Substring(colon + 1);
                    node.Props[key] = val;
                    continue;
                }

                // 不含冒号的非关键字 → 文本片段（可能忘加引号）
                bareTexts.Add(tok);
            }

            if (bareTexts.Count > 0)
            {
                node.TextContent = string.Join(" ", bareTexts);
                if (bareTexts.Count > 1)
                    warnings.Add($"第 {lineNo} 行文本未加引号，已将 {bareTexts.Count} 个片段拼接为 \"{node.TextContent}\"，建议用双引号包裹");
            }

            return node;
        }

        static bool IsLayoutToken(string tok)
        {
            string t = tok.ToLowerInvariant();
            return t == "stretch" || t == "center" || t == "stretch-h" || t == "stretch-v" || t == "absolute";
        }

        static bool IsFlagToken(string tok)
        {
            string t = tok.ToLowerInvariant();
            return t == "checked" || t == "bold";
        }

        static bool IsPropertyToken(string tok)
        {
            return tok.IndexOf(':') > 0;
        }

        static bool IsSizeToken(string tok)
        {
            return Regex.IsMatch(tok, @"^\d+x\d+$", RegexOptions.IgnoreCase);
        }

        // ──────────────────── Tokenizer ────────────────────

        /// <summary>分词器：支持引号文本和 key:value 属性。</summary>
        static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuote)
                {
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        sb.Append(line[i + 1]);
                        i++;
                    }
                    else if (c == '"')
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                        inQuote = false;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuote = true;
                    }
                    else if (char.IsWhiteSpace(c))
                    {
                        if (sb.Length > 0)
                        {
                            tokens.Add(sb.ToString());
                            sb.Clear();
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        // ──────────────────── 树构建 ────────────────────

        static DslLine BuildTree(List<DslLine> nodes)
        {
            if (nodes.Count == 0) return null;

            var root = nodes[0];
            if (root.Level != 0)
                throw new InvalidOperationException($"第 {root.LineNo} 行：根节点 '{root.Type} {root.Name}' 不应有缩进（检测到 {root.Level} 级）");
            root.Level = 0;

            var stack = new Stack<DslLine>();
            stack.Push(root);

            for (int i = 1; i < nodes.Count; i++)
            {
                var node = nodes[i];

                // 多根检测：只允许一个 0 级节点
                if (node.Level == 0)
                    throw new InvalidOperationException(
                        $"第 {node.LineNo} 行：DSL 只允许一个根节点，发现第二个 0 级节点 '{node.Type} {node.Name}'" +
                        $"（首个根节点在第 {root.LineNo} 行 '{root.Type} {root.Name}'）");

                // 弹栈到比当前节点级别小的父级
                while (stack.Count > 1 && stack.Peek().Level >= node.Level)
                    stack.Pop();

                // 跳级检测：子级最多比父级深 1 层
                if (node.Level > stack.Peek().Level + 1)
                    throw new InvalidOperationException(
                        $"第 {node.LineNo} 行：缩进跳级（从第 {stack.Peek().LineNo} 行的 {stack.Peek().Level} 级直接跳到 {node.Level} 级），" +
                        "请检查缩进是否一致");

                stack.Peek().Children.Add(node);
                stack.Push(node);
            }

            return root;
        }

        // ════════════════════════════════════════════════════════════════
        // DslLine → HtmlDomNode 转换
        // ════════════════════════════════════════════════════════════════

        // DSL 类型 → (HTML tagName, data-u-type)
        static readonly Dictionary<string, (string tag, string uType)> TypeMap =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "div",     ("div", "div") },
                { "img",     ("div", "image") },
                { "image",   ("div", "image") },
                { "text",    ("div", "text") },
                { "btn",     ("button", "button") },
                { "button",  ("button", "button") },
                { "input",   ("input", "input") },
                { "toggle",  ("div", "toggle") },
                { "slider",  ("div", "slider") },
                { "select",  ("select", "dropdown") },
                { "dropdown",("select", "dropdown") },
                { "scroll",  ("div", "scroll") },
            };

        static HtmlDomNode BuildDomTree(DslLine dslLine, List<string> warnings)
        {
            if (!TypeMap.TryGetValue(dslLine.Type, out var mapping))
            {
                warnings.Add($"第 {dslLine.LineNo} 行：未知节点类型 '{dslLine.Type}'，已按 div 处理" +
                    "（支持: div/img/text/btn/input/toggle/slider/select/scroll）");
                mapping = ("div", "div");
            }

            var node = new HtmlDomNode
            {
                TagName = mapping.tag
            };

            // data-u-* 属性
            node.Attributes["data-u-type"] = mapping.uType;
            if (!string.IsNullOrEmpty(dslLine.Name))
                node.Attributes["data-u-name"] = dslLine.Name;
            if (!string.IsNullOrEmpty(dslLine.Layout))
                node.Attributes["data-u-layout"] = dslLine.Layout;

            // 构建 CSS style 字符串
            var styleParts = new List<string>();

            // 根节点尺寸
            if (dslLine.RootWidth.HasValue)
                styleParts.Add($"width: {dslLine.RootWidth.Value}px");
            if (dslLine.RootHeight.HasValue)
                styleParts.Add($"height: {dslLine.RootHeight.Value}px");

            // 属性 → CSS / data-u-*
            foreach (var kv in dslLine.Props)
            {
                string key = kv.Key;
                string val = kv.Value;

                switch (key)
                {
                    // 颜色
                    case "bg":
                        styleParts.Add($"background-color: {val}");
                        break;
                    case "color":
                        styleParts.Add($"color: {val}");
                        break;

                    // 尺寸
                    case "w":
                        styleParts.Add($"width: {CssLength(val)}");
                        break;
                    case "h":
                        styleParts.Add($"height: {CssLength(val)}");
                        break;
                    case "maxw":
                        styleParts.Add($"max-width: {CssLength(val)}");
                        break;
                    case "minw":
                        styleParts.Add($"min-width: {CssLength(val)}");
                        break;
                    case "maxh":
                        styleParts.Add($"max-height: {CssLength(val)}");
                        break;
                    case "minh":
                        styleParts.Add($"min-height: {CssLength(val)}");
                        break;

                    // 字体
                    case "fs":
                        styleParts.Add($"font-size: {val}px");
                        break;
                    case "bold":
                        if (val == "true" || val == "")
                            styleParts.Add("font-weight: bold");
                        break;
                    case "ta":
                    case "textalign":
                        styleParts.Add($"text-align: {val}");
                        break;

                    // Flex 布局
                    case "flex":
                        styleParts.Add("display: flex");
                        var flexParts = val.Split(':');
                        for (int fi = 0; fi < flexParts.Length; fi++)
                        {
                            string fp = flexParts[fi].Trim().ToLowerInvariant();
                            if (fp == "col" || fp == "column")
                                styleParts.Add("flex-direction: column");
                            else if (fp == "row")
                                styleParts.Add("flex-direction: row");
                            else if (fi >= 1 && IsAlignValue(fp))
                                styleParts.Add($"align-items: {MapAlignValue(fp)}");
                            else if (fi >= 2 && IsJustifyValue(fp))
                                styleParts.Add($"justify-content: {MapJustifyValue(fp)}");
                            // fi==1 也可能是 justify（当只有两个部分时）
                            else if (fi == 1 && IsJustifyValue(fp) && !IsAlignValue(fp))
                                styleParts.Add($"justify-content: {MapJustifyValue(fp)}");
                        }
                        break;
                    case "align":
                        if (IsAlignValue(val))
                            styleParts.Add($"align-items: {MapAlignValue(val)}");
                        break;
                    case "justify":
                        if (IsJustifyValue(val))
                            styleParts.Add($"justify-content: {MapJustifyValue(val)}");
                        break;
                    case "gap":
                        styleParts.Add($"gap: {val}px");
                        break;

                    // 圆角
                    case "r":
                        styleParts.Add($"border-radius: {val}px");
                        break;

                    // 边框
                    case "border":
                        {
                            var bp = val.Split(':');
                            if (bp.Length >= 2)
                                styleParts.Add($"border-bottom: {bp[0]}px solid {bp[1]}");
                            else if (bp.Length == 1)
                                styleParts.Add($"border: {bp[0]}px solid #000000");
                        }
                        break;

                    // 描边（data-u-outline-*）
                    case "outline":
                        {
                            var op = val.Split(':');
                            if (op.Length >= 2)
                            {
                                node.Attributes["data-u-outline-width"] = op[0];
                                node.Attributes["data-u-outline-color"] = op[1];
                            }
                        }
                        break;

                    // 内边距
                    case "pad":
                        styleParts.Add($"padding: {ConvertBoxShorthand(val)}");
                        break;

                    // 定位
                    case "pos":
                        styleParts.Add($"position: {val}");
                        break;
                    case "left":
                        styleParts.Add($"left: {CssLength(val)}");
                        break;
                    case "top":
                        styleParts.Add($"top: {CssLength(val)}");
                        break;
                    case "right":
                        styleParts.Add($"right: {CssLength(val)}");
                        break;
                    case "bottom":
                        styleParts.Add($"bottom: {CssLength(val)}");
                        break;
                    case "z":
                        styleParts.Add($"z-index: {val}");
                        break;

                    // 背景图
                    case "img":
                        styleParts.Add($"background-image: url({val})");
                        break;

                    // 渐变
                    case "grad":
                        styleParts.Add($"background-image: linear-gradient({ConvertGradient(val)})");
                        break;

                    // data-u-* 属性
                    case "dir":
                        node.Attributes["data-u-dir"] = val;
                        break;
                    case "value":
                        node.Attributes["data-u-value"] = val;
                        break;
                    case "checked":
                        if (val == "true" || val == "")
                            node.Attributes["data-u-checked"] = "true";
                        break;
                    case "export":
                        node.Attributes["data-u-export"] = val;
                        break;

                    default:
                        warnings.Add($"第 {dslLine.LineNo} 行：未知属性 '{key}'（值 '{val}' 已忽略），" +
                            "请检查拼写或参考 DSL 属性速查表");
                        break;
                }
            }

            // 设置 style 属性
            if (styleParts.Count > 0)
                node.Attributes["style"] = string.Join("; ", styleParts);

            // 文本内容处理
            if (!string.IsNullOrEmpty(dslLine.TextContent))
            {
                string uType = mapping.uType;
                if (uType == "input")
                {
                    node.Attributes["placeholder"] = dslLine.TextContent;
                }
                else if (uType == "dropdown")
                {
                    // 逗号分隔 → option 子节点
                    var options = dslLine.TextContent.Split(',');
                    foreach (var opt in options)
                    {
                        var optNode = new HtmlDomNode
                        {
                            TagName = "option",
                            Parent = node
                        };
                        optNode.Children.Add(new HtmlDomNode
                        {
                            TagName = "#text",
                            IsText = true,
                            TextContent = opt.Trim(),
                            Parent = optNode
                        });
                        node.Children.Add(optNode);
                    }
                }
                else
                {
                    // 其他类型：文本作为子节点
                    node.Children.Add(new HtmlDomNode
                    {
                        TagName = "#text",
                        IsText = true,
                        TextContent = dslLine.TextContent,
                        Parent = node
                    });
                }
            }

            // 递归构建子节点
            foreach (var child in dslLine.Children)
            {
                var childDom = BuildDomTree(child, warnings);
                childDom.Parent = node;
                node.Children.Add(childDom);
            }

            return node;
        }

        // ──────────────────── 工具方法 ────────────────────

        static string CssLength(string val)
        {
            if (val.EndsWith("%") || val.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return val;
            return val + "px";
        }

        static string ConvertBoxShorthand(string val)
        {
            var parts = val.Split(',');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(parts[i].Trim() + "px");
            }
            return sb.ToString();
        }

        static string ConvertGradient(string val)
        {
            // grad:angle:#c1,#c2,#c3  →  "165deg, #c1, #c2, #c3"
            var parts = val.Split(new[] { ':' }, 2);
            if (parts.Length < 2)
                return val;

            string angle = parts[0];
            string colors = parts[1];

            // 检查第一个部分是否是角度
            if (!float.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                // 不是数字，可能是 "to bottom" 等
                return $"{angle}, {colors.Replace(",", ", ")}";
            }

            return $"{angle}deg, {colors.Replace(",", ", ")}";
        }

        static bool IsAlignValue(string v)
        {
            return v == "center" || v == "start" || v == "end" || v == "stretch"
                || v == "flex-start" || v == "flex-end";
        }

        static bool IsJustifyValue(string v)
        {
            return v == "center" || v == "start" || v == "end" || v == "between" || v == "evenly"
                || v == "flex-start" || v == "flex-end" || v == "space-between" || v == "space-evenly";
        }

        static string MapAlignValue(string v)
        {
            return v switch
            {
                "start" => "flex-start",
                "end" => "flex-end",
                _ => v
            };
        }

        static string MapJustifyValue(string v)
        {
            return v switch
            {
                "start" => "flex-start",
                "end" => "flex-end",
                "between" => "space-between",
                "evenly" => "space-evenly",
                _ => v
            };
        }
    }
}
