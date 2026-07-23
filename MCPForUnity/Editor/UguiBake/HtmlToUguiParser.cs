// HtmlToUguiParser.cs
// 纯 C# HTML→UIDataNode JSON 解析器，消除浏览器依赖。
// 实现轻量 HTML DOM 解析 + 简化 CSS 布局引擎 + UIDataNode 构建。
//
// 支持的 DSL 布局模式：
//   - position: absolute（left/top/width/height 精确像素）
//   - display: flex（flex-direction: column/row, gap, align-items, justify-content）
//   - 百分比尺寸（width:100%, height:100% 相对父级）
//   - box-sizing: border-box + padding
//   - data-u-layout: stretch / center / stretch-h / stretch-v / absolute
//
// 使用方式：
//   var json = HtmlToUguiParser.Parse(htmlString);
//   var json = HtmlToUguiParser.Parse(htmlString, 720, 1440);
//   var node = HtmlToUguiParser.ParseToNode(htmlString);
//

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
    /// 纯 C# HTML→UIDataNode JSON 解析器。
    /// 替代浏览器端「HTML 转 JSON 坐标烘焙器」，实现无浏览器一键烘焙。
    /// </summary>
    public static class HtmlToUguiParser
    {
        // ──────────────────── 公开 API ────────────────────

        /// <summary>
        /// 将 UI-DSL HTML 解析为 UIDataNode JSON 字符串。
        /// </summary>
        /// <param name="html">符合 UI-DSL 规范的 HTML 字符串</param>
        /// <param name="defaultWidth">默认基准宽度（当根节点未声明 width 时使用）</param>
        /// <param name="defaultHeight">默认基准高度（当根节点未声明 height 时使用）</param>
        /// <returns>UIDataNode JSON 字符串</returns>
        public static string Parse(string html, int defaultWidth = 942, int defaultHeight = 2048)
        {
            var node = ParseToNode(html, defaultWidth, defaultHeight);
            return JsonConvert.SerializeObject(node, Formatting.Indented);
        }

        /// <summary>
        /// 将 UI-DSL HTML 解析为 UIDataNode 对象。
        /// </summary>
        public static UIDataNode ParseToNode(string html, int defaultWidth = 942, int defaultHeight = 2048)
        {
            if (string.IsNullOrWhiteSpace(html))
                throw new ArgumentException("HTML 内容不能为空", nameof(html));

            // 1. 解析 HTML DOM
            var domRoot = HtmlParser.Parse(html);

            // 2. 找到第一个 data-u-type 节点作为根
            var dslRoot = FindDslRoot(domRoot);
            if (dslRoot == null)
                throw new InvalidOperationException("未找到包含 data-u-type 的根节点");

            // 3. 解析根节点尺寸
            var rootStyles = CssParser.ParseInlineStyle(dslRoot.GetAttribute("style", ""));
            int rootWidth = Mathf.RoundToInt(CssParser.ParseLengthPx(rootStyles, "width", defaultWidth));
            int rootHeight = Mathf.RoundToInt(CssParser.ParseLengthPx(rootStyles, "height", defaultHeight));

            // 4. 布局计算
            var layoutRoot = LayoutEngine.Layout(dslRoot, rootWidth, rootHeight);

            // 5. 构建 UIDataNode
            return NodeBuilder.Build(layoutRoot, rootWidth, rootHeight);
        }

        /// <summary>
        /// 将 HTML 解析为 JSON 并直接烘焙为预制体（便捷方法）。
        /// </summary>
        public static Dictionary<string, object> ParseAndBake(
            string html,
            string prefabPath,
            int width = 942,
            int height = 2048,
            bool useTMP = true,
            string sourceHtmlPath = null)
        {
            string json = Parse(html, width, height);
            return UguiBakeBridge.Bake(json, prefabPath, width, height, useTMP, null, sourceHtmlPath);
        }

        // ──────────────────── 内部 ────────────────────

        static HtmlDomNode FindDslRoot(HtmlDomNode node)
        {
            if (!string.IsNullOrEmpty(node.GetAttribute("data-u-type")))
                return node;

            foreach (var child in node.Children)
            {
                var found = FindDslRoot(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // HTML DOM 模型与解析器
    // ════════════════════════════════════════════════════════════════

    /// <summary>轻量 HTML DOM 节点。</summary>
    public class HtmlDomNode
    {
        public string TagName;
        public string TextContent;
        public bool IsText;
        public Dictionary<string, string> Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<HtmlDomNode> Children = new List<HtmlDomNode>();
        public HtmlDomNode Parent;

        public string GetAttribute(string name, string defaultValue = "")
        {
            return Attributes.TryGetValue(name, out var val) ? val : defaultValue;
        }

        /// <summary>获取直接子文本内容（类似 innerText）。</summary>
        public string GetInnerText()
        {
            var sb = new StringBuilder();
            CollectInnerText(this, sb);
            return sb.ToString().Trim();
        }

        static void CollectInnerText(HtmlDomNode node, StringBuilder sb)
        {
            if (node.IsText)
            {
                sb.Append(node.TextContent);
                return;
            }
            foreach (var child in node.Children)
            {
                if (child.TagName == "option") continue;
                CollectInnerText(child, sb);
            }
        }
    }

    /// <summary>轻量 HTML 解析器，支持常见 HTML5 标签与自闭合标签。</summary>
    public static class HtmlParser
    {
        // 自闭合/void 元素
        static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input",
            "link", "meta", "param", "source", "track", "wbr"
        };

        public static HtmlDomNode Parse(string html)
        {
            var root = new HtmlDomNode { TagName = "#document" };
            var stack = new Stack<HtmlDomNode>();
            stack.Push(root);

            int i = 0;
            int len = html.Length;

            while (i < len)
            {
                // 查找下一个 '<'
                int lt = html.IndexOf('<', i);
                if (lt < 0)
                {
                    // 剩余全是文本
                    AppendText(stack.Peek(), html.Substring(i));
                    break;
                }

                // 标签前的文本
                if (lt > i)
                    AppendText(stack.Peek(), html.Substring(i, lt - i));

                // 注释 <!-- -->
                if (lt + 4 <= len && html[lt + 1] == '!' && html[lt + 2] == '-' && html[lt + 3] == '-')
                {
                    int end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                    i = end >= 0 ? end + 3 : len;
                    continue;
                }

                // DOCTYPE 或其他声明 <!...>
                if (lt + 1 < len && html[lt + 1] == '!')
                {
                    int end = html.IndexOf('>', lt);
                    i = end >= 0 ? end + 1 : len;
                    continue;
                }

                // 结束标签 </tag>
                if (lt + 1 < len && html[lt + 1] == '/')
                {
                    int gt = html.IndexOf('>', lt);
                    if (gt < 0) break;
                    string closeTag = html.Substring(lt + 2, gt - lt - 2).Trim().ToLowerInvariant();
                    // 弹栈到匹配标签
                    PopToTag(stack, closeTag);
                    i = gt + 1;
                    continue;
                }

                // 开始标签 <tag ...>
                int tagEnd = FindTagEnd(html, lt);
                if (tagEnd < 0) break;

                string tagContent = html.Substring(lt + 1, tagEnd - lt - 1);
                bool selfClosing = tagContent.EndsWith("/");
                if (selfClosing)
                    tagContent = tagContent.Substring(0, tagContent.Length - 1).TrimEnd();

                var (tagName, attrs) = ParseTagContent(tagContent);

                var node = new HtmlDomNode
                {
                    TagName = tagName.ToLowerInvariant(),
                    Parent = stack.Peek()
                };
                node.Attributes = attrs;

                bool isVoid = VoidElements.Contains(tagName) || selfClosing;

                stack.Peek().Children.Add(node);

                if (!isVoid)
                {
                    // script/style 标签：直接读取到结束标签
                    if (tagName.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("style", StringComparison.OrdinalIgnoreCase))
                    {
                        string closeMarker = "</" + tagName;
                        int closeIdx = html.IndexOf(closeMarker, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
                        if (closeIdx >= 0)
                        {
                            int closeGt = html.IndexOf('>', closeIdx);
                            if (closeGt >= 0)
                            {
                                node.TextContent = html.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                i = closeGt + 1;
                                continue;
                            }
                        }
                    }

                    stack.Push(node);
                }

                i = tagEnd + 1;
            }

            return root;
        }

        static int FindTagEnd(string html, int start)
        {
            // 查找 > 但要注意引号内
            bool inQuote = false;
            char quoteChar = '\0';
            for (int i = start + 1; i < html.Length; i++)
            {
                char c = html[i];
                if (inQuote)
                {
                    if (c == quoteChar)
                        inQuote = false;
                }
                else
                {
                    if (c == '"' || c == '\'')
                    {
                        inQuote = true;
                        quoteChar = c;
                    }
                    else if (c == '>')
                        return i;
                }
            }
            return -1;
        }

        static (string tagName, Dictionary<string, string> attrs) ParseTagContent(string content)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            int len = content.Length;

            // 读取标签名
            while (i < len && !char.IsWhiteSpace(content[i]))
                i++;
            string tagName = content.Substring(0, i).Trim();

            // 解析属性
            while (i < len)
            {
                // 跳过空白
                while (i < len && char.IsWhiteSpace(content[i]))
                    i++;
                if (i >= len) break;

                // 读取属性名
                int nameStart = i;
                while (i < len && content[i] != '=' && !char.IsWhiteSpace(content[i]))
                    i++;
                string attrName = content.Substring(nameStart, i - nameStart).Trim();

                if (string.IsNullOrEmpty(attrName))
                {
                    i++;
                    continue;
                }

                // 跳过空白
                while (i < len && char.IsWhiteSpace(content[i]))
                    i++;

                string attrValue = "";

                if (i < len && content[i] == '=')
                {
                    i++; // 跳过 =
                    while (i < len && char.IsWhiteSpace(content[i]))
                        i++;

                    if (i < len && (content[i] == '"' || content[i] == '\''))
                    {
                        char q = content[i];
                        i++;
                        int valStart = i;
                        while (i < len && content[i] != q)
                            i++;
                        attrValue = content.Substring(valStart, i - valStart);
                        if (i < len) i++; // 跳过引号
                    }
                    else
                    {
                        int valStart = i;
                        while (i < len && !char.IsWhiteSpace(content[i]))
                            i++;
                        attrValue = content.Substring(valStart, i - valStart);
                    }
                }

                attrs[attrName] = DecodeHtmlEntities(attrValue);
            }

            return (tagName, attrs);
        }

        static string DecodeHtmlEntities(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&#39;", "'")
                .Replace("&nbsp;", " ");
        }

        static void AppendText(HtmlDomNode parent, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            // 清理多余空白
            string cleaned = Regex.Replace(text, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(cleaned))
                return;
            parent.Children.Add(new HtmlDomNode
            {
                TagName = "#text",
                IsText = true,
                TextContent = cleaned,
                Parent = parent
            });
        }

        static void PopToTag(Stack<HtmlDomNode> stack, string tagName)
        {
            // 弹栈直到找到匹配标签（容错：未闭合标签自动修复）
            var temp = new List<HtmlDomNode>();
            while (stack.Count > 1)
            {
                var node = stack.Pop();
                temp.Add(node);
                if (node.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            // 没找到匹配，推回去
            for (int i = temp.Count - 1; i >= 0; i--)
                stack.Push(temp[i]);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // CSS 内联样式解析
    // ════════════════════════════════════════════════════════════════

    /// <summary>CSS 内联样式解析与工具方法。</summary>
    public static class CssParser
    {
        /// <summary>解析 style="..." 属性为字典。</summary>
        public static Dictionary<string, string> ParseInlineStyle(string style)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(style))
                return result;

            var declarations = style.Split(';');
            foreach (var decl in declarations)
            {
                int colon = decl.IndexOf(':');
                if (colon < 0) continue;
                string prop = decl.Substring(0, colon).Trim().ToLowerInvariant();
                string val = decl.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(prop) && !string.IsNullOrEmpty(val))
                    result[prop] = val;
            }
            return result;
        }

        /// <summary>解析 CSS 长度值（px 或纯数字）。</summary>
        public static float ParseLength(Dictionary<string, string> styles, string property, float defaultValue = 0)
        {
            if (!styles.TryGetValue(property, out var val))
                return defaultValue;
            return ParseLengthValue(val, defaultValue);
        }

        public static float ParseLengthPx(Dictionary<string, string> styles, string property, float defaultValue = 0)
        {
            return ParseLength(styles, property, defaultValue);
        }

        public static float ParseLengthValue(string val, float defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(val))
                return defaultValue;
            val = val.Trim();

            // px
            var mPx = Regex.Match(val, @"^([\d.]+)px$", RegexOptions.IgnoreCase);
            if (mPx.Success)
                return float.Parse(mPx.Groups[1].Value, CultureInfo.InvariantCulture);

            // 纯数字
            var mNum = Regex.Match(val, @"^([\d.]+)$");
            if (mNum.Success)
                return float.Parse(mNum.Groups[1].Value, CultureInfo.InvariantCulture);

            return defaultValue;
        }

        /// <summary>解析百分比（返回 0~1 或 -1 表示非百分比）。</summary>
        public static float ParsePercent(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return -1;
            val = val.Trim();
            var m = Regex.Match(val, @"^([\d.]+)%$");
            if (m.Success)
                return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f;
            return -1;
        }

        /// <summary>解析颜色值（#RRGGBB, #RRGGBBAA, rgba()）为 #RRGGBBAA 格式。</summary>
        public static string ParseColor(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return "#FFFFFF00";
            val = val.Trim();

            // transparent / none
            if (val.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("none", StringComparison.OrdinalIgnoreCase))
                return "#FFFFFF00";

            // rgba(r,g,b,a) / rgb(r,g,b)
            var mRgba = Regex.Match(val, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)$", RegexOptions.IgnoreCase);
            if (mRgba.Success)
            {
                int r = int.Parse(mRgba.Groups[1].Value);
                int g = int.Parse(mRgba.Groups[2].Value);
                int b = int.Parse(mRgba.Groups[3].Value);
                int a = 255;
                if (mRgba.Groups[4].Success)
                    a = Mathf.RoundToInt(float.Parse(mRgba.Groups[4].Value, CultureInfo.InvariantCulture) * 255);
                return $"#{r:X2}{g:X2}{b:X2}" + (a < 255 ? $"{a:X2}" : "");
            }

            // #RGB / #RGBA / #RRGGBB / #RRGGBBAA
            var mHex = Regex.Match(val, @"^#([0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.IgnoreCase);
            if (mHex.Success)
            {
                string hex = mHex.Groups[1].Value;
                if (hex.Length == 3)
                    return "#" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
                if (hex.Length == 4)
                    return "#" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
                return "#" + hex;
            }

            // 常见颜色名
            return val.ToLowerInvariant() switch
            {
                "white" => "#FFFFFF",
                "black" => "#000000",
                "red" => "#FF0000",
                "green" => "#008000",
                "blue" => "#0000FF",
                "yellow" => "#FFFF00",
                "gray" or "grey" => "#808080",
                _ => "#FFFFFF00",
            };
        }

        /// <summary>解析 background-color（处理 rgba(0,0,0,0) → transparent）。</summary>
        public static string ParseBackgroundColor(Dictionary<string, string> styles)
        {
            if (!styles.TryGetValue("background-color", out var val))
            {
                // 检查 background 简写
                if (styles.TryGetValue("background", out var bg))
                {
                    // 尝试从 background 简写中提取颜色
                    val = ExtractColorFromBackground(bg);
                }
                else
                    return "#FFFFFF00";
            }

            if (val.Equals("rgba(0, 0, 0, 0)", StringComparison.OrdinalIgnoreCase) ||
                val.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return "#FFFFFF00";

            return ParseColor(val);
        }

        static string ExtractColorFromBackground(string bg)
        {
            // 简化处理：如果是纯色背景
            var mRgba = Regex.Match(bg, @"rgba?\([^)]+\)");
            if (mRgba.Success)
                return mRgba.Value;
            var mHex = Regex.Match(bg, @"#[0-9a-fA-F]{3,8}\b");
            if (mHex.Success)
                return mHex.Value;
            return "transparent";
        }

        /// <summary>解析 linear-gradient 为 UIDataLinearGradient。</summary>
        public static UIDataLinearGradient ParseLinearGradient(Dictionary<string, string> styles)
        {
            string bgImage;
            if (!styles.TryGetValue("background-image", out bgImage))
            {
                // 尝试从 background 简写提取
                if (!styles.TryGetValue("background", out var bg))
                    return null;
                bgImage = ExtractGradientFromBackground(bg);
            }

            if (string.IsNullOrWhiteSpace(bgImage) || bgImage == "none")
                return null;

            return ParseLinearGradientString(bgImage);
        }

        static string ExtractGradientFromBackground(string bg)
        {
            int idx = bg.IndexOf("linear-gradient", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "none";
            int open = bg.IndexOf('(', idx);
            if (open < 0) return "none";
            int depth = 0;
            for (int i = open; i < bg.Length; i++)
            {
                if (bg[i] == '(') depth++;
                else if (bg[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return bg.Substring(idx, i - idx + 1);
                }
            }
            return "none";
        }

        public static UIDataLinearGradient ParseLinearGradientString(string bgImage)
        {
            int idx = bgImage.IndexOf("linear-gradient", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int open = bgImage.IndexOf('(', idx);
            if (open < 0) return null;

            // 提取括号内容
            int depth = 0;
            int end = -1;
            for (int i = open; i < bgImage.Length; i++)
            {
                if (bgImage[i] == '(') depth++;
                else if (bgImage[i] == ')')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }
            if (end < 0) return null;

            string inner = bgImage.Substring(open + 1, end - open - 1);
            var parts = SplitTopLevelArgs(inner);
            if (parts.Count < 2) return null;

            float angle = 180f;
            int startIdx = 0;

            // 检查第一个参数是角度还是颜色
            var angleVal = ParseGradientAngle(parts[0]);
            var firstColor = TryParseColorToken(parts[0]);
            if (angleVal.HasValue && firstColor == null)
            {
                angle = angleVal.Value;
                startIdx = 1;
            }

            var colors = new List<string>();
            var positions = new List<float?>();

            for (int i = startIdx; i < parts.Count; i++)
            {
                string seg = parts[i].Trim();
                float? pos = null;

                // 提取百分比位置
                var mPct = Regex.Match(seg, @"(\d+(?:\.\d+)?)%\s*$");
                if (mPct.Success)
                {
                    pos = float.Parse(mPct.Groups[1].Value, CultureInfo.InvariantCulture) / 100f;
                    seg = seg.Substring(0, mPct.Index).Trim();
                }

                var col = TryParseColorToken(seg);
                if (col == null) continue;
                colors.Add(col);
                positions.Add(pos);
            }

            if (colors.Count < 2) return null;

            var result = new UIDataLinearGradient
            {
                angle = angle,
                colors = colors
            };

            // 位置处理
            bool allAuto = true;
            foreach (var p in positions)
                if (p.HasValue) { allAuto = false; break; }

            if (!allAuto)
            {
                result.positions = new List<float>();
                for (int i = 0; i < positions.Count; i++)
                {
                    result.positions.Add(positions[i].HasValue
                        ? positions[i].Value
                        : (float)i / Mathf.Max(1, positions.Count - 1));
                }
            }

            return result;
        }

        static float? ParseGradientAngle(string part)
        {
            part = part.Trim();
            var mDeg = Regex.Match(part, @"(-?\d+(?:\.\d+)?)deg", RegexOptions.IgnoreCase);
            if (mDeg.Success)
                return float.Parse(mDeg.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Regex.IsMatch(part, @"^to\s+top", RegexOptions.IgnoreCase)) return 0;
            if (Regex.IsMatch(part, @"^to\s+bottom", RegexOptions.IgnoreCase)) return 180;
            if (Regex.IsMatch(part, @"^to\s+right", RegexOptions.IgnoreCase)) return 90;
            if (Regex.IsMatch(part, @"^to\s+left", RegexOptions.IgnoreCase)) return 270;
            return null;
        }

        static string TryParseColorToken(string token)
        {
            token = token.Trim();
            if (string.IsNullOrEmpty(token)) return null;
            var result = ParseColor(token);
            return result == "#FFFFFF00" && !token.Equals("transparent", StringComparison.OrdinalIgnoreCase) ? null : result;
        }

        static List<string> SplitTopLevelArgs(string s)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(s.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            parts.Add(s.Substring(start).Trim());
            return parts;
        }

        /// <summary>解析 border-radius。</summary>
        public static List<float> ParseBorderRadius(Dictionary<string, string> styles)
        {
            float tl = ParseLength(styles, "border-top-left-radius", 0);
            float tr = ParseLength(styles, "border-top-right-radius", 0);
            float br = ParseLength(styles, "border-bottom-right-radius", 0);
            float bl = ParseLength(styles, "border-bottom-left-radius", 0);

            if (tl == 0 && tr == 0 && br == 0 && bl == 0)
            {
                if (styles.TryGetValue("border-radius", out var val))
                {
                    var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var nums = new List<float>();
                    foreach (var p in parts)
                        nums.Add(ParseLengthValue(p, 0));

                    if (nums.Count == 1) { tl = tr = br = bl = nums[0]; }
                    else if (nums.Count == 2) { tl = br = nums[0]; tr = bl = nums[1]; }
                    else if (nums.Count == 3) { tl = nums[0]; tr = bl = nums[1]; br = nums[2]; }
                    else if (nums.Count >= 4) { tl = nums[0]; tr = nums[1]; br = nums[2]; bl = nums[3]; }
                }
            }

            if (tl == 0 && tr == 0 && br == 0 && bl == 0)
                return null;

            return new List<float> { tl, tr, br, bl };
        }

        /// <summary>解析 CSS border。</summary>
        public static UIDataCssBorder ParseCssBorder(Dictionary<string, string> styles)
        {
            // 简写 border: width style color
            if (styles.TryGetValue("border", out var borderShorthand))
            {
                return ParseBorderShorthand(borderShorthand);
            }

            // 分写
            if (styles.TryGetValue("border-bottom", out var bottomShorthand))
            {
                var parsed = ParseBorderShorthand(bottomShorthand);
                if (parsed != null) return parsed;
            }

            // 各边属性
            float wt = ParseLength(styles, "border-top-width", 0);
            float wb = ParseLength(styles, "border-bottom-width", 0);
            float wl = ParseLength(styles, "border-left-width", 0);
            float wr = ParseLength(styles, "border-right-width", 0);
            float w = Mathf.Max(wt, wb, wl, wr);

            if (w <= 0) return null;

            string color = "#000000";
            if (styles.TryGetValue("border-bottom-color", out var bc))
                color = ParseColor(bc);
            else if (styles.TryGetValue("border-color", out var bcol))
                color = ParseColor(bcol);

            if (color == "#FFFFFF00") return null;

            return new UIDataCssBorder { width = w, color = color };
        }

        static UIDataCssBorder ParseBorderShorthand(string val)
        {
            var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float width = 0;
            string color = "#000000";
            bool hasStyle = false;

            foreach (var p in parts)
            {
                var lower = p.ToLowerInvariant();
                if (lower == "solid" || lower == "dashed" || lower == "dotted" || lower == "double")
                    hasStyle = true;
                else if (lower == "none" || lower == "hidden")
                    return null;
                else
                {
                    var w = ParseLengthValue(p, -1);
                    if (w >= 0)
                        width = w;
                    else
                    {
                        var c = ParseColor(p);
                        if (c != "#FFFFFF00")
                            color = c;
                    }
                }
            }

            if (width <= 0) return null;
            return new UIDataCssBorder { width = width, color = color };
        }

        /// <summary>解析 data-u-outline-* 属性或 CSS outline。</summary>
        public static UIDataOutline ParseOutline(HtmlDomNode element, Dictionary<string, string> styles)
        {
            var wRaw = element.GetAttribute("data-u-outline-width");
            var cRaw = element.GetAttribute("data-u-outline-color");

            if (!string.IsNullOrWhiteSpace(wRaw) && !string.IsNullOrWhiteSpace(cRaw))
            {
                float w = ParseLengthValue(wRaw.Replace("px", "").Trim(), 0);
                var col = ParseColor(cRaw.Trim());
                if (w > 0 && col != "#FFFFFF00")
                    return new UIDataOutline { width = w, color = col };
            }

            if (!styles.TryGetValue("outline-style", out var os) ||
                os == "none" || os == "hidden")
                return null;

            float ow = ParseLength(styles, "outline-width", 0);
            if (ow <= 0) return null;

            string oc = styles.TryGetValue("outline-color", out var ocVal) ? ParseColor(ocVal) : "#000000";
            if (oc == "#FFFFFF00") return null;

            return new UIDataOutline { width = ow, color = oc };
        }

        /// <summary>从 background-image 提取 url() 图片路径。</summary>
        public static string ExtractImageUrl(Dictionary<string, string> styles)
        {
            string bgImage;
            if (!styles.TryGetValue("background-image", out bgImage))
            {
                if (!styles.TryGetValue("background", out var bg))
                    return null;
                // 从 background 简写提取 url()
                bgImage = bg;
            }

            if (string.IsNullOrEmpty(bgImage) || bgImage == "none")
                return null;

            var m = Regex.Match(bgImage, @"url\(['""]?([^'"")]+)['""]?\)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;

            string url = m.Groups[1].Value.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;

            return url;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 布局引擎
    // ════════════════════════════════════════════════════════════════

    /// <summary>布局后的节点信息（含计算后的位置和尺寸）。</summary>
    public class LayoutNode
    {
        public HtmlDomNode DomNode;
        public float X, Y, Width, Height;
        public Dictionary<string, string> Styles;
        public List<LayoutNode> Children = new List<LayoutNode>();
        public bool IsDslNode; // 是否有 data-u-type
    }

    /// <summary>
    /// 简化 CSS 布局引擎，支持 absolute 定位、flex 布局、百分比尺寸。
    /// 覆盖 UI-DSL 常见布局模式。
    /// </summary>
    public static class LayoutEngine
    {
        public static LayoutNode Layout(HtmlDomNode dslRoot, int rootWidth, int rootHeight)
        {
            var root = BuildLayoutTree(dslRoot);
            root.X = 0;
            root.Y = 0;
            root.Width = rootWidth;
            root.Height = rootHeight;
            LayoutChildren(root);
            return root;
        }

        static LayoutNode BuildLayoutTree(HtmlDomNode domNode)
        {
            var styles = CssParser.ParseInlineStyle(domNode.GetAttribute("style", ""));
            var node = new LayoutNode
            {
                DomNode = domNode,
                Styles = styles,
                IsDslNode = !string.IsNullOrEmpty(domNode.GetAttribute("data-u-type")),
            };

            foreach (var child in domNode.Children)
            {
                if (child.IsText) continue;
                if (child.TagName == "option") continue;
                if (child.TagName == "#comment") continue;
                node.Children.Add(BuildLayoutTree(child));
            }

            return node;
        }

        static void LayoutChildren(LayoutNode parent)
        {
            string position = parent.Styles.TryGetValue("position", out var pos) ? pos.ToLowerInvariant() : "static";
            string display = parent.Styles.TryGetValue("display", out var disp) ? disp.ToLowerInvariant() : "block";
            bool isFlex = display.Contains("flex");

            // 解析父级 padding
            float padTop = CssParser.ParseLength(parent.Styles, "padding-top", 0);
            float padBottom = CssParser.ParseLength(parent.Styles, "padding-bottom", 0);
            float padLeft = CssParser.ParseLength(parent.Styles, "padding-left", 0);
            float padRight = CssParser.ParseLength(parent.Styles, "padding-right", 0);
            // padding 简写
            ParsePaddingShorthand(parent.Styles, ref padTop, ref padRight, ref padBottom, ref padLeft);

            float contentX = parent.X + padLeft;
            float contentY = parent.Y + padTop;
            float contentWidth = parent.Width - padLeft - padRight;
            float contentHeight = parent.Height - padTop - padBottom;

            // box-sizing: border-box 时，尺寸已含 padding（默认处理方式）

            // 分离绝对定位和流式子节点
            var absChildren = new List<LayoutNode>();
            var flowChildren = new List<LayoutNode>();

            foreach (var child in parent.Children)
            {
                string childPos = child.Styles.TryGetValue("position", out var cp) ? cp.ToLowerInvariant() : "static";
                if (childPos == "absolute" || childPos == "fixed")
                    absChildren.Add(child);
                else
                    flowChildren.Add(child);
            }

            // 布局流式子节点
            if (isFlex)
                LayoutFlex(parent, flowChildren, contentX, contentY, contentWidth, contentHeight);
            else
                LayoutBlock(flowChildren, contentX, contentY, contentWidth);

            // 布局绝对定位子节点
            foreach (var child in absChildren)
                LayoutAbsolute(child, parent, contentX, contentY, contentWidth, contentHeight);

            // 递归
            foreach (var child in parent.Children)
                LayoutChildren(child);
        }

        static void ParsePaddingShorthand(Dictionary<string, string> styles,
            ref float top, ref float right, ref float bottom, ref float left)
        {
            if (!styles.TryGetValue("padding", out var val))
                return;
            var parts = val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<float>();
            foreach (var p in parts)
                nums.Add(CssParser.ParseLengthValue(p, 0));

            if (nums.Count == 1) { top = right = bottom = left = nums[0]; }
            else if (nums.Count == 2) { top = bottom = nums[0]; right = left = nums[1]; }
            else if (nums.Count == 3) { top = nums[0]; right = left = nums[1]; bottom = nums[2]; }
            else if (nums.Count >= 4) { top = nums[0]; right = nums[1]; bottom = nums[2]; left = nums[3]; }
        }

        static void LayoutBlock(List<LayoutNode> children, float x, float y, float contentWidth)
        {
            float currentY = y;
            foreach (var child in children)
            {
                // 解析子节点尺寸
                ResolveSize(child, contentWidth, 0);

                // margin
                float marginTop = CssParser.ParseLength(child.Styles, "margin-top", 0);
                currentY += marginTop;

                child.X = x + CssParser.ParseLength(child.Styles, "margin-left", 0);
                child.Y = currentY;

                currentY += child.Height + CssParser.ParseLength(child.Styles, "margin-bottom", 0);
            }
        }

        static void LayoutFlex(LayoutNode parent, List<LayoutNode> children,
            float x, float y, float contentWidth, float contentHeight)
        {
            if (children.Count == 0) return;

            string direction = "row";
            if (parent.Styles.TryGetValue("flex-direction", out var fd))
                direction = fd.ToLowerInvariant().Trim();

            bool isColumn = direction.Contains("column");

            float gap = CssParser.ParseLength(parent.Styles, "gap", 0);
            // 也检查 row-gap / column-gap
            if (gap == 0)
            {
                if (isColumn)
                    gap = CssParser.ParseLength(parent.Styles, "row-gap", 0);
                else
                    gap = CssParser.ParseLength(parent.Styles, "column-gap", 0);
            }

            string alignItems = "stretch";
            if (parent.Styles.TryGetValue("align-items", out var ai))
                alignItems = ai.ToLowerInvariant().Trim();

            string justifyContent = "flex-start";
            if (parent.Styles.TryGetValue("justify-content", out var jc))
                justifyContent = jc.ToLowerInvariant().Trim();

            if (isColumn)
                LayoutFlexColumn(children, x, y, contentWidth, contentHeight, gap, alignItems, justifyContent);
            else
                LayoutFlexRow(children, x, y, contentWidth, contentHeight, gap, alignItems, justifyContent);
        }

        static void LayoutFlexColumn(List<LayoutNode> children, float x, float y,
            float contentWidth, float contentHeight, float gap,
            string alignItems, string justifyContent)
        {
            // 第一遍：测量所有子节点的高度
            float totalHeight = 0;
            var sizes = new float[children.Count, 2]; // [i, 0]=width, [i, 1]=height

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                ResolveSize(child, contentWidth, contentHeight);

                // flex: 1 的子节点先记录，稍后分配剩余空间
                sizes[i, 0] = child.Width;
                sizes[i, 1] = child.Height;
                totalHeight += child.Height;
            }

            totalHeight += gap * (children.Count - 1);

            // 计算起始 Y（justify-content）
            float startY = y;
            float freeSpace = contentHeight - totalHeight;

            if (freeSpace > 0)
            {
                switch (justifyContent)
                {
                    case "center":
                        startY = y + freeSpace / 2;
                        break;
                    case "flex-end":
                    case "end":
                        startY = y + freeSpace;
                        break;
                    case "space-between":
                        // 后处理
                        break;
                    case "space-evenly":
                        startY = y + freeSpace / (children.Count + 1);
                        break;
                }
            }

            float currentY = startY;
            float extraGap = 0;

            if (justifyContent == "space-between" && children.Count > 1 && freeSpace > 0)
                extraGap = freeSpace / (children.Count - 1);
            else if (justifyContent == "space-evenly" && children.Count > 0 && freeSpace > 0)
                extraGap = 0; // 已在 startY 处理

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                child.Y = currentY;

                // align-items（交叉轴 = 水平方向）
                float childWidth = sizes[i, 0];
                switch (alignItems)
                {
                    case "center":
                        child.X = x + (contentWidth - childWidth) / 2;
                        break;
                    case "flex-end":
                    case "end":
                        child.X = x + contentWidth - childWidth;
                        break;
                    case "stretch":
                        child.X = x;
                        child.Width = contentWidth;
                        break;
                    default: // flex-start
                        child.X = x;
                        break;
                }

                currentY += child.Height + gap + extraGap;

                if (justifyContent == "space-evenly")
                    currentY += freeSpace / (children.Count + 1);
            }
        }

        static void LayoutFlexRow(List<LayoutNode> children, float x, float y,
            float contentWidth, float contentHeight, float gap,
            string alignItems, string justifyContent)
        {
            float totalWidth = 0;
            var sizes = new float[children.Count, 2];

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                ResolveSize(child, contentWidth, contentHeight);
                sizes[i, 0] = child.Width;
                sizes[i, 1] = child.Height;
                totalWidth += child.Width;
            }

            totalWidth += gap * (children.Count - 1);

            float startX = x;
            float freeSpace = contentWidth - totalWidth;

            if (freeSpace > 0)
            {
                switch (justifyContent)
                {
                    case "center":
                        startX = x + freeSpace / 2;
                        break;
                    case "flex-end":
                    case "end":
                        startX = x + freeSpace;
                        break;
                    case "space-between":
                        break;
                    case "space-evenly":
                        startX = x + freeSpace / (children.Count + 1);
                        break;
                }
            }

            float currentX = startX;
            float extraGap = 0;

            if (justifyContent == "space-between" && children.Count > 1 && freeSpace > 0)
                extraGap = freeSpace / (children.Count - 1);

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                child.X = currentX;

                float childHeight = sizes[i, 1];
                switch (alignItems)
                {
                    case "center":
                        child.Y = y + (contentHeight - childHeight) / 2;
                        break;
                    case "flex-end":
                    case "end":
                        child.Y = y + contentHeight - childHeight;
                        break;
                    case "stretch":
                        child.Y = y;
                        child.Height = contentHeight;
                        break;
                    default:
                        child.Y = y;
                        break;
                }

                currentX += child.Width + gap + extraGap;

                if (justifyContent == "space-evenly")
                    currentX += freeSpace / (children.Count + 1);
            }
        }

        static void LayoutAbsolute(LayoutNode child, LayoutNode parent,
            float contentX, float contentY, float contentWidth, float contentHeight)
        {
            // 相对于父级 padding box
            float left = CssParser.ParseLength(child.Styles, "left", -1);
            float top = CssParser.ParseLength(child.Styles, "top", -1);
            float right = CssParser.ParseLength(child.Styles, "right", -1);
            float bottom = CssParser.ParseLength(child.Styles, "bottom", -1);

            // 尺寸
            ResolveSize(child, contentWidth, contentHeight);

            // 百分比 left/top
            if (left < 0)
            {
                string leftStr = child.Styles.TryGetValue("left", out var lv) ? lv : "";
                var leftPct = CssParser.ParsePercent(leftStr);
                if (leftPct >= 0)
                    left = leftPct * parent.Width;
            }
            if (top < 0)
            {
                string topStr = child.Styles.TryGetValue("top", out var tv) ? tv : "";
                var topPct = CssParser.ParsePercent(topStr);
                if (topPct >= 0)
                    top = topPct * parent.Height;
            }

            // 使用 inset 简写
            if (left < 0 && child.Styles.TryGetValue("inset", out var inset))
            {
                var parts = inset.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    top = CssParser.ParseLengthValue(parts[0], top);
                    if (parts.Length >= 2)
                    {
                        right = CssParser.ParseLengthValue(parts[1], right);
                        bottom = CssParser.ParseLengthValue(parts[1], bottom);
                        left = CssParser.ParseLengthValue(parts[1], left);
                    }
                    if (parts.Length >= 3)
                        bottom = CssParser.ParseLengthValue(parts[2], bottom);
                    if (parts.Length >= 4)
                        left = CssParser.ParseLengthValue(parts[3], left);
                }
            }

            child.X = left >= 0 ? parent.X + left : contentX;
            child.Y = top >= 0 ? parent.Y + top : contentY;

            // 如果有 right 无 width，拉伸
            if (right >= 0 && child.Width == 0)
            {
                child.Width = parent.X + parent.Width - right - child.X;
            }
            if (bottom >= 0 && child.Height == 0)
            {
                child.Height = parent.Y + parent.Height - bottom - child.Y;
            }
        }

        /// <summary>解析子节点 width/height（支持 px、百分比、auto）。</summary>
        static void ResolveSize(LayoutNode child, float parentWidth, float parentHeight)
        {
            // Width
            if (child.Styles.TryGetValue("width", out var wVal))
            {
                var pct = CssParser.ParsePercent(wVal);
                if (pct >= 0)
                    child.Width = pct * parentWidth;
                else
                    child.Width = CssParser.ParseLengthValue(wVal, 0);
            }
            // flex-basis / flex
            else if (child.Styles.TryGetValue("flex", out var flexVal) && flexVal != "0" && flexVal != "none")
            {
                // 简化：flex: 1 → 占满剩余空间（在外部处理）
                child.Width = 0; // 标记为 flex
            }

            // Height
            if (child.Styles.TryGetValue("height", out var hVal))
            {
                var pct = CssParser.ParsePercent(hVal);
                if (pct >= 0)
                    child.Height = pct * parentHeight;
                else
                    child.Height = CssParser.ParseLengthValue(hVal, 0);
            }
            else if (child.Styles.TryGetValue("flex", out var flexVal) && flexVal != "0" && flexVal != "none")
            {
                child.Height = 0;
            }

            // min-width / min-height
            if (child.Styles.TryGetValue("min-width", out var mwVal))
            {
                float mw = CssParser.ParseLengthValue(mwVal, 0);
                if (child.Width < mw) child.Width = mw;
            }
            if (child.Styles.TryGetValue("min-height", out var mhVal))
            {
                float mh = CssParser.ParseLengthValue(mhVal, 0);
                if (child.Height < mh) child.Height = mh;
            }
            if (child.Styles.TryGetValue("max-width", out var maxWVal))
            {
                float maxW = CssParser.ParseLengthValue(maxWVal, float.MaxValue);
                if (child.Width > maxW) child.Width = maxW;
            }
            if (child.Styles.TryGetValue("max-height", out var maxHVal))
            {
                float maxH = CssParser.ParseLengthValue(maxHVal, float.MaxValue);
                if (child.Height > maxH) child.Height = maxH;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // UIDataNode 构建器
    // ════════════════════════════════════════════════════════════════

    /// <summary>将布局后的 DOM 树转换为 UIDataNode JSON 树。</summary>
    public static class NodeBuilder
    {
        // 组件类型 → 前缀映射
        static readonly Dictionary<string, string> ComponentPrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "button", "btn." },
            { "image", "img." },
            { "text", "txt." },
            { "input", "input." },
            { "dropdown", "dropdown." },
            { "toggle", "toggle." },
            { "slider", "slider." },
            { "scroll", "scroll." },
        };

        public static UIDataNode Build(LayoutNode layoutRoot, float rootWidth, float rootHeight)
        {
            return BuildNode(layoutRoot, 0, 0, rootWidth, rootHeight);
        }

        static UIDataNode BuildNode(LayoutNode node, float rootX, float rootY, float rootW, float rootH)
        {
            if (!node.IsDslNode)
            {
                // 非 DSL 节点：如果有子节点，递归处理
                if (node.Children.Count == 0)
                    return null;

                // 多个子节点：收集并返回虚拟容器
                var childNodes = new List<UIDataNode>();
                foreach (var child in node.Children)
                {
                    var built = BuildNode(child, rootX, rootY, rootW, rootH);
                    if (built != null)
                        childNodes.Add(built);
                }

                if (childNodes.Count == 0)
                    return null;
                if (childNodes.Count == 1)
                    return childNodes[0];

                // 创建布局组容器
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var c in childNodes)
                {
                    minX = Mathf.Min(minX, c.x);
                    minY = Mathf.Min(minY, c.y);
                    maxX = Mathf.Max(maxX, c.x + c.width);
                    maxY = Mathf.Max(maxY, c.y + c.height);
                }

                return new UIDataNode
                {
                    name = "layoutGroup_" + Guid.NewGuid().ToString("N").Substring(0, 5),
                    type = "div",
                    dir = "v",
                    value = 0,
                    isChecked = false,
                    options = new List<string>(),
                    x = Mathf.Round(minX),
                    y = Mathf.Round(minY),
                    width = Mathf.Round(maxX - minX),
                    height = Mathf.Round(maxY - minY),
                    color = "#FFFFFF00",
                    fontColor = "#000000",
                    fontSize = 14,
                    textAlign = "center",
                    text = "",
                    layout = "",
                    children = childNodes
                };
            }

            // DSL 节点
            var dom = node.DomNode;
            var styles = node.Styles;

            string uType = dom.GetAttribute("data-u-type", "").ToLowerInvariant();
            string uName = dom.GetAttribute("data-u-name", "");
            string uDir = dom.GetAttribute("data-u-dir", "v");
            float uValue = ParseFloatSafe(dom.GetAttribute("data-u-value", ""), 0.5f);
            bool uChecked = dom.GetAttribute("data-u-checked", "").Equals("true", StringComparison.OrdinalIgnoreCase);
            string uLayout = dom.GetAttribute("data-u-layout", "").Trim().ToLowerInvariant();

            // data-u-export 检查
            var exportRaw = dom.GetAttribute("data-u-export", "").Trim().ToLowerInvariant();
            bool shouldExport = string.IsNullOrEmpty(exportRaw) || exportRaw == "true" || exportRaw == "1" || exportRaw == "yes";
            if (!shouldExport)
                return null;

            // 解析 CSS 属性
            string bgColor = CssParser.ParseBackgroundColor(styles);
            string fontColor = CssParser.ParseColor(styles.TryGetValue("color", out var colorVal) ? colorVal : "#000000");
            int fontSize = Mathf.RoundToInt(CssParser.ParseLength(styles, "font-size", 14));
            string textAlign = (styles.TryGetValue("text-align", out var taVal) ? taVal : "center").ToLowerInvariant();

            // 文本内容
            string textContent = GetTextContent(dom, uType);

            // 下拉选项
            var options = new List<string>();
            if (uType == "dropdown")
            {
                foreach (var child in dom.Children)
                {
                    if (child.TagName == "option")
                        options.Add(child.GetInnerText().Trim());
                }
            }

            // 构建节点名（含前缀映射）
            string fullNodeName = BuildNodeName(uType, uName);

            // 渐变、描边、圆角、边框
            var linearGradient = CssParser.ParseLinearGradient(styles);
            var outline = CssParser.ParseOutline(dom, styles);
            var borderRadius = CssParser.ParseBorderRadius(styles);
            var border = CssParser.ParseCssBorder(styles);
            var imageUrl = CssParser.ExtractImageUrl(styles);

            // 子节点
            var children = new List<UIDataNode>();
            foreach (var child in node.Children)
            {
                var built = BuildNode(child, rootX, rootY, rootW, rootH);
                if (built != null)
                    children.Add(built);
            }

            var result = new UIDataNode
            {
                name = uName, // 保留原始 data-u-name（与浏览器转换器一致）
                type = uType,
                dir = uDir,
                value = uValue,
                isChecked = uChecked,
                options = options,
                x = Mathf.RoundToInt(node.X - rootX),
                y = Mathf.RoundToInt(node.Y - rootY),
                width = Mathf.RoundToInt(node.Width),
                height = Mathf.RoundToInt(node.Height),
                color = bgColor,
                fontColor = fontColor,
                fontSize = fontSize,
                textAlign = textAlign,
                text = textContent,
                layout = uLayout,
                children = children
            };

            if (linearGradient != null)
                result.linearGradient = linearGradient;
            if (outline != null)
                result.outline = outline;
            if (borderRadius != null)
                result.borderRadius = borderRadius;
            if (border != null)
                result.border = border;
            if (!string.IsNullOrEmpty(imageUrl))
                result.image = imageUrl;

            return result;
        }

        static string GetTextContent(HtmlDomNode dom, string uType)
        {
            if (dom.TagName == "input")
            {
                return dom.GetAttribute("value", dom.GetAttribute("placeholder", ""));
            }

            // 收集直接文本子节点（非 data-u 节点的文本）
            var sb = new StringBuilder();
            CollectDirectText(dom, sb);
            return sb.ToString().Trim();
        }

        static void CollectDirectText(HtmlDomNode node, StringBuilder sb)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    sb.Append(child.TextContent);
                }
                else if (!child.Attributes.ContainsKey("data-u-type"))
                {
                    // 非 DSL 子节点：递归收集文本
                    CollectDirectText(child, sb);
                }
            }
        }

        static string BuildNodeName(string uType, string uName)
        {
            if (string.IsNullOrEmpty(uName))
                return uName;

            if (!ComponentPrefixes.TryGetValue(uType, out var prefix))
                return uName;

            // 已带正确前缀
            if (uName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return uName;

            // image 类型且名为 bg：不加前缀
            if (uType == "image" && uName == "bg")
                return "bg";

            // 添加前缀 + 首字母大写
            string localName = uName;
            if (localName.Length > 0)
                localName = char.ToUpperInvariant(localName[0]) + localName.Substring(1);

            return prefix + localName;
        }

        static float ParseFloatSafe(string val, float defaultValue)
        {
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            return defaultValue;
        }
    }
}
