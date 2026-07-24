// VfxBakeBridge.cs
// MCP 桥接层：封装 VfxPrefabBakerCore 调用，供 execute_code 或专用 MCP 工具使用。
// 唯一烘焙入口：BakeFromJson（JSON DSL → Prefab 一步到位）。
// 所有方法返回 Dictionary<string, object> 以便 JSON 序列化。
//
// 使用方式（通过 execute_code）：
//   return VfxBakeBridge.BakeFromJson(jsonContent, "Assets/Baked/Explosion_Fire.prefab");
//

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Bake;
using static MCPForUnity.Editor.Bake.BakeFileUtils;

namespace MCPForUnity.Editor.VfxBake
{
    /// <summary>
    /// MCP 桥接层：为 AI / MCP 工具提供简洁的粒子特效烘焙 API。
    /// 所有方法返回 Dictionary&lt;string, object&gt;，可被 execute_code 序列化为 JSON。
    /// </summary>
    public static class VfxBakeBridge
    {
        // ──────────────────── 常量 ────────────────────

        public const string DefaultPrefabDir = "Assets/MCP/VfxBake/Baked/Prefabs";
        public const string DefaultJsonDir = "Assets/MCP/VfxBake/Baked/Json";
        public const string BackupDir = "Assets/MCP/VfxBake/Baked/Prefabs/.backup";

        // ──────────────────── JSON → 预制体（唯一烘焙入口） ────────────────────

        /// <summary>
        /// 从 VFX-DSL JSON 直接烘焙粒子特效预制体（解析 + 烘焙一步到位）。
        /// </summary>
        /// <param name="jsonContent">符合 VFX-DSL 规范的 JSON 字符串</param>
        /// <param name="prefabPath">输出预制体路径</param>
        /// <param name="sourceJsonPath">源 JSON 路径（可选，溯源记录用）</param>
        /// <param name="skipIfUnchanged">增量烘焙：当 JSON 与上次快照一致且预制体已存在时跳过重新烘焙</param>
        public static Dictionary<string, object> BakeFromJson(
            string jsonContent,
            string prefabPath,
            string sourceJsonPath = null,
            bool skipIfUnchanged = true)
        {
            var result = new Dictionary<string, object>();
            var report = BakeReport.Begin(prefabPath, "dsl");
            report.Title = "VfxBake 烘焙报告";

            // 1. 参数校验
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                report.LogError("validate", "jsonContent 不能为空");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult("jsonContent 不能为空");
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

            // 2. 解析 JSON（解析失败时附带 Newtonsoft 提供的行号信息）
            JObject rootJson;
            try
            {
                rootJson = JObject.Parse(jsonContent);
                report.LogInfo("parse", "JSON 解析成功");
            }
            catch (JsonException e)
            {
                report.LogError("parse", $"JSON 解析失败: {e.Message}");
                report.Finish(false, 0);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult($"JSON 解析失败: {e.Message}");
            }

            // 3. 解析特效名与系统数（用于快照命名与报告）
            string vfxName = rootJson["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(vfxName))
                vfxName = Path.GetFileNameWithoutExtension(prefabPath);
            int systemCount = CountSystems(rootJson["systems"] as JArray);
            report.LogInfo("parse", $"特效: {vfxName}, 粒子系统数: {systemCount}");

            // 4. 确保输出目录存在
            EnsureAssetFolderForPath(prefabPath);

            // 5. 增量烘焙：JSON 未变更且预制体已存在时跳过重新烘焙。
            if (skipIfUnchanged && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                string prevSnapshot = FindLatestJsonSnapshot(vfxName, DefaultJsonDir, "vfx");
                if (prevSnapshot != null && IsJsonEquivalent(jsonContent, File.ReadAllText(prevSnapshot)))
                {
                    report.LogInfo("bake", "JSON 与上次快照一致，跳过烘焙（增量模式）");
                    report.Finish(true, systemCount);
                    result["bakeReport"] = report.ToSummaryString();
                    result["bakeElapsedMs"] = report.GetTotalElapsedMs();
                    result["success"] = true;
                    result["prefabPath"] = prefabPath;
                    result["vfxName"] = vfxName;
                    result["systemCount"] = systemCount;
                    result["skipped"] = true;
                    result["message"] = $"JSON 未变更，跳过烘焙: {vfxName}";
                    if (!string.IsNullOrEmpty(sourceJsonPath))
                        result["sourceJson"] = sourceJsonPath;
                    return result;
                }
            }

            // 6. 保存 JSON 快照
            string snapshotPath = null;
            try
            {
                snapshotPath = SaveJsonSnapshot(jsonContent, vfxName, DefaultJsonDir, "vfx");
                report.LogInfo("bake", $"JSON 快照已保存: {snapshotPath}");
            }
            catch (Exception e)
            {
                report.LogWarning("bake", $"JSON 快照保存失败: {e.Message}");
                Debug.LogWarning($"[VfxBakeBridge] JSON 快照保存失败（不影响烘焙）: {e.Message}");
            }

            // 7. 备份现有预制体
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
                    Debug.LogWarning($"[VfxBakeBridge] 预制体备份失败（不影响烘焙）: {e.Message}");
                }
            }

            // 8. 执行烘焙
            report.LogInfo("bake", $"开始烘焙 → {prefabPath}");
            string bakeError;

