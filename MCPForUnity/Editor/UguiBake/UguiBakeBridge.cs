// UguiBakeBridge.cs
// MCP 桥接层：封装 UguiPrefabBakerCore 调用，供 execute_code 或专用 MCP 工具使用。
// 唯一烘焙入口：BakeFromHtml（HTML → JSON → Prefab 一步到位）。
// 所有方法返回 Dictionary<string, object> 以便 JSON 序列化。
//
// 使用方式（通过 execute_code）：
//   return UguiBakeBridge.BakeFromHtml(htmlContent, "Assets/Baked/MyPage.prefab", 942, 2048);
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using MCPForUnity.Runtime.UguiBake;
using MCPForUnity.Editor.Bake;
using static MCPForUnity.Editor.Bake.BakeFileUtils;

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

        // ──────────────────── HTML → 预制体（唯一烘焙入口） ────────────────────

        /// <summary>
        /// 从 HTML 直接烘焙预制体（解析 + 烘焙一步到位）。
        /// 返回结果中包含 htmlContent 字段，供调用方获取生成的 HTML。
        /// </summary>
        /// <param name="htmlContent">符合 UI-DSL 规范的 HTML 字符串</param>
        /// <param name="prefabPath">输出预制体路径</param>
        /// <param name="width">基准分辨率宽</param>
        /// <param name="height">基准分辨率高</param>
        /// <param name="useTMP">使用 TextMeshPro</param>
        /// <param name="sourceHtmlPath">源 HTML 路径（用于图片解析）</param>
        /// <param name="templatePrefabPath">模板预制体路径（可选，根节点需有 Canvas）</param>
        /// <param name="fontPath">字体资源路径</param>
        /// <param name="skipIfUnchanged">增量烘焙：当 JSON 与上次快照一致且预制体已存在时跳过重新烘焙</param>
        public static Dictionary<string, object> BakeFromHtml(
            string htmlContent,
            string prefabPath,
            int width = 942,
            int height = 2048,
            bool useTMP = true,
            string sourceHtmlPath = null,
            string templatePrefabPath = null,
            string fontPath = null,
            bool skipIfUnchanged = true,
            string userInputContent = null,
            string userInputExtension = "txt",
            string userInputSourcePath = null,
            string attachScript = null)
        {
            var result = new Dictionary<string, object>();
            var report = BakeReport.Begin(prefabPath, "html");

            // 1. 参数校验
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                report.LogError("validate", "htmlContent 不能为空");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("htmlContent 不能为空");
            }
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                report.LogError("validate", "prefabPath 不能为空");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("prefabPath 不能为空");
            }

            prefabPath = prefabPath.Replace("\\", "/").Trim();
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                report.LogError("validate", "prefabPath 必须以 .prefab 结尾");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("prefabPath 必须以 .prefab 结尾");
            }
            if (!prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                report.LogError("validate", "prefabPath 必须以 Assets/ 开头");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("prefabPath 必须以 Assets/ 开头");
            }

            // 2. 解析 HTML → JSON
            string jsonContent;
            try
            {
                jsonContent = UguiHtmlParser.Parse(htmlContent, width, height);
                report.LogInfo("parse", "HTML 解析成功");
            }
            catch (Exception e)
            {
                report.LogError("parse", $"HTML 解析失败: {e.Message}");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult($"HTML 解析失败: {e.Message}");
            }

            // 3. JSON 校验
            if (!UguiPrefabBakerCore.TryValidateJson(jsonContent, out string validateError))
            {
                report.LogError("validate", "JSON 校验失败: " + validateError);
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("JSON 校验失败: " + validateError);
            }
            report.LogInfo("validate", "JSON 校验通过");

            // 4. 解析根节点名（用于快照命名）
            string pageName = "Unknown";
            int nodeCount = 0;
            try
            {
                var rootNode = JsonConvert.DeserializeObject<UIDataNode>(jsonContent);
                if (rootNode != null && !string.IsNullOrEmpty(rootNode.name))
                    pageName = rootNode.name;
                nodeCount = CountNodes(rootNode);
            }
            catch { /* 忽略，用默认名 */ }
            report.LogInfo("parse", $"页面: {pageName}, 节点数: {nodeCount}");

            // 4.2 确保输出目录存在，并把用户原始输入与转换后的 HTML 固化到预制体同目录。
            EnsureAssetFolderForPath(prefabPath);
            string convertedHtmlPath = null;
            string userInputPath = null;
            try
            {
                SaveBakeSourceFiles(prefabPath, htmlContent, userInputContent, userInputExtension, userInputSourcePath,
                    out convertedHtmlPath, out userInputPath);
                if (!string.IsNullOrEmpty(convertedHtmlPath))
                    report.LogInfo("bake", $"转换 HTML 已保存: {convertedHtmlPath}");
                if (!string.IsNullOrEmpty(userInputPath))
                    report.LogInfo("bake", $"用户输入已保存: {userInputPath}");
            }
            catch (Exception e)
            {
                report.LogWarning("bake", $"输入/HTML 保存失败: {e.Message}");
                Debug.LogWarning($"[UguiBakeBridge] 输入/HTML 保存失败（不影响烘焙）: {e.Message}");
            }

            // 4.1 解析模板预制体：参数优先，未指定时回退到 UguiBakeConfig 默认模板。
            // 提前解析以便增量跳过时能识别「预制体尚未应用模板」的情况。
            GameObject templatePrefab = null;
            string resolvedTemplatePath = templatePrefabPath;
            if (string.IsNullOrEmpty(resolvedTemplatePath))
            {
                var bakeConfig = FindBakeConfig();
                if (bakeConfig != null && bakeConfig.defaultTemplatePrefab != null)
                {
                    templatePrefab = bakeConfig.defaultTemplatePrefab;
                    resolvedTemplatePath = AssetDatabase.GetAssetPath(templatePrefab);
                    report.LogInfo("bake", $"使用配置默认模板: {resolvedTemplatePath}");
                }
            }
            if (templatePrefab == null && !string.IsNullOrEmpty(resolvedTemplatePath))
            {
                templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(resolvedTemplatePath);
                if (templatePrefab == null)
                {
                    report.LogError("bake", $"模板预制体未找到: {resolvedTemplatePath}");
                    report.Finish(false, nodeCount);
                    result["bakeReport"] = report.ToSummaryString();
                    return ErrorResult($"模板预制体未找到: {resolvedTemplatePath}");
                }
            }

            // 4.5 增量烘焙：JSON 未变更时跳过重新烘焙。
            // 例外：将应用模板但现有预制体根节点无 Canvas（即上次未按模板烘焙）时不跳过。
            if (skipIfUnchanged && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                bool templateMissing = templatePrefab != null && existingPrefab.GetComponent<Canvas>() == null;
                if (!templateMissing)
                {
                    string prevSnapshot = FindLatestJsonSnapshot(pageName, DefaultJsonDir, "ugui");
                    if (prevSnapshot != null && IsJsonEquivalent(jsonContent, File.ReadAllText(prevSnapshot)))
                    {
                        report.LogInfo("bake", "JSON 与上次快照一致，跳过烘焙（增量模式）");
                        report.Finish(true, nodeCount);
                        result["bakeReport"] = report.ToSummaryString();
                        result["bakeElapsedMs"] = report.GetTotalElapsedMs();
                        result["success"] = true;
                        result["prefabPath"] = prefabPath;
                        result["pageName"] = pageName;
                        result["nodeCount"] = nodeCount;
                        result["skipped"] = true;
                        result["message"] = $"JSON 未变更，跳过烘焙: {pageName}";
                        result["htmlContent"] = htmlContent;
                        if (!string.IsNullOrEmpty(convertedHtmlPath))
                            result["convertedHtmlPath"] = convertedHtmlPath;
                        if (!string.IsNullOrEmpty(userInputPath))
                            result["userInputPath"] = userInputPath;
                        return result;
                    }
                }
            }

            // 5. 保存 JSON 快照
            string snapshotPath = null;
            if (true) // 始终保存快照
            {
                try
                {
                    snapshotPath = SaveJsonSnapshot(jsonContent, pageName, DefaultJsonDir, "ugui");
                    report.LogInfo("bake", $"JSON 快照已保存: {snapshotPath}");
                }
                catch (Exception e)
                {
                    report.LogWarning("bake", $"JSON 快照保存失败: {e.Message}");
                    Debug.LogWarning($"[UguiBakeBridge] JSON 快照保存失败（不影响烘焙）: {e.Message}");
                }
            }

            // 6. 备份现有预制体
            string backupPath = null;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                try
                {
                    backupPath = BackupPrefab(prefabPath, BackupDir);
                    report.LogInfo("bake", $"已备份旧预制体: {backupPath}");
                }
                catch (Exception e)
                {
                    report.LogWarning("bake", $"预制体备份失败: {e.Message}");
                    Debug.LogWarning($"[UguiBakeBridge] 预制体备份失败（不影响烘焙）: {e.Message}");
                }
            }

            // 7. 确保输出目录存在
            EnsureAssetFolderForPath(prefabPath);

            // 8. 加载字体
            TMP_FontAsset tmpFont = null;
            Font legacyFont = null;
            LoadFonts(fontPath, useTMP, out tmpFont, out legacyFont);

            // 10. 执行烘焙
            report.LogInfo("bake", $"开始烘焙 → {prefabPath}");
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
                    true,
                    tmpFont,
                    legacyFont);
            }
            catch (Exception e)
            {
                report.LogError("bake", $"烘焙异常: {e.Message}");
                report.Finish(false, nodeCount);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult($"烘焙异常: {e.Message}");
            }

            // 11. 构建结果
            report.Finish(success, nodeCount);
            result["bakeReport"] = report.ToSummaryString();
            result["bakeElapsedMs"] = report.GetTotalElapsedMs();
            result["success"] = success;
            result["prefabPath"] = prefabPath;
            result["pageName"] = pageName;
            result["nodeCount"] = nodeCount;
            result["resolution"] = new { width, height };
            result["useTMP"] = useTMP;
            result["htmlContent"] = htmlContent;
            if (!string.IsNullOrEmpty(convertedHtmlPath))
                result["convertedHtmlPath"] = convertedHtmlPath;
            if (!string.IsNullOrEmpty(userInputPath))
                result["userInputPath"] = userInputPath;
            if (!string.IsNullOrEmpty(fontPath))
                result["fontPath"] = fontPath;
            if (templatePrefab != null)
                result["templatePrefab"] = resolvedTemplatePath;
            if (tmpFont != null)
                result["tmpFont"] = tmpFont.name;
            if (legacyFont != null)
                result["legacyFont"] = legacyFont.name;
            if (snapshotPath != null)
                result["jsonSnapshot"] = snapshotPath;
            if (backupPath != null)
                result["backup"] = backupPath;

            if (!success)
            {
                report.LogError("bake", bakeError ?? "未知烘焙错误");
                result["error"] = bakeError ?? "未知烘焙错误";
            }
            else
            {
                result["message"] = $"成功烘焙 '{pageName}' → {prefabPath}";
                AssetDatabase.Refresh();

                // 烘焙后挂载脚本
                if (!string.IsNullOrWhiteSpace(attachScript))
                {
                    var attachResult = AttachScriptToPrefab(prefabPath, attachScript);
                    result["attachScript"] = attachResult;
                }
            }

            return result;
        }

        // ──────────────────── 挂载脚本 ────────────────────

        /// <summary>
        /// 将指定 MonoBehaviour 脚本挂载到预制体根节点。
        /// 类型必须已编译（项目程序集中存在）。
        /// </summary>
        /// <param name="prefabPath">预制体 Assets 相对路径</param>
        /// <param name="typeName">类型短名或全限定名（如 "SkillEditorPanel" 或 "Game.Battle2D.SkillEditorPanel"）</param>
        /// <returns>操作结果字典</returns>
        public static Dictionary<string, object> AttachScriptToPrefab(string prefabPath, string typeName)
        {
            var result = new Dictionary<string, object>();

            // 解析类型
            if (!MCPForUnity.Editor.Helpers.UnityTypeResolver.TryResolve(
                    typeName, out Type scriptType, out string resolveError, typeof(Component)))
            {
                result["success"] = false;
                result["message"] = $"无法解析脚本类型 '{typeName}': {resolveError}";
                return result;
            }

            // 打开预制体进行编辑
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null)
            {
                result["success"] = false;
                result["message"] = $"无法加载预制体: {prefabPath}";
                return result;
            }

            try
            {
                // 检查是否已挂载
                var existing = contents.GetComponent(scriptType);
                if (existing != null)
                {
                    result["success"] = true;
                    result["message"] = $"脚本 '{scriptType.Name}' 已存在于预制体根节点，跳过重复挂载。";
                    result["alreadyAttached"] = true;
                    return result;
                }

                contents.AddComponent(scriptType);
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);

                result["success"] = true;
                result["message"] = $"成功挂载 '{scriptType.FullName}' → {prefabPath}";
                result["attachedType"] = scriptType.FullName;
            }
            catch (Exception e)
            {
                result["success"] = false;
                result["message"] = $"挂载脚本失败: {e.Message}";
            }
            finally
            {
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

            bool deleted = AssetDatabase.DeleteAsset(prefabPath);

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

        // ──────────────────── HTML 规范获取 ────────────────────

        /// <summary>
        /// 获取 UI-DSL HTML 规范文本（从 AI-Workflow 文档读取）。
        /// AI 生成 HTML 前应调用此方法获取规范。
        /// </summary>
        public static Dictionary<string, object> GetSpec()
        {
            var result = new Dictionary<string, object>();

            string specContent = null;
            string foundPath = null;

            const string docPath = "Assets/MCP/UguiBake/Docs/AI-Workflow.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(docPath);
            if (asset != null)
            {
                specContent = asset.text;
                foundPath = docPath;
            }
            else
            {
                var guids = AssetDatabase.FindAssets("AI-Workflow t:TextAsset",
                    new[] { "Assets/MCP/UguiBake" });
                if (guids.Length > 0)
                {
                    foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var a = AssetDatabase.LoadAssetAtPath<TextAsset>(foundPath);
                    if (a != null)
                        specContent = a.text;
                }
            }

            if (specContent == null)
                return ErrorResult("未找到 HTML 规范文件");

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
            result["spec"] = specContent;
            result["specPath"] = foundPath;
            result["availableStyles"] = styles;
            result["baseResolution"] = new { width = 942, height = 2048 };

            return result;
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

            var bindings = new List<BindingInfo>();
            CollectBindings(prefab.transform, "", bindings);

            string className = prefab.name;
            if (className.EndsWith("Page"))
                className = className.Substring(0, className.Length - 4);
            className += "View";

            if (string.IsNullOrEmpty(scriptPath))
            {
                string dir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
                scriptPath = $"{dir}/{className}.cs";
            }

            string scriptContent = GenerateScriptContent(className, namespaceName, bindings);

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

        // ──────────────────── 字体加载 ────────────────────

        /// <summary>
        /// 加载字体资源。优先从 fontPath 加载，其次从 UguiBakeConfig 配置加载默认字体。
        /// </summary>
        static void LoadFonts(string fontPath, bool useTMP, out TMP_FontAsset tmpFont, out Font legacyFont)
        {
            tmpFont = null;
            legacyFont = null;

            if (!string.IsNullOrEmpty(fontPath))
            {
                fontPath = fontPath.Replace("\\", "/").Trim();
                if (useTMP)
                {
                    tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                    if (tmpFont == null)
                        Debug.LogWarning($"[UguiBakeBridge] 无法加载 TMP 字体: {fontPath}");
                }
                else
                {
                    legacyFont = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                    if (legacyFont == null)
                        Debug.LogWarning($"[UguiBakeBridge] 无法加载旧版字体: {fontPath}");
                }
            }

            if ((useTMP && tmpFont == null) || (!useTMP && legacyFont == null))
            {
                var config = FindBakeConfig();
                if (config != null)
                {
                    if (useTMP && tmpFont == null)
                        tmpFont = config.defaultTmpFont;
                    if (!useTMP && legacyFont == null)
                        legacyFont = config.defaultLegacyFont;
                }
            }
        }

        /// <summary>查找工程中的 UguiBakeConfig 配置资源。</summary>
        static UguiBakeConfig FindBakeConfig()
        {
            var guids = AssetDatabase.FindAssets("t:UguiBakeConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<UguiBakeConfig>(path);
            }
            return null;
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

        static void SaveBakeSourceFiles(
            string prefabPath,
            string convertedHtmlContent,
            string userInputContent,
            string userInputExtension,
            string userInputSourcePath,
            out string convertedHtmlPath,
            out string userInputPath)
        {
            convertedHtmlPath = null;
            userInputPath = null;

            string prefabDir = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(prefabDir))
                return;

            EnsureAssetFolder(prefabDir);

            string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(prefabPath));
            convertedHtmlPath = $"{prefabDir}/{baseName}.ugui.html";
            File.WriteAllText(convertedHtmlPath, convertedHtmlContent ?? string.Empty, Encoding.UTF8);
            AssetDatabase.ImportAsset(convertedHtmlPath);

            if (!string.IsNullOrEmpty(userInputContent))
            {
                string ext = SanitizeExtension(userInputExtension);
                userInputPath = $"{prefabDir}/{baseName}.input.{ext}";
                File.WriteAllText(userInputPath, userInputContent, Encoding.UTF8);
                AssetDatabase.ImportAsset(userInputPath);
                return;
            }

            if (!string.IsNullOrEmpty(userInputSourcePath))
            {
                string normalizedSource = userInputSourcePath.Replace("\\", "/").Trim();
                if (File.Exists(normalizedSource))
                {
                    string ext = SanitizeExtension(Path.GetExtension(normalizedSource));
                    userInputPath = $"{prefabDir}/{baseName}.input.{ext}";
                    File.Copy(normalizedSource, userInputPath, true);
                    AssetDatabase.ImportAsset(userInputPath);
                }
            }
        }

        static string SanitizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return "txt";
            extension = extension.Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension)) return "txt";

            foreach (char c in Path.GetInvalidFileNameChars())
                extension = extension.Replace(c, '_');
            extension = extension.Replace('/', '_').Replace('\\', '_');
            return string.IsNullOrWhiteSpace(extension) ? "txt" : extension;
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

                CollectBindings(child, childPath, bindings);
            }
        }

        static string ConvertToFieldName(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase))
                return "field";
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

            foreach (var b in bindings)
            {
                sb.AppendLine($"        [SerializeField] private {b.componentType} {b.fieldName};");
            }

            if (bindings.Count > 0)
                sb.AppendLine();

            sb.AppendLine("        /// <summary>按节点路径自动绑定。在 Awake 或 OnEnable 中调用。</summary>");
            sb.AppendLine("        public void AutoBind()");
            sb.AppendLine("        {");

            if (bindings.Count == 0)
            {
                sb.AppendLine("            // 此预制体无符合命名规范的控件节点");
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
