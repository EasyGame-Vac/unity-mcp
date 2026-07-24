// BakeFileUtils.cs
// 烘焙管线公共文件工具：JSON 快照、增量比较、预制体备份、目录创建、文件名清洗。
// 管线无关，UguiBake / VfxBake 等所有烘焙管线共用（原 UguiBakeBridge / VfxBakeBridge 中的重复实现）。

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Bake
{
    /// <summary>
    /// 烘焙管线公共文件工具。
    /// <para>
    /// 快照文件命名约定：<c>{jsonDir}/{safeName}.{snapshotTag}.json</c>，
    /// 同名冲突时追加时间戳 <c>{safeName}_{yyyyMMdd_HHmmss}.{snapshotTag}.json</c>。
    /// </para>
    /// </summary>
    public static class BakeFileUtils
    {
        /// <summary>
        /// 保存 JSON 快照（用于增量烘焙对比）。已存在同名快照时追加时间戳后缀。
        /// </summary>
        /// <param name="jsonContent">JSON 内容（尝试格式化后写入，解析失败则原样写入）</param>
        /// <param name="assetName">资产名（页面名 / 特效名等）</param>
        /// <param name="jsonDir">快照目录（Assets 相对路径）</param>
        /// <param name="snapshotTag">快照标签（如 ugui / vfx），用于文件名中段</param>
        /// <returns>实际写入的快照路径</returns>
        public static string SaveJsonSnapshot(string jsonContent, string assetName, string jsonDir, string snapshotTag)
        {
            EnsureAssetFolder(jsonDir);
            string safeName = SanitizeFileName(assetName);
            string path = $"{jsonDir}/{safeName}.{snapshotTag}.json";

            if (File.Exists(path))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = $"{jsonDir}/{safeName}_{timestamp}.{snapshotTag}.json";
            }

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

        /// <summary>查找指定资产名的最新 JSON 快照路径（无则返回 null）。</summary>
        public static string FindLatestJsonSnapshot(string assetName, string jsonDir, string snapshotTag)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            string safeName = SanitizeFileName(assetName);
            string exactPath = $"{jsonDir}/{safeName}.{snapshotTag}.json";
            if (File.Exists(exactPath))
                return exactPath;

            // 查找带时间戳的快照（name_YYYYMMDD_HHMMSS.{tag}.json）
            if (!AssetDatabase.IsValidFolder(jsonDir))
                return null;

            string prefix = $"{safeName}_";
            var guids = AssetDatabase.FindAssets("t:TextAsset", new[] { jsonDir });
            string latestPath = null;
            DateTime latestTime = DateTime.MinValue;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName != null && fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName.EndsWith($".{snapshotTag}", StringComparison.Ordinal))
                {
                    var info = new FileInfo(path);
                    if (info.LastWriteTime > latestTime)
                    {
                        latestTime = info.LastWriteTime;
                        latestPath = path;
                    }
                }
            }
            return latestPath;
        }

        /// <summary>语义级比较两段 JSON 是否等价（忽略格式差异；解析失败退回字符串比较）。</summary>
        public static bool IsJsonEquivalent(string jsonA, string jsonB)
        {
            if (string.IsNullOrEmpty(jsonA) || string.IsNullOrEmpty(jsonB))
                return jsonA == jsonB;

            try
            {
                var tokenA = JToken.Parse(jsonA);
                var tokenB = JToken.Parse(jsonB);
                return JToken.DeepEquals(tokenA, tokenB);
            }
            catch
            {
                return string.Equals(jsonA?.Trim(), jsonB?.Trim(), StringComparison.Ordinal);
            }
        }

        /// <summary>备份既有预制体到备份目录（追加时间戳）。预制体不存在时返回 null。</summary>
        public static string BackupPrefab(string prefabPath, string backupDir)
        {
            EnsureAssetFolder(backupDir);
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = $"{backupDir}/{name}_{timestamp}.prefab";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                PrefabUtility.SaveAsPrefabAsset(prefab, backupPath);
                return backupPath;
            }
            return null;
        }

        /// <summary>确保 Assets 相对目录存在（逐级创建，非 Assets 路径直接忽略）。</summary>
        public static void EnsureAssetFolder(string assetPath)
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

        /// <summary>确保指定资产文件所在的目录存在。</summary>
        public static void EnsureAssetFolderForPath(string assetFilePath)
        {
            assetFilePath = assetFilePath.Replace("\\", "/");
            string dir = Path.GetDirectoryName(assetFilePath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir))
                EnsureAssetFolder(dir);
        }

        /// <summary>清洗文件名：非法字符替换为下划线，空名回退 "Unknown"。</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
