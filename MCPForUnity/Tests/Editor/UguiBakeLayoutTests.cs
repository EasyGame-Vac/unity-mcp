// UguiBakeLayoutTests.cs
// UguiBake 布局引擎与解析器的 EditMode 单元测试。
// 覆盖：颜色解析、布局缓存、文本宽度估算、Flex 布局、Bake Report。
//

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.UguiBake;

namespace MCPForUnity.Tests.Editor
{
    // ════════════════════════════════════════════════════════════════
    // 颜色解析测试
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class ColorParsingTests
    {
        [Test]
        public void ParseColor_3DigitHex_ExpandsTo6Digit()
        {
            string result = CssParser.ParseColor("#abc");
            Assert.AreEqual("#AABBCC", result);
        }

        [Test]
        public void ParseColor_4DigitHex_ExpandsTo8Digit()
        {
            string result = CssParser.ParseColor("#abcd");
            Assert.AreEqual("#AABBCCDD", result);
        }

        [Test]
        public void ParseColor_6DigitHex_PassesThrough()
        {
            string result = CssParser.ParseColor("#FF8800");
            Assert.AreEqual("#FF8800", result);
        }

        [Test]
        public void ParseColor_8DigitHex_PassesThrough()
        {
            string result = CssParser.ParseColor("#FF8800AA");
            Assert.AreEqual("#FF8800AA", result);
        }

        [Test]
        public void ParseColor_Rgb_IntegerValues()
        {
            string result = CssParser.ParseColor("rgb(255, 128, 0)");
            Assert.AreEqual("#FF8000", result);
        }

        [Test]
        public void ParseColor_Rgba_WithAlpha()
        {
            string result = CssParser.ParseColor("rgba(255, 128, 0, 0.5)");
            Assert.IsTrue(result.StartsWith("#FF8000"));
            // alpha = 0.5 * 255 = 127.5 → 128 = 0x80
            Assert.IsTrue(result.Length == 9);
        }

        [Test]
        public void ParseColor_Rgb_PercentageValues()
        {
            string result = CssParser.ParseColor("rgb(100%, 50%, 0%)");
            Assert.AreEqual("#FF8000", result);
        }

        [Test]
        public void ParseColor_Hsl_BasicConversion()
        {
            // hsl(0, 100%, 50%) = pure red
            string result = CssParser.ParseColor("hsl(0, 100%, 50%)");
            Assert.AreEqual("#FF0000", result);
        }

        [Test]
        public void ParseColor_Hsl_120Green()
        {
            // hsl(120, 100%, 50%) = pure green
            string result = CssParser.ParseColor("hsl(120, 100%, 50%)");
            Assert.AreEqual("#00FF00", result);
        }

        [Test]
        public void ParseColor_Hsl_240Blue()
        {
            // hsl(240, 100%, 50%) = pure blue
            string result = CssParser.ParseColor("hsl(240, 100%, 50%)");
            Assert.AreEqual("#0000FF", result);
        }

        [Test]
        public void ParseColor_Hsla_WithAlpha()
        {
            string result = CssParser.ParseColor("hsla(0, 100%, 50%, 0.5)");
            Assert.IsTrue(result.StartsWith("#FF0000"));
            Assert.IsTrue(result.Length == 9);
        }

        [Test]
        public void ParseColor_NamedColor_White()
        {
            string result = CssParser.ParseColor("white");
            Assert.AreEqual("#FFFFFF", result);
        }

        [Test]
        public void ParseColor_NamedColor_Black()
        {
            string result = CssParser.ParseColor("black");
            Assert.AreEqual("#000000", result);
        }

        [Test]
        public void ParseColor_NamedColor_Coral()
        {
            string result = CssParser.ParseColor("coral");
            Assert.AreEqual("#FF7F50", result);
        }

        [Test]
        public void ParseColor_Transparent_ReturnsTransparentWhite()
        {
            string result = CssParser.ParseColor("transparent");
            Assert.AreEqual("#FFFFFF00", result);
        }

        [Test]
        public void ParseColor_None_ReturnsTransparentWhite()
        {
            string result = CssParser.ParseColor("none");
            Assert.AreEqual("#FFFFFF00", result);
        }

        [Test]
        public void ParseColor_EmptyString_ReturnsTransparentWhite()
        {
            string result = CssParser.ParseColor("");
            Assert.AreEqual("#FFFFFF00", result);
        }

        [Test]
        public void ParseColor_CaseInsensitive()
        {
            string upper = CssParser.ParseColor("#ABCDEF");
            string lower = CssParser.ParseColor("#abcdef");
            Assert.AreEqual(upper, lower);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 布局缓存测试
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class LayoutCacheTests
    {
        [SetUp]
        public void Setup()
        {
            HtmlToUguiParser.ClearCache();
        }

        [Test]
        public void ClearCache_RemovesAllEntries()
        {
            HtmlToUguiParser.ClearCache();
            // 确保不抛出异常
            Assert.Pass();
        }

        [Test]
        public void ParseToNode_SameInputTwice_UsesCache()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:300px;background:#333\">" +
                          "<div data-u-type=\"text\" data-u-name=\"label\" style=\"font-size:24px;color:#fff\">Hello</div>" +
                          "</div>";

            var first = HtmlToUguiParser.ParseToNode(html, 400, 300);
            var second = HtmlToUguiParser.ParseToNode(html, 400, 300);

            // 缓存应返回同一实例引用
            Assert.AreSame(first, second);
        }

        [Test]
        public void ParseToNode_DifferentDimensions_DifferentResults()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:300px;background:#333\"></div>";

            var first = HtmlToUguiParser.ParseToNode(html, 400, 300);
            var second = HtmlToUguiParser.ParseToNode(html, 800, 600);

            // 不同尺寸应产生不同实例
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void ParseToNode_AfterClearCache_NewInstance()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:200px;height:100px;background:#fff\"></div>";

            var first = HtmlToUguiParser.ParseToNode(html, 200, 100);
            HtmlToUguiParser.ClearCache();
            var second = HtmlToUguiParser.ParseToNode(html, 200, 100);

            Assert.AreNotSame(first, second);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 文本宽度估算测试
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class TextWidthEstimationTests
    {
        [Test]
        public void ParseToNode_TextNode_HasNonZeroWidth()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:300px;display:flex;flex-direction:column;align-items:center\">" +
                          "<div data-u-type=\"text\" data-u-name=\"label\" style=\"font-size:24px;color:#fff\">Hello World</div>" +
                          "</div>";

            var root = HtmlToUguiParser.ParseToNode(html, 400, 300);
            Assert.IsNotNull(root);
            Assert.IsNotNull(root.children);
            Assert.AreEqual(1, root.children.Count);

            var textNode = root.children[0];
            // 文本节点应有非零宽度（通过估算）
            Assert.Greater(textNode.width, 0f, "文本节点宽度应大于 0");
        }

        [Test]
        public void ParseToNode_CjkText_HasNonZeroWidth()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:300px;display:flex;flex-direction:column\">" +
                          "<div data-u-type=\"text\" data-u-name=\"label\" style=\"font-size:32px;color:#fff\">你好世界</div>" +
                          "</div>";

            var root = HtmlToUguiParser.ParseToNode(html, 400, 300);
            var textNode = root.children[0];
            Assert.Greater(textNode.width, 0f, "CJK 文本节点宽度应大于 0");
        }

        [Test]
        public void ParseToNode_TextNode_CjkWidthGreaterThanSingleChar()
        {
            string htmlShort = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:800px;height:600px;display:flex;flex-direction:column\">" +
                               "<div data-u-type=\"text\" data-u-name=\"s\" style=\"font-size:24px;color:#fff\">A</div>" +
                               "</div>";
            string htmlLong = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:800px;height:600px;display:flex;flex-direction:column\">" +
                              "<div data-u-type=\"text\" data-u-name=\"l\" style=\"font-size:24px;color:#fff\">这是一个很长的中文文本内容用于测试宽度估算</div>" +
                              "</div>";

            var shortNode = HtmlToUguiParser.ParseToNode(htmlShort, 800, 600);
            var longNode = HtmlToUguiParser.ParseToNode(htmlLong, 800, 600);

            HtmlToUguiParser.ClearCache();

            var shortText = shortNode.children[0];
            var longText = longNode.children[0];

            Assert.Greater(longText.width, shortText.width,
                "长文本宽度应大于短文本宽度");
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Flex 布局测试
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class FlexLayoutTests
    {
        [TearDown]
        public void Cleanup()
        {
            HtmlToUguiParser.ClearCache();
        }

        [Test]
        public void ParseToNode_FlexColumn_ChildrenStackedVertically()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:600px;display:flex;flex-direction:column;gap:10px\">" +
                          "<div data-u-type=\"div\" data-u-name=\"a\" style=\"width:100%;height:100px;background:#f00\"></div>" +
                          "<div data-u-type=\"div\" data-u-name=\"b\" style=\"width:100%;height:200px;background:#0f0\"></div>" +
                          "</div>";

            var root = HtmlToUguiParser.ParseToNode(html, 400, 600);
            Assert.IsNotNull(root.children);
            Assert.AreEqual(2, root.children.Count);

            // 第一个子节点在上方
            var a = root.children[0];
            var b = root.children[1];
            Assert.LessOrEqual(a.y, b.y, "第一个子节点 Y 坐标应小于第二个");
        }

        [Test]
        public void ParseToNode_FlexGrow_ChildrenShareRemainingSpace()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:600px;display:flex;flex-direction:column\">" +
                          "<div data-u-type=\"div\" data-u-name=\"fixed\" style=\"width:100%;height:100px;background:#f00\"></div>" +
                          "<div data-u-type=\"div\" data-u-name=\"grow1\" style=\"flex:1;background:#00f\"></div>" +
                          "<div data-u-type=\"div\" data-u-name=\"grow2\" style=\"flex:1;background:#0f0\"></div>" +
                          "</div>";

            var root = HtmlToUguiParser.ParseToNode(html, 400, 600);
            Assert.AreEqual(3, root.children.Count);

            var grow1 = root.children[1];
            var grow2 = root.children[2];

            // flex:1 的两个节点应平分剩余空间：(600 - 100) / 2 = 250
            Assert.Greater(grow1.height, 0f, "flex:1 节点高度应大于 0");
            Assert.Greater(grow2.height, 0f, "flex:1 节点高度应大于 0");

            // 两个 flex:1 节点高度应大致相等
            float diff = Mathf.Abs(grow1.height - grow2.height);
            Assert.LessOrEqual(diff, 2f, "两个 flex:1 节点高度差应不超过 2px");
        }

        [Test]
        public void ParseToNode_FlexRow_ChildrenLaidOutHorizontally()
        {
            string html = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:600px;height:100px;display:flex;flex-direction:row;gap:10px\">" +
                          "<div data-u-type=\"div\" data-u-name=\"a\" style=\"width:200px;height:100%;background:#f00\"></div>" +
                          "<div data-u-type=\"div\" data-u-name=\"b\" style=\"width:200px;height:100%;background:#0f0\"></div>" +
                          "</div>";

            var root = HtmlToUguiParser.ParseToNode(html, 600, 100);
            Assert.AreEqual(2, root.children.Count);

            var a = root.children[0];
            var b = root.children[1];
            Assert.Less(a.x, b.x, "Row 布局中第一个子节点 X 坐标应小于第二个");
        }

        [Test]
        public void ParseToNode_Gap_CreatesSpacingBetweenChildren()
        {
            string htmlNoGap = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:600px;display:flex;flex-direction:column\">" +
                               "<div data-u-type=\"div\" data-u-name=\"a\" style=\"width:100%;height:100px;background:#f00\"></div>" +
                               "<div data-u-type=\"div\" data-u-name=\"b\" style=\"width:100%;height:100px;background:#0f0\"></div>" +
                               "</div>";
            string htmlWithGap = "<div data-u-type=\"div\" data-u-name=\"root\" style=\"width:400px;height:600px;display:flex;flex-direction:column;gap:20px\">" +
                                 "<div data-u-type=\"div\" data-u-name=\"a\" style=\"width:100%;height:100px;background:#f00\"></div>" +
                                 "<div data-u-type=\"div\" data-u-name=\"b\" style=\"width:100%;height:100px;background:#0f0\"></div>" +
                                 "</div>";

            var noGap = HtmlToUguiParser.ParseToNode(htmlNoGap, 400, 600);
            HtmlToUguiParser.ClearCache();
            var withGap = HtmlToUguiParser.ParseToNode(htmlWithGap, 400, 600);

            float noGapSpacing = noGap.children[1].y - noGap.children[0].y;
            float withGapSpacing = withGap.children[1].y - withGap.children[0].y;

            Assert.Greater(withGapSpacing, noGapSpacing, "有 gap 时子节点间距应更大");
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Bake Report 测试
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class BakeReportTests
    {
        [Test]
        public void Begin_StartsTimer()
        {
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            Assert.IsNotNull(report);
            Assert.AreEqual("Assets/Test.prefab", report.PrefabPath);
            Assert.AreEqual("html", report.SourceFormat);
        }

        [Test]
        public void LogInfo_AddsEntry()
        {
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            int beforeCount = report.Entries.Count;
            report.LogInfo("parse", "开始解析");
            Assert.AreEqual(beforeCount + 1, report.Entries.Count);
            var entry = report.Entries[report.Entries.Count - 1];
            Assert.AreEqual("info", entry.Level);
            Assert.AreEqual("parse", entry.Stage);
            Assert.AreEqual("开始解析", entry.Message);
        }

        [Test]
        public void LogWarning_AddsEntry()
        {
            LogAssert.Expect(LogType.Warning, "[UguiBake][layout] 宽度为 0");
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            report.LogWarning("layout", "宽度为 0");
            var entry = report.Entries[report.Entries.Count - 1];
            Assert.AreEqual("warning", entry.Level);
        }

        [Test]
        public void LogError_AddsEntry()
        {
            LogAssert.Expect(LogType.Error, "[UguiBake][bake] 烘焙失败");
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            report.LogError("bake", "烘焙失败");
            var entry = report.Entries[report.Entries.Count - 1];
            Assert.AreEqual("error", entry.Level);
        }

        [Test]
        public void Finish_SetsSuccessAndNodeCount()
        {
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            report.Finish(true, 42);
            Assert.IsTrue(report.Success);
            Assert.AreEqual(42, report.NodeCount);
            Assert.AreEqual(42, report.TotalNodes);
        }

        [Test]
        public void GetTotalElapsedMs_ReturnsPositiveAfterBegin()
        {
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            float elapsed = report.GetTotalElapsedMs();
            Assert.GreaterOrEqual(elapsed, 0f);
        }

        [Test]
        public void ToSummaryString_ContainsKeyInfo()
        {
            var report = BakeReport.Begin("Assets/Test.prefab", "html");
            report.LogWarning("layout", "测试警告");
            report.Finish(true, 10);
            string summary = report.ToSummaryString();
            Assert.IsTrue(summary.Contains("Assets/Test.prefab"));
            Assert.IsTrue(summary.Contains("成功"));
            Assert.IsTrue(summary.Contains("测试警告"));
        }

        [Test]
        public void Current_BeginAndEnd_ThreadSafeAccess()
        {
            BakeReport.BeginCurrent("Assets/CurrentTest.prefab", "html");
            Assert.IsNotNull(BakeReport.Current);
            Assert.AreEqual("Assets/CurrentTest.prefab", BakeReport.Current.PrefabPath);
            BakeReport.EndCurrent();
            Assert.IsNull(BakeReport.Current);
        }
    }
}
