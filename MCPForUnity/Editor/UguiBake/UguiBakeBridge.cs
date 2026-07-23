// UguiBakeBridge.cs
// MCP 桥接层：封装 UguiPrefabBakerCore 调用，供 execute_code 或专用 MCP 工具使用。
// 所有方法返回 Dictionary<string, object> 以便 JSON 序列化。
//
// 使用方式（通过 execute_code）：
//   return UguiBakeBridge.Bake(jsonContent, "Assets/Baked/MyPage.prefab", 942, 2048);
//

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>
    /// MCP 桥接层：为 AI / MCP 工具提供简洁的烘焙 API。
    /// 所有方法返回 Dictionary&lt;string, object&gt;，可被 execute_code 序列化为 JSON。
    /// </summary>
    public static class UguiBakeBridge
    {
        // ──────────────────── 常量 ────────────────────

        public const string DefaultPrefabDir = "Assets/MCP/UguiBake/Baked/Prefabs";
        public const string DefaultJsonDir = "Assets/MCP/UguiBake/Baked/Json";
        public const string BackupDir = "Assets/MCP/UguiBake/Baked/Prefabs/.backup";

        // ──────────────────── 单个烘焙 ────────────────────

        /// <summary>
        /// 烘焙单个 UIDataNode JSON → UGUI 预制体。
        /// </summary>
        /// <param name="jsonContent">符合 UIDataNode 结构的 JSON 字符串</param>
        /// <param name="prefabPath">输出预制体 Assets 相对路径（如 Assets/Baked/LoginPage.prefab）</param>
        /// <param name="width">设计分辨率宽（默认 942）</param>
        /// <param name="height">设计分辨率高（默认 2048）</param>
        /// <param name="useTMP">使用 TextMeshPro（默认 true）</param>
        /// <param name="templatePrefabPath">模板预制体路径（可选，根节点需有 Canvas）</param>
        /// <param name="sourceHtmlPath">源 HTML 路径（可选，用于图片路径解析）</param>
        /// <param name="saveSnapshot">是否保存 JSON 快照（默认 true）</param>
        public static Dictionary<string, object> Bake(
            string jsonContent,
            string prefabPath,
            int width = 942,
            int height = 2048,
            bool useTMP = true,
            string templatePrefabPath = null,
            string sourceHtmlPath = null,
            bool saveSnapshot = true)
        {
            var result = new Dictionary<string, object>();

            // 1. 参数校验
            if (string.IsNullOrWhiteSpace(jsonContent))
                return ErrorResult("jsonContent 不能为空");
            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            prefabPath = prefabPath.Replace("\\", "/").Trim();
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return ErrorResult("prefabPath 必须以 .prefab 结尾");
            if (!prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
                return ErrorResult("prefabPath 必须以 Assets/ 开头");

            // 2. JSON 校验
            if (!UguiPrefabBakerCore.TryValidateJson(jsonContent, out string validateError))
                return ErrorResult("JSON 校验失败: " + validateError);

            // 3. 解析根节点名（用于快照命名）
            string pageName = "Unknown";
            try
            {
                var rootNode = JsonConvert.DeserializeObject<UIDataNode>(jsonContent);
                if (rootNode != null && !string.IsNullOrEmpty(rootNode.name))
                    pageName = rootNode.name;
            }
            catch { /* 忽略，用默认名 */ }

            // 4. 保存 JSON 快照
            string snapshotPath = null;
            if (saveSnapshot)
            {
                try
                {
                    snapshotPath = SaveJsonSnapshot(jsonContent, pageName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UguiBakeBridge] JSON 快照保存失败（不影响烘焙）: {e.Message}");
                }
            }

            // 5. 备份现有预制体
            string backupPath = null;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                try
                {
                    backupPath = BackupPrefab(prefabPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UguiBakeBridge] 预制体备份失败（不影响烘焙）: {e.Message}");
                }
            }

            // 6. 确保输出目录存在
            EnsureAssetFolderForPath(prefabPath);

            // 7. 解析模板预制体
            GameObject templatePrefab = null;
            if (!string.IsNullOrEmpty(templatePrefabPath))
            {
                templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(templatePrefabPath);
                if (templatePrefab == null)
                    return ErrorResult($"模板预制体未找到: {templatePrefabPath}");
            }

            // 8. 执行烘焙
            var refSize = new Vector2(width, height);
            string bakeError;

            bool success;
            try
            {
                success = UguiPrefabBakerCore.TryBakeJsonStringToPrefab(
                    jsonContent,
                    prefabPath,
                    refSize,
                    templatePrefab,
                    sourceHtmlPath,
                    out bakeError,
                    useTMP,
                    true);
            }
            catch (Exception e)
            {
                return ErrorResult($"烘焙异常: {e.Message}");
            }

            // 9. 构建结果
            result["success"] = success;
            result["prefabPath"] = prefabPath;
            result["pageName"] = pageName;
            result["resolution"] = new { width, height };
            result["useTMP"] = useTMP;
            if (snapshotPath != null)
                result["jsonSnapshot"] = snapshotPath;
            if (backupPath != null)
                result["backup"] = backupPath;

            if (!success)
            {
                result["error"] = bakeError ?? "未知烘焙错误";
            }
            else
            {
                result["message"] = $"成功烘焙 '{pageName}' → {prefabPath}";
                // 刷新 AssetDatabase
                AssetDatabase.Refresh();
            }

            return result;
        }

        // ──────────────────── 批量烘焙 ────────────────────

        /// <summary>
        /// 批量烘焙多个 JSON → 多个预制体。
        /// </summary>
        /// <param name="jsonArrayContent">JSON 数组字符串，每个元素为 { json: "...", prefabPath: "..." } 或直接为 UIDataNode JSON（此时预制体名从根节点 name 推导）</param>
        /// <param name="outputDir">输出目录（Assets 相对路径），当 JSON 元素未指定 prefabPath 时使用</param>
        /// <param name="width">设计分辨率宽</param>
        /// <param name="height">设计分辨率高</param>
        /// <param name="useTMP">使用 TextMeshPro</param>
        public static Dictionary<string, object> BakeBatch(
            string jsonArrayContent,
            string outputDir,
            int width = 942,
            int height = 2048,
            bool useTMP = true)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(jsonArrayContent))
                return ErrorResult("jsonArrayContent 不能为空");

            outputDir = (outputDir ?? DefaultPrefabDir).Replace("\\", "/").Trim();
            if (!outputDir.StartsWith("Assets/", StringComparison.Ordinal))
                return ErrorResult("outputDir 必须以 Assets/ 开头");

            EnsureAssetFolder(outputDir);

            JArray items;
            try
            {
                items = JArray.Parse(jsonArrayContent);
            }
            catch (Exception e)
            {
                return ErrorResult("JSON 数组解析失败: " + e.Message);
            }

            var results = new List<Dictionary<string, object>>();
            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string itemJson = null;
                string itemPrefabPath = null;

                if (item.Type == JTokenType.Object)
                {
                    // 检查是否有 json/prefabPath 字段
                    var jsonToken = item["json"];
                    var pathToken = item["prefabPath"];

                    if (jsonToken != null)
                    {
                        itemJson = jsonToken.ToString(Formatting.None);
                        // 如果 json 本身是对象，需要取其字符串形式
                        if (jsonToken.Type == JTokenType.Object)
                            itemJson = jsonToken.ToString(Formatting.None);
                        else if (jsonToken.Type == JTokenType.String)
                            itemJson = jsonToken.Value<string>();
                    }
                    else
                    {
                        // 整个对象就是一个 UIDataNode
                        itemJson = item.ToString(Formatting.None);
                    }

                    if (pathToken != null)
                        itemPrefabPath = pathToken.Value<string>();
                }
                else if (item.Type == JTokenType.String)
                {
                    itemJson = item.Value<string>();
                }

                // 如果没有指定 prefabPath，从根节点 name 推导
                if (string.IsNullOrEmpty(itemPrefabPath))
                {
                    string pageName = ExtractPageName(itemJson);
                    itemPrefabPath = $"{outputDir}/{pageName}.prefab";
                }

                var bakeResult = Bake(itemJson, itemPrefabPath, width, height, useTMP);
                results.Add(bakeResult);

                if (bakeResult.TryGetValue("success", out var suc) && suc is bool b && b)
                    successCount++;
                else
                    failCount++;
            }

            result["success"] = failCount == 0;
            result["total"] = items.Count;
            result["succeeded"] = successCount;
            result["failed"] = failCount;
            result["results"] = results;
            result["message"] = $"批量烘焙完成: {successCount} 成功, {failCount} 失败 (共 {items.Count})";

            return result;
        }

        // ──────────────────── 增量更新 ────────────────────

        /// <summary>
        /// 增量更新预制体的指定子树。
        /// 加载现有预制体，找到 nodePath 对应的节点，删除其子节点并重新烘焙 jsonContent。
        /// </summary>
        /// <param name="prefabPath">现有预制体路径</param>
        /// <param name="nodePath">目标节点路径（如 "content/@topHud"），用 / 分隔</param>
        /// <param name="jsonContent">要烘焙到该位置的 UIDataNode JSON（可以是单个节点或含 children 的容器）</param>
        /// <param name="width">设计分辨率宽</param>
        /// <param name="height">设计分辨率高</param>
        /// <param name="useTMP">使用 TextMeshPro</param>
        public static Dictionary<string, object> BakePartial(
            string prefabPath,
            string nodePath,
            string jsonContent,
            int width = 942,
            int height = 2048,
            bool useTMP = true)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");
            if (string.IsNullOrWhiteSpace(nodePath))
                return ErrorResult("nodePath 不能为空");
            if (string.IsNullOrWhiteSpace(jsonContent))
                return ErrorResult("jsonContent 不能为空");

            prefabPath = prefabPath.Replace("\\", "/").Trim();

            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existingPrefab == null)
                return ErrorResult($"预制体未找到: {prefabPath}");

            // 校验 JSON
            if (!UguiPrefabBakerCore.TryValidateJson(jsonContent, out string validateError))
                return ErrorResult("JSON 校验失败: " + validateError);

            // 备份
            string backupPath = null;
            try { backupPath = BackupPrefab(prefabPath); }
            catch { /* 忽略 */ }

            // 加载预制体内容
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(prefabPath);

                // 找到目标节点
                Transform targetNode = FindNodeByPath(contents.transform, nodePath);
                if (targetNode == null)
                    return ErrorResult($"在预制体中未找到节点路径: {nodePath}");

                // 解析 JSON
                var rootNode = UguiPrefabBakerCore.ParseUiDataJson(jsonContent);

                // 删除目标节点的所有子节点
                for (int i = targetNode.childCount - 1; i >= 0; i--)
                {
                    var child = targetNode.GetChild(i);
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                // 在目标节点下烘焙新的子树
                float px = targetNode.GetComponent<RectTransform>()?.anchoredPosition.x ?? 0;
                float py = targetNode.GetComponent<RectTransform>()?.anchoredPosition.y ?? 0;
                var rect = targetNode.GetComponent<RectTransform>();
                float pw = rect != null ? rect.rect.width : width;
                float ph = rect != null ? rect.rect.height : height;

                UguiPrefabBakerCore.BeginImageResolveSession(null);
                try
                {
                    if (rootNode.children != null && rootNode.children.Count > 0)
                    {
                        foreach (var child in rootNode.children)
                            UguiPrefabBakerCore.CreateUINode(child, targetNode, px, py, pw, ph, useTMP);
                    }
                    else
                    {
                        UguiPrefabBakerCore.CreateUINode(rootNode, targetNode, px, py, pw, ph, useTMP);
                    }
                }
                finally
                {
                    UguiPrefabBakerCore.EndImageResolveSession();
                }

                // 保存
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);

                result["success"] = true;
                result["prefabPath"] = prefabPath;
                result["nodePath"] = nodePath;
                if (backupPath != null)
                    result["backup"] = backupPath;
                result["message"] = $"增量更新成功: {prefabPath} @ {nodePath}";

                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                return ErrorResult($"增量更新异常: {e.Message}");
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
            }

            return result;
        }

        // ──────────────────── 列表 / 删除 ────────────────────

        /// <summary>
        /// 列出已烘焙的预制体。
        /// </summary>
        public static Dictionary<string, object> ListBaked(string searchDir = null)
        {
            var result = new Dictionary<string, object>();
            searchDir = (searchDir ?? DefaultPrefabDir).Replace("\\", "/").Trim();

            if (!AssetDatabase.IsValidFolder(searchDir))
            {
                result["success"] = true;
                result["prefabs"] = new List<object>();
                result["message"] = $"目录不存在: {searchDir}";
                return result;
            }

            var prefabs = new List<object>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchDir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var info = new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["name"] = go.name,
                };

                var canvas = go.GetComponent<Canvas>();
                if (canvas != null)
                    info["hasCanvas"] = true;

                // 统计子节点数
                info["childCount"] = CountAllChildren(go.transform);

                prefabs.Add(info);
            }

            result["success"] = true;
            result["prefabs"] = prefabs;
            result["total"] = prefabs.Count;
            result["message"] = $"找到 {prefabs.Count} 个预制体";

            return result;
        }

        /// <summary>
        /// 删除预制体（及其 JSON 快照）。
        /// </summary>
        public static Dictionary<string, object> DeleteBaked(string prefabPath)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            prefabPath = prefabPath.Replace("\\", "/").Trim();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return ErrorResult($"预制体未找到: {prefabPath}");

            // 删除预制体
            bool deleted = AssetDatabase.DeleteAsset(prefabPath);

            // 尝试删除对应的 JSON 快照
            string pageName = Path.GetFileNameWithoutExtension(prefabPath);
            string jsonSnapshot = $"{DefaultJsonDir}/{pageName}.ugui.json";
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(jsonSnapshot) != null)
                AssetDatabase.DeleteAsset(jsonSnapshot);

            result["success"] = deleted;
            result["prefabPath"] = prefabPath;
            result["message"] = deleted
                ? $"已删除 {prefabPath}"
                : $"删除失败: {prefabPath}";

            return result;
        }

        // ──────────────────── 缩进 DSL → JSON / Prefab ────────────────────

        /// <summary>
        /// 将缩进 DSL 解析为 UIDataNode JSON（比 HTML 轻 ~65% token，布局引擎自动算坐标）。
        /// </summary>
        public static Dictionary<string, object> ParseDsl(
            string dslContent,
            int width = 942,
            int height = 2048)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(dslContent))
                return ErrorResult("dslContent 不能为空");

            try
            {
                var diag = IndentDslParser.ParseWithDiagnostics(dslContent, width, height);
                string json = JsonConvert.SerializeObject(diag.Root, Formatting.Indented);
                var rootNode = diag.Root;

                result["success"] = true;
                result["json"] = json;
                result["pageName"] = rootNode?.name ?? "Unknown";
                result["resolution"] = new { width, height };
                result["nodeCount"] = CountNodes(rootNode);
                if (diag.Warnings.Count > 0)
                    result["warnings"] = diag.Warnings;
                string warnSuffix = diag.Warnings.Count > 0 ? $"（{diag.Warnings.Count} 条警告）" : "";
                result["message"] = $"DSL 解析成功: {rootNode?.name ?? "Unknown"} ({CountNodes(rootNode)} 个节点){warnSuffix}";
            }
            catch (Exception e)
            {
                return ErrorResult($"DSL 解析失败: {e.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从缩进 DSL 直接烘焙预制体（解析 + 烘焙一步到位，推荐入口）。
        /// </summary>
        public static Dictionary<string, object> BakeFromDsl(
            string dslContent,
            string prefabPath,
            int width = 942,
            int height = 2048,
            bool useTMP = true,
            string sourceHtmlPath = null)
        {
            if (string.IsNullOrWhiteSpace(dslContent))
                return ErrorResult("dslContent 不能为空");
            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            // 1. 解析 DSL → JSON（带诊断）
            string json;
            List<string> dslWarnings = null;
            try
            {
                var diag = IndentDslParser.ParseWithDiagnostics(dslContent, width, height);
                json = JsonConvert.SerializeObject(diag.Root, Formatting.Indented);
                dslWarnings = diag.Warnings;
            }
            catch (Exception e)
            {
                return ErrorResult($"DSL 解析失败: {e.Message}");
            }

            // 2. 烘焙 JSON → 预制体
            var bakeResult = Bake(json, prefabPath, width, height, useTMP, null, sourceHtmlPath);
            if (dslWarnings != null && dslWarnings.Count > 0)
                bakeResult["dslWarnings"] = dslWarnings;
            return bakeResult;
        }

        // ──────────────────── HTML → JSON 解析 ────────────────────

        /// <summary>
        /// 将 UI-DSL HTML 解析为 UIDataNode JSON（纯 C#，无需浏览器）。
        /// </summary>
        public static Dictionary<string, object> ParseHtml(
            string htmlContent,
            int width = 942,
            int height = 2048)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(htmlContent))
                return ErrorResult("htmlContent 不能为空");

            try
            {
                string json = HtmlToUguiParser.Parse(htmlContent, width, height);
                var rootNode = JsonConvert.DeserializeObject<UIDataNode>(json);

                result["success"] = true;
                result["json"] = json;
                result["pageName"] = rootNode?.name ?? "Unknown";
                result["resolution"] = new { width, height };
                result["nodeCount"] = CountNodes(rootNode);
                result["message"] = $"HTML 解析成功: {rootNode?.name ?? "Unknown"} ({CountNodes(rootNode)} 个节点)";
            }
            catch (Exception e)
            {
                return ErrorResult($"HTML 解析失败: {e.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从 HTML 直接烘焙预制体（解析 + 烘焙一步到位）。
        /// </summary>
        /// <param name="htmlContent">符合 UI-DSL 规范的 HTML 字符串</param>
        /// <param name="prefabPath">输出预制体路径</param>
        /// <param name="width">基准分辨率宽</param>
        /// <param name="height">基准分辨率高</param>
        /// <param name="useTMP">使用 TextMeshPro</param>
        /// <param name="sourceHtmlPath">源 HTML 路径（用于图片解析）</param>
        public static Dictionary<string, object> BakeFromHtml(
            string htmlContent,
            string prefabPath,
            int width = 942,
            int height = 2048,
            bool useTMP = true,
            string sourceHtmlPath = null)
        {
            if (string.IsNullOrWhiteSpace(htmlContent))
                return ErrorResult("htmlContent 不能为空");
            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            // 1. 解析 HTML → JSON
            string json;
            try
            {
                json = HtmlToUguiParser.Parse(htmlContent, width, height);
            }
            catch (Exception e)
            {
                return ErrorResult($"HTML 解析失败: {e.Message}");
            }

            // 2. 烘焙 JSON → 预制体
            return Bake(json, prefabPath, width, height, useTMP, null, sourceHtmlPath);
        }

        static int CountNodes(UIDataNode node)
        {
            if (node == null) return 0;
            int count = 1;
            if (node.children != null)
                foreach (var child in node.children)
                    count += CountNodes(child);
            return count;
        }

        // ──────────────────── DSL 规范获取 ────────────────────

        /// <summary>
        /// 获取 DSL 规范文本（从 DSL 目录读取）。
        /// </summary>
        public static Dictionary<string, object> GetDsl()
        {
            var result = new Dictionary<string, object>();

            // 尝试找到 DSL 规范文件
            string[] possiblePaths = {
                "Assets/MCP/UguiBake/Docs/UI-DSL-全控件版.md",
                "Assets/MCP/UguiBake/Docs/UI-DSL.md",
            };

            string dslContent = null;
            string foundPath = null;

            foreach (var path in possiblePaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                {
                    dslContent = asset.text;
                    foundPath = path;
                    break;
                }
            }

            // 如果精确路径找不到，用 GUID 搜索
            if (dslContent == null)
            {
                var guids = AssetDatabase.FindAssets("UI-DSL t:TextAsset", new[] { "Assets/MCP/UguiBake" });
                if (guids.Length > 0)
                {
                    foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(foundPath);
                    if (asset != null)
                        dslContent = asset.text;
                }
            }

            if (dslContent == null)
                return ErrorResult("未找到 DSL 规范文件");

            // 查找可用画风
            var styles = new List<string>();
            var styleGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/MCP/UguiBake/DSL/画风描述" });
            foreach (var guid in styleGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                styles.Add(name);
            }

            result["success"] = true;
            result["dsl"] = dslContent;
            result["dslPath"] = foundPath;
            result["availableStyles"] = styles;
            result["baseResolution"] = new { width = 942, height = 2048 };

            // 附带缩进 DSL 语法参考（从 AI 工作流文档提取，AI 可直接据此编写轻量 DSL）
            string dslSyntax = ExtractDslSyntax();
            if (dslSyntax != null)
                result["dslSyntax"] = dslSyntax;

            return result;
        }

        /// <summary>
        /// 从 AI 工作流文档中提取「缩进 DSL 语法参考」章节（## 三、 到 ## 四、 之间）。
        /// </summary>
        static string ExtractDslSyntax()
        {
            string content = null;
            const string docPath = "Assets/MCP/UguiBake/Docs/AI-Workflow.md";

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(docPath);
            if (asset != null)
            {
                content = asset.text;
            }
            else
            {
                var guids = AssetDatabase.FindAssets("AI-Workflow t:TextAsset",
                    new[] { "Assets/MCP/UguiBake" });
                if (guids.Length > 0)
                {
                    var a = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (a != null)
                        content = a.text;
                }
            }

            if (string.IsNullOrEmpty(content))
                return null;

            const string startMarker = "## 三、";
            const string endMarker = "## 四、";
            int start = content.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                return null;
            int end = content.IndexOf(endMarker, StringComparison.Ordinal);
            if (end <= start)
                end = content.Length;

            return content.Substring(start, end - start).Trim();
        }

        // ──────────────────── 自动绑定脚本生成 ────────────────────

        /// <summary>
        /// 为烘焙好的预制体生成 C# View 脚本骨架（含字段绑定）。
        /// </summary>
        public static Dictionary<string, object> GenerateViewScript(
            string prefabPath,
            string scriptPath = null,
            string namespaceName = "Game.UI")
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            prefabPath = prefabPath.Replace("\\", "/").Trim();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return ErrorResult($"预制体未找到: {prefabPath}");

            // 收集需要绑定的节点
            var bindings = new List<BindingInfo>();
            CollectBindings(prefab.transform, "", bindings);

            // 生成类名
            string className = prefab.name;
            if (className.EndsWith("Page"))
                className = className.Substring(0, className.Length - 4);
            className += "View";

            // 生成脚本路径
            if (string.IsNullOrEmpty(scriptPath))
            {
                string dir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
                scriptPath = $"{dir}/{className}.cs";
            }

            // 生成脚本内容
            string scriptContent = GenerateScriptContent(className, namespaceName, bindings);

            // 写入文件
            EnsureAssetFolderForPath(scriptPath);
            File.WriteAllText(scriptPath, scriptContent);
            AssetDatabase.ImportAsset(scriptPath);

            result["success"] = true;
            result["scriptPath"] = scriptPath;
            result["className"] = className;
            result["bindingCount"] = bindings.Count;
            result["bindings"] = bindings.ConvertAll(b => new
            {
                fieldName = b.fieldName,
                nodePath = b.nodePath,
                componentType = b.componentType
            });
            result["message"] = $"生成 View 脚本: {className} ({bindings.Count} 个绑定)";

            return result;
        }

        // ──────────────────── 内部工具方法 ────────────────────

        static Dictionary<string, object> ErrorResult(string error)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = error
            };
        }

        static string SaveJsonSnapshot(string jsonContent, string pageName)
        {
            EnsureAssetFolder(DefaultJsonDir);
            string safeName = SanitizeFileName(pageName);
            string path = $"{DefaultJsonDir}/{safeName}.ugui.json";

            // 如果已存在，添加时间戳
            if (File.Exists(path))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = $"{DefaultJsonDir}/{safeName}_{timestamp}.ugui.json";
            }

            // 格式化 JSON
            try
            {
                var parsed = JToken.Parse(jsonContent);
                File.WriteAllText(path, parsed.ToString(Formatting.Indented));
            }
            catch
            {
                File.WriteAllText(path, jsonContent);
            }

            AssetDatabase.ImportAsset(path);
            return path;
        }

        static string BackupPrefab(string prefabPath)
        {
            EnsureAssetFolder(BackupDir);
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = $"{BackupDir}/{name}_{timestamp}.prefab";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                PrefabUtility.SaveAsPrefabAsset(prefab, backupPath);
                return backupPath;
            }
            return null;
        }

        static void EnsureAssetFolder(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return;

            string[] parts = assetPath.Split('/');
            string current = parts[0]; // Assets
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static void EnsureAssetFolderForPath(string assetFilePath)
        {
            assetFilePath = assetFilePath.Replace("\\", "/");
            string dir = Path.GetDirectoryName(assetFilePath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir))
                EnsureAssetFolder(dir);
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static string ExtractPageName(string jsonContent)
        {
            try
            {
                var node = JsonConvert.DeserializeObject<UIDataNode>(jsonContent);
                if (node != null && !string.IsNullOrEmpty(node.name))
                    return node.name;
            }
            catch { }
            return "UnknownPage";
        }

        static Transform FindNodeByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;

            string[] parts = path.Split('/');
            Transform current = root;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var child = current.Find(part);
                if (child == null)
                {
                    // 尝试递归查找
                    child = FindDeep(current, part);
                }
                if (child == null)
                    return null;
                current = child;
            }

            return current;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                    return child;
                var found = FindDeep(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        static int CountAllChildren(Transform t)
        {
            int count = t.childCount;
            for (int i = 0; i < t.childCount; i++)
                count += CountAllChildren(t.GetChild(i));
            return count;
        }

        // ──────────────────── 自动绑定相关 ────────────────────

        class BindingInfo
        {
            public string fieldName;
            public string nodePath;
            public string componentType;
        }

        static void CollectBindings(Transform node, string currentPath, List<BindingInfo> bindings)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                string childPath = string.IsNullOrEmpty(currentPath) ? child.name : $"{currentPath}/{child.name}";

                // 根据命名规范判断是否需要绑定
                string name = child.name;
                string componentType = null;
                string fieldName = null;

                if (name.StartsWith("btn."))
                {
                    componentType = "Button";
                    fieldName = ConvertToFieldName(name.Substring(4));
                }
                else if (name.StartsWith("txt."))
                {
                    componentType = "TMP_Text";
                    fieldName = ConvertToFieldName(name.Substring(4));
                }
                else if (name.StartsWith("img."))
                {
                    componentType = "Image";
                    fieldName = ConvertToFieldName(name.Substring(4));
                }
                else if (name.StartsWith("input."))
                {
                    componentType = "TMP_InputField";
                    fieldName = ConvertToFieldName(name.Substring(6));
                }
                else if (name.StartsWith("scroll."))
                {
                    componentType = "ScrollRect";
                    fieldName = ConvertToFieldName(name.Substring(7));
                }
                else if (name.StartsWith("toggle."))
                {
                    componentType = "Toggle";
                    fieldName = ConvertToFieldName(name.Substring(7));
                }
                else if (name.StartsWith("slider."))
                {
                    componentType = "Slider";
                    fieldName = ConvertToFieldName(name.Substring(7));
                }
                else if (name.StartsWith("dropdown."))
                {
                    componentType = "TMP_Dropdown";
                    fieldName = ConvertToFieldName(name.Substring(9));
                }

                if (componentType != null)
                {
                    bindings.Add(new BindingInfo
                    {
                        fieldName = fieldName,
                        nodePath = childPath,
                        componentType = componentType
                    });
                }

                // 递归子节点
                CollectBindings(child, childPath, bindings);
            }
        }

        static string ConvertToFieldName(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase))
                return "field";
            // PascalCase → camelCase
            return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
        }

        static string GenerateScriptContent(string className, string ns, List<BindingInfo> bindings)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// Auto-generated by UguiBakeBridge. Do not modify manually.");
            sb.AppendLine("// Regenerate via: UguiBakeBridge.GenerateViewScript(prefabPath)");
            sb.AppendLine();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using TMPro;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className} : MonoBehaviour");
            sb.AppendLine("    {");

            // 字段
            foreach (var b in bindings)
            {
                sb.AppendLine($"        [SerializeField] private {b.componentType} {b.fieldName};");
            }

            if (bindings.Count > 0)
                sb.AppendLine();

            // 初始化方法
            sb.AppendLine("        /// <summary>按节点路径自动绑定。在 Awake 或 OnEnable 中调用。</summary>");
            sb.AppendLine("        public void AutoBind()");
            sb.AppendLine("        {");

            if (bindings.Count == 0)
            {
                sb.AppendLine("            // 此预制体无符合 DSL 命名规范的控件节点");
            }
            else
            {
                foreach (var b in bindings)
                {
                    sb.AppendLine($"            {b.fieldName} = transform.Find(\"{b.nodePath}\")?.GetComponent<{b.componentType}>();");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
