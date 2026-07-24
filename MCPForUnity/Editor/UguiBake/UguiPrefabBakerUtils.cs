using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using MCPForUnity.Runtime.UguiBake;

namespace MCPForUnity.Editor.UguiBake
{
    public static class UguiPrefabBakerUtils
    {
        public static bool TryAbsolutePathToAssetsPath(string absolutePath, out string assetsPath)
        {
            assetsPath = null;
            try
            {
                string fullAbs = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullData = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!fullAbs.StartsWith(fullData, StringComparison.OrdinalIgnoreCase))
                    return false;
                string tail = fullAbs.Length > fullData.Length ? fullAbs.Substring(fullData.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : "";
                assetsPath = string.IsNullOrEmpty(tail) ? "Assets" : "Assets/" + tail.Replace("\\", "/");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string AssetsPathToAbsolute(string assetsPath)
        {
            if (string.IsNullOrEmpty(assetsPath))
                return null;
            assetsPath = assetsPath.Replace("\\", "/");
            if (!assetsPath.StartsWith("Assets/", StringComparison.Ordinal))
                return null;
            return Path.GetFullPath(Path.Combine(Application.dataPath, assetsPath.Substring(7)));
        }

        public static string CombineAssetPath(string assetsDir, string fileName)
        {
            return (assetsDir.TrimEnd('/') + "/" + fileName).Replace("\\", "/");
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "UI_Baked";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        public static string AbsoluteDiskPathToAssetsPath(string diskPath)
        {
            try
            {
                string full = Path.GetFullPath(diskPath);
                if (!TryAbsolutePathToAssetsPath(full, out string rel))
                    return null;
                return rel.Replace("\\", "/");
            }
            catch
            {
                return null;
            }
        }

        public static bool NormalizeDefaultDir(ref string dir, string prefsKey, out string err)
        {
            err = null;
            if (string.IsNullOrWhiteSpace(dir))
            {
                err = "请先设置默认输出目录（步骤二 全局设置）。";
                return false;
            }

            dir = dir.Trim().Replace("\\", "/");
            if (!dir.StartsWith("Assets/", StringComparison.Ordinal))
            {
                err = "输出目录须以 Assets/ 开头。";
                return false;
            }

            EditorPrefs.SetString(prefsKey, dir);
            return true;
        }

        public static void PickAssetsSubfolder(ref string assetsFolder, string prefsKey)
        {
            string abs = EditorUtility.OpenFolderPanel("须在工程 Assets 目录下", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs))
                return;
            if (!TryAbsolutePathToAssetsPath(abs, out string rel))
            {
                EditorUtility.DisplayDialog("UguiBake", "请选择位于本工程 Assets 文件夹内的目录。", "确定");
                return;
            }

            assetsFolder = rel.Replace("\\", "/");
            EditorPrefs.SetString(prefsKey, assetsFolder);
        }

        public static void EnsureAssetFoldersForPath(string assetPath)
        {
            string dir = AssetPathGetDirectory(assetPath);
            if (string.IsNullOrEmpty(dir) || dir == "Assets")
                return;
            if (AssetDatabase.IsValidFolder(dir))
                return;

            bool created = false;
            string[] parts = dir.Split('/');
            string cur = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                    continue;
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                    created = true;
                }
                cur = next;
            }

            if (created)
                AssetDatabase.Refresh();
        }

        public static string AssetPathGetDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            assetPath = assetPath.Replace("\\", "/");
            int i = assetPath.LastIndexOf('/');
            if (i <= 0)
                return null;
            return assetPath.Substring(0, i);
        }

        static string StripImageExtension(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return assetPath;
            return assetPath
                .Replace(".png", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".jpg", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".jpeg", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".gif", "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 将 HTML→JSON 的 <c>image</c> 字段解析为 Unity <c>Assets/</c> 下资源路径（无扩展名）。
        /// 支持：Assets/ 绝对路径、<c>../DSL/</c> / <c>../../DSL/</c> 散图、相对 <paramref name="sourceHtmlAssetPath"/> 的 url。
        /// </summary>
        public static string ResolveImageAssetPath(string imageUrl, string sourceHtmlAssetPath = null)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;
            string path = imageUrl.Trim().Replace('\\', '/');

            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return StripImageExtension(path);

            int dslMarker = path.IndexOf("DSL/", StringComparison.Ordinal);
            if (dslMarker >= 0)
            {
                string dslRel = path.Substring(dslMarker);
                return StripImageExtension("Assets/MCP/UguiBake/" + dslRel);
            }

            if (!string.IsNullOrEmpty(sourceHtmlAssetPath))
            {
                string htmlDir = AssetPathGetDirectory(sourceHtmlAssetPath.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(htmlDir))
                    return StripImageExtension(CombineAssetPath(htmlDir, path.TrimStart('/')));
            }

            return StripImageExtension(path);
        }

        /// <summary>
        /// 按解析后的 Assets 路径加载 Sprite（无扩展名）。
        /// 导入为 Sprite 的 PNG 在 Unity 中多为 Texture 的子资源，须用 <see cref="AssetDatabase.LoadAllAssetsAtPath"/>。
        /// </summary>
        public static Sprite TryLoadSpriteAtAssetPath(string assetPathWithoutExtension)
        {
            if (string.IsNullOrEmpty(assetPathWithoutExtension)) return null;

            string basePath = assetPathWithoutExtension.Replace('\\', '/').TrimEnd('/');

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(basePath);
            if (sprite != null) return sprite;

            foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".gif" })
            {
                string pathWithExt = basePath + ext;

                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pathWithExt);
                if (sprite != null) return sprite;

                UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(pathWithExt);
                if (main is Sprite mainSprite) return mainSprite;

                UnityEngine.Object[] subs = AssetDatabase.LoadAllAssetsAtPath(pathWithExt);
                if (subs == null) continue;
                for (int i = 0; i < subs.Length; i++)
                {
                    if (subs[i] is Sprite subSprite) return subSprite;
                }
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            if (tex != null)
            {
                Debug.LogWarning(
                    $"[UguiBake] 纹理「{basePath}」未找到可序列化的 Sprite 子资源，请将 Texture Type 设为 Sprite (2D and UI)。临时 Sprite 不会写入预制体。");
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }

            return null;
        }

        /// <summary>
        /// UGUI <see cref="Image"/> 在 <c>sprite == null</c> 时往往不生成可绘制网格，<see cref="UguiLinearGradient"/> 与纯色底都会失效。
        /// 烘焙时对需自绘颜色的 Image 挂上白块 Sprite（<see cref="Texture2D.whiteTexture"/>）。
        /// </summary>
        static Sprite _bakeWhiteSprite;

        public static void EnsureUiImageHasWhiteSprite(Image img)
        {
            if (img == null || img.sprite != null) return;
            if (_bakeWhiteSprite == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                _bakeWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _bakeWhiteSprite.name = "[UguiBake] BakeWhite";
            }
            img.sprite = _bakeWhiteSprite;
        }

        /// <summary>
        /// 烘焙用向下箭头 Sprite（Dropdown 箭头），程序化生成的三角形。
        /// </summary>
        static Sprite _bakeArrowSprite;

        public static Sprite GetBakeArrowSprite()
        {
            if (_bakeArrowSprite != null) return _bakeArrowSprite;

            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // 向下三角形：顶部宽、底部尖
            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1); // 0=底(尖), 1=顶(宽)
                int halfWidth = Mathf.RoundToInt(t * (size / 2f - 1));
                int cx = size / 2;
                for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                {
                    if (x >= 0 && x < size)
                        pixels[y * size + x] = Color.white;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _bakeArrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _bakeArrowSprite.name = "[UguiBake] BakeArrow";
            return _bakeArrowSprite;
        }

        /// <summary>烘焙用圆形 Sprite（Slider 手柄、Toggle 勾选标记等），程序化生成并缓存。</summary>
        static Sprite _bakeCircleSprite;

        public static Sprite GetBakeCircleSprite()
        {
            if (_bakeCircleSprite != null) return _bakeCircleSprite;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];

            float center = size / 2f;
            float radius = size / 2f - 1f;
            float radiusSq = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= radiusSq)
                    {
                        // 边缘抗锯齿：距离越近越透明
                        float dist = Mathf.Sqrt(distSq);
                        float alpha = dist > radius - 1f ? Mathf.Clamp01(radius - dist) : 1f;
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _bakeCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _bakeCircleSprite.name = "[UguiBake] BakeCircle";
            return _bakeCircleSprite;
        }

        /// <summary>烘焙用圆角矩形 Sprite（备用，当前圆角由 UguiRoundedCorners 组件实现）。</summary>
        static Sprite _bakeRoundedRectSprite;

        public static Sprite GetBakeRoundedRectSprite(float radius = 8f)
        {
            if (_bakeRoundedRectSprite != null) return _bakeRoundedRectSprite;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];

            float r = Mathf.Min(radius, size / 4f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = true;

                    // 四角检测
                    float cx = x, cy = y;
                    // 左上
                    if (x < r && y < r)
                    {
                        float dx = r - x, dy = r - y;
                        inside = dx * dx + dy * dy <= r * r;
                    }
                    // 右上
                    else if (x >= size - r && y < r)
                    {
                        float dx = x - (size - r - 1), dy = r - y;
                        inside = dx * dx + dy * dy <= r * r;
                    }
                    // 左下
                    else if (x < r && y >= size - r)
                    {
                        float dx = r - x, dy = y - (size - r - 1);
                        inside = dx * dx + dy * dy <= r * r;
                    }
                    // 右下
                    else if (x >= size - r && y >= size - r)
                    {
                        float dx = x - (size - r - 1), dy = y - (size - r - 1);
                        inside = dx * dx + dy * dy <= r * r;
                    }

                    pixels[y * size + x] = inside ? Color.white : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            _bakeRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _bakeRoundedRectSprite.name = "[UguiBake] BakeRoundedRect";
            return _bakeRoundedRectSprite;
        }

        /// <summary>清理所有缓存的 Sprite（编辑器重新编译或域重载时调用）。</summary>
        public static void ClearSpriteCache()
        {
            _bakeWhiteSprite = null;
            _bakeArrowSprite = null;
            _bakeCircleSprite = null;
            _bakeRoundedRectSprite = null;
        }

        public static Color ParseHexColor(string hex, Color defaultColor)
        {
            if (string.IsNullOrEmpty(hex)) return defaultColor;
            if (ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
            return defaultColor;
        }

        public static TextAlignmentOptions ParseTextAlign(string alignStr)
        {
            if (string.IsNullOrEmpty(alignStr)) return TextAlignmentOptions.Midline;
            switch (alignStr.ToLower())
            {
                case "left":
                case "start":
                    return TextAlignmentOptions.MidlineLeft;
                case "right":
                case "end":
                    return TextAlignmentOptions.MidlineRight;
                case "center":
                default:
                    return TextAlignmentOptions.Midline;
            }
        }

        public static GameObject CreateChildRect(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
            return go;
        }
    }
}