            bool success;
            try
            {
                success = VfxPrefabBakerCore.TryBakeJsonStringToPrefab(
                    jsonContent, prefabPath, out bakeError);
            }
            catch (Exception e)
            {
                report.LogError("bake", $"烘焙异常: {e.Message}");
                report.Finish(false, systemCount);
                result["bakeReport"] = report.ToSummaryString();
                return ErrorResult($"烘焙异常: {e.Message}");
            }

            // 9. 构建结果
            report.Finish(success, systemCount);
            result["bakeReport"] = report.ToSummaryString();
            result["bakeElapsedMs"] = report.GetTotalElapsedMs();
            result["success"] = success;
            result["prefabPath"] = prefabPath;
            result["vfxName"] = vfxName;
            result["systemCount"] = systemCount;
            if (!string.IsNullOrEmpty(sourceJsonPath))
                result["sourceJson"] = sourceJsonPath;
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
                result["message"] = $"成功烘焙 '{vfxName}' → {prefabPath}";
                AssetDatabase.ImportAsset(prefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return result;
        }

        // ──────────────────── 列表 / 删除 ────────────────────

        /// <summary>
        /// 列出已烘焙的特效预制体。
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

                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                info["systemCount"] = systems.Length;
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
        /// 删除预制体（移入系统回收站，并删除其 JSON 快照）。
        /// </summary>
        public static Dictionary<string, object> DeleteBaked(string prefabPath)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(prefabPath))
                return ErrorResult("prefabPath 不能为空");

            prefabPath = prefabPath.Replace("\\", "/").Trim();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return ErrorResult($"预制体未找到: {prefabPath}");

            bool deleted = AssetDatabase.MoveAssetToTrash(prefabPath);

            string vfxName = Path.GetFileNameWithoutExtension(prefabPath);
            string jsonSnapshot = $"{DefaultJsonDir}/{vfxName}.vfx.json";
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(jsonSnapshot) != null)
                AssetDatabase.MoveAssetToTrash(jsonSnapshot);

            result["success"] = deleted;
            result["prefabPath"] = prefabPath;
            result["message"] = deleted
                ? $"已删除 {prefabPath}"
                : $"删除失败: {prefabPath}";

            return result;
        }

        // ──────────────────── 贴图列表 ────────────────────

        /// <summary>
        /// 列出指定目录（递归）下可用的特效贴图，供 AI 在生成 renderer.texture 时选用。
        /// </summary>
        /// <param name="searchDir">Assets 相对目录，默认为工程特效贴图目录</param>
        public static Dictionary<string, object> ListTextures(string searchDir = null)
        {
            var result = new Dictionary<string, object>();
            searchDir = (searchDir ?? "Assets/GameEffect/Texture").Replace("\\", "/").Trim();

            if (!AssetDatabase.IsValidFolder(searchDir))
            {
                result["success"] = true;
                result["textures"] = new List<object>();
                result["searchDir"] = searchDir;
                result["message"] = $"目录不存在: {searchDir}";
                return result;
            }

            var textures = new List<object>();
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { searchDir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                textures.Add(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["name"] = tex.name,
                    ["width"] = tex.width,
                    ["height"] = tex.height,
                });
            }

            // 按名称排序，便于阅读
            textures.Sort((a, b) =>
                string.Compare(((Dictionary<string, object>)a)["name"]?.ToString(),
                               ((Dictionary<string, object>)b)["name"]?.ToString(),
                               StringComparison.OrdinalIgnoreCase));

            result["success"] = true;
            result["textures"] = textures;
            result["total"] = textures.Count;
            result["searchDir"] = searchDir;
            result["message"] = $"在 {searchDir} 找到 {textures.Count} 张贴图";

            return result;
        }

        // ──────────────────── DSL 规范获取 ────────────────────

        /// <summary>
        /// 获取 VFX-DSL JSON 规范文本（从 AI-Workflow 文档读取）。
        /// AI 生成 JSON 前应调用此方法获取规范。
        /// </summary>
        public static Dictionary<string, object> GetSpec()
        {
            var result = new Dictionary<string, object>();

            string specContent = null;
            string foundPath = null;

            const string docPath = "Assets/MCP/VfxBake/Docs/AI-Workflow.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(docPath);
            if (asset != null)
            {
                specContent = asset.text;
                foundPath = docPath;
            }
            else
            {
                // 兜底：全工程搜索 VfxBake 目录下的 AI-Workflow 文档。
                // 规范文档随包分发（Editor/VfxBake/Docs/），包被装在 Packages 下时
                // 不在 Assets/MCP 目录内，因此这里不限定搜索目录、按路径过滤。
                var guids = AssetDatabase.FindAssets("AI-Workflow t:TextAsset");
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path == null || path.IndexOf("VfxBake", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var a = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (a != null)
                    {
                        foundPath = path;
                        specContent = a.text;
                        break;
                    }
                }
            }

            if (specContent == null)
                return ErrorResult("未找到 VFX-DSL 规范文件");

            result["success"] = true;
            result["spec"] = specContent;
            result["specPath"] = foundPath;

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

        /// <summary>递归统计 DSL 中的粒子系统总数（含 children 嵌套）。</summary>
        static int CountSystems(JArray systems)
        {
            if (systems == null) return 0;
            int count = 0;
            foreach (var sys in systems)
            {
                count++;
                if (sys is JObject obj)
                    count += CountSystems(obj["children"] as JArray);
            }
            return count;
        }

        static int CountAllChildren(Transform t)
        {
            int count = t.childCount;
            for (int i = 0; i < t.childCount; i++)
                count += CountAllChildren(t.GetChild(i));
            return count;
        }
    }
}
