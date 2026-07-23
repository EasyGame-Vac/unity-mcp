using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Creates and manages uGUI (Canvas-based) UI template GameObjects.
    /// Supports preset layouts (four-corner HUD) and individual element creation.
    /// </summary>
    [McpForUnityTool("manage_ui_template")]
    public static class ManageUITemplate
    {
        // --- Zone anchor definitions ---
        // Each zone maps to anchorMin, anchorMax, pivot for RectTransform.
        private static readonly Dictionary<string, (Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)> ZoneAnchors =
            new()
            {
                { "top_left",     (new Vector2(0, 1),   new Vector2(0, 1),   new Vector2(0, 1)) },
                { "top_right",    (new Vector2(1, 1),   new Vector2(1, 1),   new Vector2(1, 1)) },
                { "bottom_left",  (new Vector2(0, 0),   new Vector2(0, 0),   new Vector2(0, 0)) },
                { "bottom_right", (new Vector2(1, 0),   new Vector2(1, 0),   new Vector2(1, 0)) },
                { "center",       (new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)) },
                { "top_full",     (new Vector2(0, 1),   new Vector2(1, 1),   new Vector2(0.5f, 1)) },
                { "bottom_full",  (new Vector2(0, 0),   new Vector2(1, 0),   new Vector2(0.5f, 0)) },
                { "full",         (new Vector2(0, 0),   new Vector2(1, 1),   new Vector2(0.5f, 0.5f)) },
            };

        // Default zone sizes (width, height) in pixels for 1920x1080 reference.
        // bottom_right is larger to reflect "visual weight = information importance".
        private static readonly Dictionary<string, Vector2> DefaultZoneSizes =
            new()
            {
                { "top_left",     new Vector2(280, 120) },
                { "top_right",    new Vector2(200, 80) },
                { "bottom_left",  new Vector2(360, 80) },
                { "bottom_right", new Vector2(200, 200) }, // Largest — lifespan seal
                { "center",       new Vector2(0, 0) },      // Stretches with parent
                { "top_full",     new Vector2(0, 60) },     // Full width, fixed height
                { "bottom_full",  new Vector2(0, 60) },
                { "full",         new Vector2(0, 0) },
            };

        // Safe margin as fraction of short edge (from UI layout spec: 2%).
        private const float SafeMarginFraction = 0.02f;
        private const float ShortEdgeReference = 1080f;
        private const float SafeMargin = SafeMarginFraction * ShortEdgeReference; // ~22px

        // --- Main Handler ---

        private static readonly List<string> ValidActions = new()
        {
            "create_canvas",
            "create_panel",
            "create_element",
            "create_from_preset",
            "list_presets",
            "get_info",
        };

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("Action parameter is required.");

            if (!ValidActions.Contains(action))
            {
                return new ErrorResponse(
                    $"Unknown action: '{action}'. Valid actions are: {string.Join(", ", ValidActions)}"
                );
            }

            // list_presets is handled server-side, but if it reaches here, return the list.
            if (action == "list_presets")
                return ListPresets();

            try
            {
                return action switch
                {
                    "create_canvas" => CreateCanvas(@params),
                    "create_panel" => CreatePanel(@params),
                    "create_element" => CreateElement(@params),
                    "create_from_preset" => CreateFromPreset(@params),
                    "get_info" => GetInfo(@params),
                    _ => new ErrorResponse($"Unknown action: '{action}'."),
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageUITemplate] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error in action '{action}': {e.Message}");
            }
        }

        // --- Action: create_canvas ---

        private static object CreateCanvas(JObject @params)
        {
            string name = @params["name"]?.ToString() ?? "Canvas";
            string canvasType = @params["canvasType"]?.ToString() ?? "screen_space_overlay";
            string refRes = @params["referenceResolution"]?.ToString() ?? "1920x1080";

            // Parse reference resolution
            if (!TryParseResolution(refRes, out int refWidth, out int refHeight))
                return new ErrorResponse($"Invalid referenceResolution '{refRes}'. Expected format: '1920x1080'.");

            // Create root GameObject
            var canvasGo = new GameObject(name);
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Configure render mode
            canvas.renderMode = canvasType switch
            {
                "screen_space_overlay" => RenderMode.ScreenSpaceOverlay,
                "screen_space_camera" => RenderMode.ScreenSpaceCamera,
                "world_space" => RenderMode.WorldSpace,
                _ => RenderMode.ScreenSpaceOverlay,
            };

            // Configure CanvasScaler
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refWidth, refHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Mark dirty so Unity saves it
            EditorUtility.SetDirty(canvasGo);

            var result = GetGameObjectData(canvasGo);
            result["canvasType"] = canvas.renderMode.ToString();
            result["referenceResolution"] = $"{refWidth}x{refHeight}";

            // Handle prefab save
            if (@params["saveAsPrefab"]?.ToObject<bool>() == true)
            {
                string prefabPath = @params["prefabPath"]?.ToString();
                var saveResult = TrySaveAsPrefab(canvasGo, prefabPath);
                if (saveResult != null)
                    return saveResult;
            }

            return new SuccessResponse(
                $"Canvas '{name}' created with {canvasType} mode, reference {refWidth}x{refHeight}.",
                result
            );
        }

        // --- Action: create_panel ---

        private static object CreatePanel(JObject @params)
        {
            string name = @params["name"]?.ToString() ?? "Panel";
            string parentPath = @params["parent"]?.ToString();
            string zone = @params["zone"]?.ToString()?.ToLowerInvariant();

            if (string.IsNullOrEmpty(zone) || !ZoneAnchors.ContainsKey(zone))
                return new ErrorResponse(
                    $"Invalid or missing zone. Valid zones: {string.Join(", ", ZoneAnchors.Keys)}"
                );

            // Find parent (Canvas or another panel)
            Transform parent = FindParent(parentPath);
            if (parent == null)
                return new ErrorResponse(
                    $"Parent not found: '{parentPath}'. Create a Canvas first via create_canvas."
                );

            // Create panel GameObject
            var panelGo = new GameObject(name, typeof(RectTransform));
            panelGo.transform.SetParent(parent, false);
            var rt = panelGo.GetComponent<RectTransform>();

            // Apply zone anchoring
            ApplyZoneAnchor(rt, zone);

            // Apply properties if provided
            var properties = @params["properties"] as JObject;
            if (properties != null && properties.HasValues)
            {
                ApplyRectTransformProperties(rt, properties);
                // Add Image component if color or sprite is specified
                TryAddImage(panelGo, properties);
            }

            EditorUtility.SetDirty(panelGo);

            return new SuccessResponse(
                $"Panel '{name}' created at zone '{zone}' under '{parentPath}'.",
                GetGameObjectData(panelGo)
            );
        }

        // --- Action: create_element ---

        private static object CreateElement(JObject @params)
        {
            string name = @params["name"]?.ToString() ?? "Element";
            string parentPath = @params["parent"]?.ToString();
            string elementType = @params["elementType"]?.ToString()?.ToLowerInvariant() ?? "empty";
            var properties = @params["properties"] as JObject;

            if (string.IsNullOrEmpty(parentPath))
                return new ErrorResponse("'parent' is required for create_element.");

            Transform parent = FindParent(parentPath);
            if (parent == null)
                return new ErrorResponse($"Parent not found: '{parentPath}'.");

            var elementGo = new GameObject(name, typeof(RectTransform));
            elementGo.transform.SetParent(parent, false);
            var rt = elementGo.GetComponent<RectTransform>();

            // Default to stretch-to-parent
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Apply properties first (may override anchors)
            if (properties != null && properties.HasValues)
            {
                ApplyRectTransformProperties(rt, properties);
            }

            // Add component based on element type
            switch (elementType)
            {
                case "image":
                    var img = elementGo.AddComponent<Image>();
                    if (properties != null)
                        ApplyImageProperties(img, properties);
                    break;

                case "text":
                    var txt = elementGo.AddComponent<Text>();
                    if (properties != null)
                        ApplyTextProperties(txt, properties);
                    else
                    {
                        txt.text = name;
                        txt.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                    break;

                case "button":
                    var btnImg = elementGo.AddComponent<Image>();
                    var button = elementGo.AddComponent<Button>();
                    if (properties != null)
                    {
                        ApplyImageProperties(btnImg, properties);
                    }
                    // Add a child Text by default
                    var childTextGo = new GameObject("Text", typeof(RectTransform));
                    childTextGo.transform.SetParent(elementGo.transform, false);
                    var childRt = childTextGo.GetComponent<RectTransform>();
                    childRt.anchorMin = Vector2.zero;
                    childRt.anchorMax = Vector2.one;
                    childRt.offsetMin = Vector2.zero;
                    childRt.offsetMax = Vector2.zero;
                    var childText = childTextGo.AddComponent<Text>();
                    childText.text = name;
                    childText.alignment = TextAnchor.MiddleCenter;
                    childText.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    break;

                case "empty":
                    // Just a RectTransform container, no visual component
                    break;

                default:
                    return new ErrorResponse(
                        $"Unknown elementType: '{elementType}'. Valid: image, text, button, empty."
                    );
            }

            EditorUtility.SetDirty(elementGo);

            return new SuccessResponse(
                $"Element '{name}' ({elementType}) created under '{parentPath}'.",
                GetGameObjectData(elementGo)
            );
        }

        // --- Action: create_from_preset ---

        private static object CreateFromPreset(JObject @params)
        {
            string presetName = @params["preset"]?.ToString()?.ToLowerInvariant();
            string canvasName = @params["name"]?.ToString() ?? "Canvas";
            string refRes = @params["referenceResolution"]?.ToString() ?? "1920x1080";

            if (string.IsNullOrEmpty(presetName))
                return new ErrorResponse("'preset' is required for create_from_preset.");

            switch (presetName)
            {
                case "four_corner_hud":
                    return CreateFourCornerHud(canvasName, refRes, @params);
                case "dialog":
                    return CreateDialogPreset(canvasName, refRes, @params);
                case "status_bar":
                    return CreateStatusBarPreset(canvasName, refRes, @params);
                default:
                    return new ErrorResponse(
                        $"Unknown preset: '{presetName}'. Valid: four_corner_hud, dialog, status_bar."
                    );
            }
        }

        /// <summary>
        /// Creates a four-corner HUD layout based on the UI layout spec:
        /// visual weight = information importance.
        /// </summary>
        private static object CreateFourCornerHud(string canvasName, string refRes, JObject @params)
        {
            if (!TryParseResolution(refRes, out int refWidth, out int refHeight))
                return new ErrorResponse($"Invalid referenceResolution '{refRes}'.");

            // 1. Create Canvas
            var canvasGo = new GameObject(canvasName);
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refWidth, refHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            float margin = SafeMargin;

            // 2. Create zones
            var zones = new[]
            {
                ("Zone_TopLeft", "top_left", new Vector2(280, 120)),
                ("Zone_TopRight", "top_right", new Vector2(200, 80)),
                ("Zone_BottomLeft", "bottom_left", new Vector2(360, 80)),
                ("Zone_BottomRight", "bottom_right", new Vector2(200, 200)), // Largest
                ("Zone_Center", "center", Vector2.zero),
            };

            var createdZones = new List<object>();

            foreach (var (zoneName, zoneKey, defaultSize) in zones)
            {
                var zoneGo = new GameObject(zoneName, typeof(RectTransform));
                zoneGo.transform.SetParent(canvasGo.transform, false);
                var rt = zoneGo.GetComponent<RectTransform>();
                ApplyZoneAnchor(rt, zoneKey);

                // Apply safe margin offset
                ApplySafeMargin(rt, zoneKey, margin);

                // Set default size (center zone stretches full)
                if (zoneKey != "center")
                {
                    rt.sizeDelta = defaultSize;
                }

                createdZones.Add(new
                {
                    name = zoneName,
                    zone = zoneKey,
                    sizeDelta = new { x = rt.sizeDelta.x, y = rt.sizeDelta.y },
                    anchorMin = new { x = rt.anchorMin.x, y = rt.anchorMin.y },
                    anchorMax = new { x = rt.anchorMax.x, y = rt.anchorMax.y },
                });
            }

            // 3. Handle prefab save
            bool saveAsPrefab = @params["saveAsPrefab"]?.ToObject<bool>() ?? false;
            string prefabPath = @params["prefabPath"]?.ToString();

            if (saveAsPrefab && !string.IsNullOrEmpty(prefabPath))
            {
                var saveResult = TrySaveAsPrefab(canvasGo, prefabPath);
                if (saveResult != null)
                    return saveResult;
            }

            EditorUtility.SetDirty(canvasGo);

            return new SuccessResponse(
                $"Four-corner HUD '{canvasName}' created with 5 zones "
                + $"(reference {refWidth}x{refHeight}, margin {margin:F0}px).",
                new
                {
                    canvas = GetGameObjectData(canvasGo),
                    zones = createdZones,
                    layoutSpec = new
                    {
                        visualWeight = "bottom_right > top_left > top_right > bottom_left",
                        safeMarginPx = margin,
                        centerArea = "reserved for gameplay, no UI panels",
                    },
                }
            );
        }

        private static object CreateDialogPreset(string canvasName, string refRes, JObject @params)
        {
            if (!TryParseResolution(refRes, out int refWidth, out int refHeight))
                return new ErrorResponse($"Invalid referenceResolution '{refRes}'.");

            // Canvas with semi-transparent backdrop
            var canvasGo = new GameObject(canvasName);
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refWidth, refHeight);
            scaler.matchWidthOrHeight = 0.5f;

            // Backdrop (full screen, semi-transparent)
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(canvasGo.transform, false);
            var backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = backdropGo.AddComponent<Image>();
            backdropImg.color = new Color(0, 0, 0, 0.6f);

            // Dialog panel (centered, 60% width, 40% height)
            var dialogGo = new GameObject("DialogPanel", typeof(RectTransform));
            dialogGo.transform.SetParent(canvasGo.transform, false);
            var dialogRt = dialogGo.GetComponent<RectTransform>();
            dialogRt.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRt.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRt.pivot = new Vector2(0.5f, 0.5f);
            dialogRt.sizeDelta = new Vector2(refWidth * 0.6f, refHeight * 0.4f);
            var dialogImg = dialogGo.AddComponent<Image>();
            dialogImg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            EditorUtility.SetDirty(canvasGo);

            return new SuccessResponse(
                $"Dialog preset '{canvasName}' created (backdrop + centered panel).",
                new { canvas = GetGameObjectData(canvasGo) }
            );
        }

        private static object CreateStatusBarPreset(string canvasName, string refRes, JObject @params)
        {
            if (!TryParseResolution(refRes, out int refWidth, out int refHeight))
                return new ErrorResponse($"Invalid referenceResolution '{refRes}'.");

            var canvasGo = new GameObject(canvasName);
            var canvas = canvasGo.AddComponent<Canvas>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refWidth, refHeight);
            scaler.matchWidthOrHeight = 0.5f;

            // Top bar
            var topBarGo = new GameObject("TopBar", typeof(RectTransform));
            topBarGo.transform.SetParent(canvasGo.transform, false);
            var topRt = topBarGo.GetComponent<RectTransform>();
            ApplyZoneAnchor(topRt, "top_full");
            topRt.sizeDelta = new Vector2(0, 60);
            var topImg = topBarGo.AddComponent<Image>();
            topImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            // Bottom bar
            var bottomBarGo = new GameObject("BottomBar", typeof(RectTransform));
            bottomBarGo.transform.SetParent(canvasGo.transform, false);
            var bottomRt = bottomBarGo.GetComponent<RectTransform>();
            ApplyZoneAnchor(bottomRt, "bottom_full");
            bottomRt.sizeDelta = new Vector2(0, 60);
            var bottomImg = bottomBarGo.AddComponent<Image>();
            bottomImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            EditorUtility.SetDirty(canvasGo);

            return new SuccessResponse(
                $"Status bar preset '{canvasName}' created (top + bottom bars).",
                new { canvas = GetGameObjectData(canvasGo) }
            );
        }

        // --- Action: get_info ---

        private static object GetInfo(JObject @params)
        {
            string path = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'name' (GameObject path) is required for get_info.");

            Transform target = FindParent(path);
            if (target == null)
                return new ErrorResponse($"GameObject not found: '{path}'.");

            return new SuccessResponse(
                $"UI hierarchy info for '{path}'.",
                GetHierarchyData(target, depth: 0, maxDepth: 5)
            );
        }

        // --- Action: list_presets (fallback) ---

        private static object ListPresets()
        {
            return new SuccessResponse(
                "Available UI templates.",
                new
                {
                    presets = new[]
                    {
                        new { name = "four_corner_hud", zones = new[] { "top_left", "top_right", "bottom_left", "bottom_right", "center" } },
                        new { name = "dialog", zones = new[] { "full", "center" } },
                        new { name = "status_bar", zones = new[] { "top_full", "bottom_full" } },
                    }
                }
            );
        }

        // --- Helpers ---

        private static bool TryParseResolution(string input, out int width, out int height)
        {
            width = 1920;
            height = 1080;
            if (string.IsNullOrEmpty(input)) return false;
            var parts = input.ToLowerInvariant().Split('x');
            if (parts.Length != 2) return false;
            return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
        }

        private static Transform FindParent(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var go = GameObject.Find(path);
            return go?.transform;
        }

        private static void ApplyZoneAnchor(RectTransform rt, string zone)
        {
            if (!ZoneAnchors.TryGetValue(zone, out var anchor))
                anchor = ZoneAnchors["center"];

            rt.anchorMin = anchor.anchorMin;
            rt.anchorMax = anchor.anchorMax;
            rt.pivot = anchor.pivot;

            // Set default size
            if (DefaultZoneSizes.TryGetValue(zone, out var size))
                rt.sizeDelta = size;
        }

        /// <summary>
        /// Applies safe margin offset based on the UI layout spec:
        /// each corner zone is offset from the screen edge by the margin.
        /// </summary>
        private static void ApplySafeMargin(RectTransform rt, string zone, float margin)
        {
            switch (zone)
            {
                case "top_left":
                    rt.anchoredPosition = new Vector2(margin, -margin);
                    break;
                case "top_right":
                    rt.anchoredPosition = new Vector2(-margin, -margin);
                    break;
                case "bottom_left":
                    rt.anchoredPosition = new Vector2(margin, margin);
                    break;
                case "bottom_right":
                    // Slightly more inset for visual weight (3% per spec)
                    rt.anchoredPosition = new Vector2(-margin * 1.5f, margin * 1.5f);
                    break;
                case "top_full":
                    rt.anchoredPosition = new Vector2(0, -margin);
                    break;
                case "bottom_full":
                    rt.anchoredPosition = new Vector2(0, margin);
                    break;
                // center and full: no offset needed
            }
        }

        private static void ApplyRectTransformProperties(RectTransform rt, JObject props)
        {
            // Size
            if (props["width"] != null || props["height"] != null)
            {
                float w = props["width"]?.ToObject<float>() ?? rt.sizeDelta.x;
                float h = props["height"]?.ToObject<float>() ?? rt.sizeDelta.y;
                rt.sizeDelta = new Vector2(w, h);
            }

            // Anchors
            if (props["anchorMin"] is JObject am)
                rt.anchorMin = ParseVector2(am);
            if (props["anchorMax"] is JObject aM)
                rt.anchorMax = ParseVector2(aM);

            // Pivot
            if (props["pivot"] is JObject pv)
                rt.pivot = ParseVector2(pv);

            // Offsets
            if (props["offsetMin"] is JObject om)
                rt.offsetMin = ParseVector2(om);
            if (props["offsetMax"] is JObject oM)
                rt.offsetMax = ParseVector2(oM);

            // Position (anchoredPosition)
            if (props["position"] is JObject pos)
                rt.anchoredPosition = ParseVector2(pos);

            // Rotation
            if (props["rotation"] is JObject rot)
            {
                var r = ParseVector3(rot);
                rt.localEulerAngles = r;
            }

            // Scale
            if (props["scale"] is JObject scl)
            {
                var s = ParseVector3(scl);
                rt.localScale = s;
            }
        }

        private static void TryAddImage(GameObject go, JObject props)
        {
            bool hasColor = props["color"] != null || props["backgroundColor"] != null;
            bool hasSprite = props["spritePath"] != null;
            if (!hasColor && !hasSprite) return;

            var img = go.AddComponent<Image>();

            if (hasColor)
            {
                string hex = props["color"]?.ToString() ?? props["backgroundColor"]?.ToString();
                if (TryParseColor(hex, out Color c))
                    img.color = c;
            }

            if (hasSprite)
            {
                string spritePath = props["spritePath"]!.ToString();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null)
                    img.sprite = sprite;
                else
                    McpLog.Warn($"[ManageUITemplate] Sprite not found at path: {spritePath}");
            }

            if (props["raycastTarget"]?.Type == JTokenType.Boolean)
                img.raycastTarget = props["raycastTarget"].ToObject<bool>();
        }

        private static void ApplyImageProperties(Image img, JObject props)
        {
            if (props["color"]?.ToString() is string hex && TryParseColor(hex, out Color c))
                img.color = c;

            if (props["spritePath"]?.ToString() is string spritePath)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null) img.sprite = sprite;
            }

            if (props["raycastTarget"]?.Type == JTokenType.Boolean)
                img.raycastTarget = props["raycastTarget"].ToObject<bool>();

            // Image type
            if (props["imageType"]?.ToString()?.ToLowerInvariant() is string it)
            {
                img.type = it switch
                {
                    "simple" => Image.Type.Simple,
                    "sliced" => Image.Type.Sliced,
                    "tiled" => Image.Type.Tiled,
                    "filled" => Image.Type.Filled,
                    _ => Image.Type.Simple,
                };
            }
        }

        private static void ApplyTextProperties(Text txt, JObject props)
        {
            if (props["text"]?.ToString() is string t)
                txt.text = t;

            if (props["fontSize"]?.Type == JTokenType.Integer)
                txt.fontSize = props["fontSize"].ToObject<int>();

            if (props["color"]?.ToString() is string hex && TryParseColor(hex, out Color c))
                txt.color = c;

            if (props["alignment"]?.ToString() is string align)
            {
                txt.alignment = align.ToLowerInvariant() switch
                {
                    "upperleft" => TextAnchor.UpperLeft,
                    "uppercenter" => TextAnchor.UpperCenter,
                    "upperright" => TextAnchor.UpperRight,
                    "middleleft" => TextAnchor.MiddleLeft,
                    "middlecenter" => TextAnchor.MiddleCenter,
                    "middleright" => TextAnchor.MiddleRight,
                    "lowerleft" => TextAnchor.LowerLeft,
                    "lowercenter" => TextAnchor.LowerCenter,
                    "lowerright" => TextAnchor.LowerRight,
                    _ => TextAnchor.MiddleCenter,
                };
            }

            if (props["fontPath"]?.ToString() is string fontPath)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font != null) txt.font = font;
            }

            // Default font if none set
            if (txt.font == null)
                txt.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static object TrySaveAsPrefab(GameObject go, string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return new ErrorResponse("'prefabPath' is required when saveAsPrefab is true.");

            string fullPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            string dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                McpLog.Warn($"[ManageUITemplate] Directory '{dir}' does not exist. Attempting to create.");
                // AssetDatabase.CreateFolder can only create one level at a time.
                // Fall back to Directory.CreateDirectory for nested paths.
                string absDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), dir);
                System.IO.Directory.CreateDirectory(absDir);
                AssetDatabase.Refresh();
            }

            try
            {
                bool success = PrefabUtility.SaveAsPrefabAsset(go, fullPath, out _);
                if (success)
                    return null; // null means "no error, continue"
                return new ErrorResponse($"Failed to save prefab at '{fullPath}'.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving prefab: {e.Message}");
            }
        }

        private static Dictionary<string, object> GetGameObjectData(GameObject go)
        {
            var data = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["instanceID"] = go.GetInstanceID(),
                ["active"] = go.activeSelf,
            };

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                data["rectTransform"] = new
                {
                    anchorMin = new { x = rt.anchorMin.x, y = rt.anchorMin.y },
                    anchorMax = new { x = rt.anchorMax.x, y = rt.anchorMax.y },
                    pivot = new { x = rt.pivot.x, y = rt.pivot.y },
                    sizeDelta = new { x = rt.sizeDelta.x, y = rt.sizeDelta.y },
                    anchoredPosition = new { x = rt.anchoredPosition.x, y = rt.anchoredPosition.y },
                };
            }

            var components = go.GetComponents<Component>()
                .Select(c => c.GetType().Name)
                .ToList();
            data["components"] = components;

            data["childCount"] = go.transform.childCount;
            return data;
        }

        private static object GetHierarchyData(Transform transform, int depth, int maxDepth)
        {
            var go = transform.gameObject;
            var data = GetGameObjectData(go);

            if (depth < maxDepth && transform.childCount > 0)
            {
                var children = new List<object>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    children.Add(GetHierarchyData(transform.GetChild(i), depth + 1, maxDepth));
                }
                data["children"] = children;
            }

            return data;
        }

        // --- JSON parsing helpers ---

        private static Vector2 ParseVector2(JObject obj)
        {
            float x = obj["x"]?.ToObject<float>() ?? 0;
            float y = obj["y"]?.ToObject<float>() ?? 0;
            return new Vector2(x, y);
        }

        private static Vector3 ParseVector3(JObject obj)
        {
            float x = obj["x"]?.ToObject<float>() ?? 0;
            float y = obj["y"]?.ToObject<float>() ?? 0;
            float z = obj["z"]?.ToObject<float>() ?? 0;
            return new Vector3(x, y, z);
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            return ColorUtility.TryParseHtmlString(hex, out color);
        }
    }
}
