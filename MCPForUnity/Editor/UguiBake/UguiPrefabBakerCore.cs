using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using Newtonsoft.Json;
using System.Collections.Generic;
using MCPForUnity.Runtime.UguiBake;

namespace MCPForUnity.Editor.UguiBake
{
    public static class UguiPrefabBakerCore
    {
        static void ApplyRoundedCornersIfAny(Image img, UIDataNode nodeData)
        {
            if (nodeData.borderRadius == null || nodeData.borderRadius.Count == 0) return;
            var br = nodeData.borderRadius;
            float tl, tr, radBr, bl;
            if (br.Count == 1)
                tl = tr = radBr = bl = br[0];
            else if (br.Count == 2)
            {
                tl = radBr = br[0];
                tr = bl = br[1];
            }
            else if (br.Count == 3)
            {
                tl = br[0];
                tr = bl = br[1];
                radBr = br[2];
            }
            else
            {
                tl = br[0];
                tr = br[1];
                radBr = br[2];
                bl = br.Count >= 4 ? br[3] : radBr;
            }
            if (tl < 0.01f && tr < 0.01f && radBr < 0.01f && bl < 0.01f) return;

            var fx = img.gameObject.GetComponent<UguiRoundedCorners>();
            if (fx == null) fx = img.gameObject.AddComponent<UguiRoundedCorners>();
            fx.ApplyBakedRadii(tl, tr, radBr, bl);
        }

        /// <summary>当前烘焙批次内用于解析 JSON <c>image</c> 相对路径的源 HTML（Assets 相对路径）。</summary>
        static string _imageResolveSourceHtml;

        public static void BeginImageResolveSession(string sourceHtmlAssetPath)
        {
            _imageResolveSourceHtml = string.IsNullOrWhiteSpace(sourceHtmlAssetPath)
                ? null
                : sourceHtmlAssetPath.Trim().Replace('\\', '/');
        }

        public static void EndImageResolveSession()
        {
            _imageResolveSourceHtml = null;
        }

        /// <summary>单色 <see cref="Image.color"/> 或 JSON 中的 <c>linearGradient</c>（顶点渐变）。</summary>
        static void ConfigureBakedBackgroundImage(Image img, UIDataNode nodeData, bool isBackdropBg)
        {
            UguiPrefabBakerUtils.EnsureUiImageHasWhiteSprite(img);
            ApplyRoundedCornersIfAny(img, nodeData);
            Color bgColor = UguiPrefabBakerUtils.ParseHexColor(nodeData.color, Color.white);
            bool hasGrad = nodeData.linearGradient != null
                && nodeData.linearGradient.colors != null
                && nodeData.linearGradient.colors.Count >= 2;
            
            // 处理背景图片
            if (!string.IsNullOrEmpty(nodeData.image))
            {
                string imagePath = UguiPrefabBakerUtils.ResolveImageAssetPath(nodeData.image, _imageResolveSourceHtml);
                Sprite sprite = UguiPrefabBakerUtils.TryLoadSpriteAtAssetPath(imagePath);
                if (sprite != null)
                {
                    img.sprite = sprite;
                    img.type = Image.Type.Simple;
                    img.preserveAspect = false;
                    img.color = Color.white; // 使用图片原始颜色
                }
                else
                {
                    Debug.LogWarning(
                        $"[UguiBake] 无法加载图片资源：{imagePath}（JSON image=\"{nodeData.image}\", sourceHtml=\"{_imageResolveSourceHtml ?? ""}\"）");
                    // 如果图片加载失败，回退到颜色或渐变
                    if (hasGrad)
                    {
                        img.color = Color.white;
                        var colors = new List<Color>();
                        foreach (var hex in nodeData.linearGradient.colors)
                            colors.Add(UguiPrefabBakerUtils.ParseHexColor(hex, Color.white));

                        IReadOnlyList<float> pos = null;
                        if (nodeData.linearGradient.positions != null
                            && nodeData.linearGradient.positions.Count == colors.Count)
                            pos = nodeData.linearGradient.positions;

                        var fx = img.gameObject.GetComponent<UguiLinearGradient>();
                        if (fx == null) fx = img.gameObject.AddComponent<UguiLinearGradient>();
                        fx.SetGradient(nodeData.linearGradient.angle, colors, pos);
                    }
                    else
                        img.color = bgColor;
                }
            }
            else if (hasGrad)
            {
                img.color = Color.white;
                var colors = new List<Color>();
                foreach (var hex in nodeData.linearGradient.colors)
                    colors.Add(UguiPrefabBakerUtils.ParseHexColor(hex, Color.white));

                IReadOnlyList<float> pos = null;
                if (nodeData.linearGradient.positions != null
                    && nodeData.linearGradient.positions.Count == colors.Count)
                    pos = nodeData.linearGradient.positions;

                var fx = img.gameObject.GetComponent<UguiLinearGradient>();
                if (fx == null) fx = img.gameObject.AddComponent<UguiLinearGradient>();
                fx.SetGradient(nodeData.linearGradient.angle, colors, pos);
            }
            else
                img.color = bgColor;

            // 描边组件须在渐变之后添加；IMeshModifier 顺序：RoundedCorners → LinearGradient → Outline
            // DSL outline 优先；否则使用 JSON 中的 CSS border。
            float strokeW = 0f;
            string strokeHex = null;
            if (nodeData.outline != null && nodeData.outline.width > 0.001f)
            {
                strokeW = nodeData.outline.width;
                strokeHex = nodeData.outline.color;
            }
            else if (nodeData.border != null && nodeData.border.width > 0.001f)
            {
                strokeW = nodeData.border.width;
                strokeHex = nodeData.border.color;
            }
            if (strokeW > 0.001f && !string.IsNullOrEmpty(strokeHex))
            {
                Color strokeCol = UguiPrefabBakerUtils.ParseHexColor(strokeHex, Color.black);
                if (strokeCol.a > 0.001f)
                {
                    var stroke = img.gameObject.GetComponent<UguiImageOutline>();
                    if (stroke == null) stroke = img.gameObject.AddComponent<UguiImageOutline>();
                    stroke.ApplyBakedStroke(strokeW, strokeCol);
                }
            }

            if (isBackdropBg)
                img.raycastTarget = hasGrad || bgColor.a > 0.01f;
        }

        static bool NodeHasLinearGradient(UIDataNode nodeData)
        {
            return nodeData.linearGradient != null
                && nodeData.linearGradient.colors != null
                && nodeData.linearGradient.colors.Count >= 2;
        }

        public static GameObject CreateUINode(UIDataNode nodeData, Transform parent, float parentAbsX, float parentAbsY, float parentAbsW, float parentAbsH, bool useTMPText = true)
        {
            GameObject go = new GameObject(nodeData.name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            ApplyLayoutToRectTransform(rect, nodeData, parentAbsX, parentAbsY, parentAbsW, parentAbsH);

            Transform childrenContainer = ApplyComponentByType(go, nodeData, useTMPText);

            if (nodeData.children != null && nodeData.children.Count > 0)
            {
                float childRefPx = nodeData.x;
                float childRefPy = nodeData.y;
                float childRefPw = nodeData.width;
                float childRefPh = nodeData.height;
                if (childRefPw <= 0.001f || childRefPh <= 0.001f)
                {
                    childRefPx = parentAbsX;
                    childRefPy = parentAbsY;
                    childRefPw = parentAbsW;
                    childRefPh = parentAbsH;
                }

                foreach (var childNode in nodeData.children)
                    CreateUINode(childNode, childrenContainer, childRefPx, childRefPy, childRefPw, childRefPh, useTMPText);
            }

            return go;
        }

        public static void ApplyLayoutToRectTransform(RectTransform rect, UIDataNode nodeData, float px, float py, float pw, float ph)
        {
            string mode = nodeData.layout == null ? string.Empty : nodeData.layout.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(mode))
                mode = "absolute";

            float x = nodeData.x;
            float y = nodeData.y;
            float w = nodeData.width;
            float h = nodeData.height;

            // HTML 合成的 layoutGroup（无 data-u-name 的包裹层）常为 0×0，需铺满父级并在子节点坐标换算时回退到上层设计框。
            if ((w <= 0.001f || h <= 0.001f) && nodeData.children != null && nodeData.children.Count > 0)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return;
            }

            switch (mode)
            {
                case "stretch":
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    {
                        float left = x - px;
                        float top = y - py;
                        float right = (px + pw) - (x + w);
                        float bottom = (py + ph) - (y + h);
                        rect.offsetMin = new Vector2(left, bottom);
                        rect.offsetMax = new Vector2(-right, -top);
                    }
                    break;
                case "center":
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(w, h);
                    {
                        // 设计整像素 + 奇数宽高时，几何中心在 *.5；与父中心差会出现 (0.5,-0.5) 一类 anchoredPosition。
                        // 用半宽/半高下取整对齐像素格，使常见主菜单等场景为 (0,0)。
                        float cx = x + Mathf.Floor(w * 0.5f + 1e-4f);
                        float cy = y + Mathf.Floor(h * 0.5f + 1e-4f);
                        float pcx = px + Mathf.Floor(pw * 0.5f + 1e-4f);
                        float pcy = py + Mathf.Floor(ph * 0.5f + 1e-4f);
                        rect.anchoredPosition = new Vector2(cx - pcx, -(cy - pcy));
                    }
                    break;
                case "stretch-h":
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    {
                        float left = x - px;
                        float right = (px + pw) - (x + w);
                        rect.sizeDelta = new Vector2(-(left + right), h);
                        rect.anchoredPosition = new Vector2((right - left) * 0.5f, -(y - py));
                    }
                    break;
                case "stretch-v":
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    {
                        float top = y - py;
                        float bottom = (py + ph) - (y + h);
                        rect.sizeDelta = new Vector2(w, -(top + bottom));
                        rect.anchoredPosition = new Vector2(x - px, (bottom - top) * 0.5f);
                    }
                    break;
                default:
                    rect.anchorMin = new Vector2(0, 1);
                    rect.anchorMax = new Vector2(0, 1);
                    rect.pivot = new Vector2(0, 1);
                    rect.anchoredPosition = new Vector2(x - px, -(y - py));
                    rect.sizeDelta = new Vector2(w, h);
                    break;
            }
        }

        public static Transform ApplyComponentByType(GameObject go, UIDataNode nodeData, bool useTMPText = true)
        {
            Color bgColor = UguiPrefabBakerUtils.ParseHexColor(nodeData.color, Color.white);
            Color fontColor = UguiPrefabBakerUtils.ParseHexColor(nodeData.fontColor, Color.black);
            int fontSize = nodeData.fontSize > 0 ? nodeData.fontSize : 24;
            TextAlignmentOptions alignment = UguiPrefabBakerUtils.ParseTextAlign(nodeData.textAlign);
            TextAnchor unityAlignment = TextAnchor.MiddleCenter;
            switch (alignment)
            {
                case TextAlignmentOptions.MidlineLeft:
                    unityAlignment = TextAnchor.MiddleLeft;
                    break;
                case TextAlignmentOptions.MidlineRight:
                    unityAlignment = TextAnchor.MiddleRight;
                    break;
                case TextAlignmentOptions.Midline:
                default:
                    unityAlignment = TextAnchor.MiddleCenter;
                    break;
            }
            bool isMultiLine = nodeData.height > (fontSize * 1.5f);

            switch (nodeData.type.ToLower())
            {
                case "div":
                case "image":
                    Image img = go.AddComponent<Image>();
                    bool isBackdropBg = string.Equals(nodeData.name, "bg", System.StringComparison.Ordinal);
                    ConfigureBakedBackgroundImage(img, nodeData, isBackdropBg);
                    if (!isBackdropBg)
                        img.raycastTarget = false;
                    return go.transform;

                case "text":
                    if (useTMPText)
                    {
                        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
                        txt.text = nodeData.text;
                        txt.color = fontColor;
                        txt.fontSize = fontSize;
                        txt.alignment = alignment;
                        txt.enableWordWrapping = isMultiLine;
                        txt.overflowMode = isMultiLine ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
                        txt.raycastTarget = false;
                    }
                    else
                    {
                        Text txt = go.AddComponent<Text>();
                        txt.text = nodeData.text;
                        txt.color = fontColor;
                        txt.fontSize = fontSize;
                        txt.alignment = unityAlignment;
                        txt.supportRichText = true;
                        txt.raycastTarget = false;
                    }
                    return go.transform;

                case "button":
                    Image btnImg = go.AddComponent<Image>();
                    ConfigureBakedBackgroundImage(btnImg, nodeData, false);
                    Button btn = go.AddComponent<Button>();
                    btn.targetGraphic = btnImg;

                    GameObject btnTxtGo = UguiPrefabBakerUtils.CreateChildRect(go, useTMPText ? "Text (TMP)" : "Text", Vector2.zero, Vector2.one);
                    if (useTMPText)
                    {
                        TextMeshProUGUI btnTxt = btnTxtGo.AddComponent<TextMeshProUGUI>();
                        btnTxt.text = nodeData.text;
                        btnTxt.color = fontColor;
                        btnTxt.fontSize = fontSize;
                        btnTxt.alignment = alignment;
                        btnTxt.enableWordWrapping = false;
                        btnTxt.overflowMode = TextOverflowModes.Overflow;
                        btnTxt.raycastTarget = false;
                    }
                    else
                    {
                        Text btnTxt = btnTxtGo.AddComponent<Text>();
                        btnTxt.text = nodeData.text;
                        btnTxt.color = fontColor;
                        btnTxt.fontSize = fontSize;
                        btnTxt.alignment = unityAlignment;
                        btnTxt.supportRichText = true;
                        btnTxt.raycastTarget = false;
                    }
                    return go.transform;

                case "input":
                    Image inputBg = go.AddComponent<Image>();
                    ConfigureBakedBackgroundImage(inputBg, nodeData, false);
                    
                    if (useTMPText)
                    {
                        TMP_InputField inputField = go.AddComponent<TMP_InputField>();
                        inputField.targetGraphic = inputBg;

                        GameObject textAreaGo = UguiPrefabBakerUtils.CreateChildRect(go, "Text Area", Vector2.zero, Vector2.one, new Vector2(10, 5), new Vector2(-10, -5));
                        textAreaGo.AddComponent<RectMask2D>();

                        GameObject phGo = UguiPrefabBakerUtils.CreateChildRect(textAreaGo, "Placeholder", Vector2.zero, Vector2.one);
                        TextMeshProUGUI phTxt = phGo.AddComponent<TextMeshProUGUI>();
                        phTxt.text = nodeData.text;
                        Color phColor = fontColor;
                        phColor.a = 0.5f;
                        phTxt.color = phColor;
                        phTxt.fontSize = fontSize;
                        phTxt.alignment = alignment;
                        phTxt.enableWordWrapping = false;
                        phTxt.raycastTarget = false;

                        GameObject textGo = UguiPrefabBakerUtils.CreateChildRect(textAreaGo, "Text", Vector2.zero, Vector2.one);
                        TextMeshProUGUI inTxt = textGo.AddComponent<TextMeshProUGUI>();
                        inTxt.color = fontColor;
                        inTxt.fontSize = fontSize;
                        inTxt.alignment = alignment;
                        inTxt.enableWordWrapping = false;
                        inTxt.raycastTarget = false;

                        inputField.textViewport = textAreaGo.GetComponent<RectTransform>();
                        inputField.textComponent = inTxt;
                        inputField.placeholder = phTxt;
                    }
                    else
                    {
                        InputField inputField = go.AddComponent<InputField>();
                        inputField.targetGraphic = inputBg;

                        GameObject textAreaGo = UguiPrefabBakerUtils.CreateChildRect(go, "Text Area", Vector2.zero, Vector2.one, new Vector2(10, 5), new Vector2(-10, -5));
                        textAreaGo.AddComponent<RectMask2D>();

                        GameObject phGo = UguiPrefabBakerUtils.CreateChildRect(textAreaGo, "Placeholder", Vector2.zero, Vector2.one);
                        Text phTxt = phGo.AddComponent<Text>();
                        phTxt.text = nodeData.text;
                        Color phColor = fontColor;
                        phColor.a = 0.5f;
                        phTxt.color = phColor;
                        phTxt.fontSize = fontSize;
                        phTxt.alignment = unityAlignment;
                        phTxt.supportRichText = true;
                        phTxt.raycastTarget = false;

                        GameObject textGo = UguiPrefabBakerUtils.CreateChildRect(textAreaGo, "Text", Vector2.zero, Vector2.one);
                        Text inTxt = textGo.AddComponent<Text>();
                        inTxt.color = fontColor;
                        inTxt.fontSize = fontSize;
                        inTxt.alignment = unityAlignment;
                        inTxt.supportRichText = true;
                        inTxt.raycastTarget = false;

                        inputField.textComponent = inTxt;
                        inputField.placeholder = phTxt;
                    }
                    return go.transform;

                case "scroll":
                    Image scrollBg = go.AddComponent<Image>();
                    ConfigureBakedBackgroundImage(scrollBg, nodeData, false);
                    scrollBg.raycastTarget = NodeHasLinearGradient(nodeData) || scrollBg.color.a > 0.01f;

                    ScrollRect scrollRect = go.AddComponent<ScrollRect>();
                    bool isVertical = string.IsNullOrEmpty(nodeData.dir) || nodeData.dir.ToLower() == "v";
                    scrollRect.horizontal = !isVertical;
                    scrollRect.vertical = isVertical;

                    GameObject viewportGo = UguiPrefabBakerUtils.CreateChildRect(go, "Viewport", Vector2.zero, Vector2.one);
                    viewportGo.AddComponent<RectMask2D>();

                    GameObject contentGo = UguiPrefabBakerUtils.CreateChildRect(viewportGo, "Content", new Vector2(0, 1), new Vector2(0, 1));
                    RectTransform contentRect = contentGo.GetComponent<RectTransform>();
                    contentRect.pivot = new Vector2(0, 1);
                    contentRect.sizeDelta = new Vector2(nodeData.width, nodeData.height);

                    scrollRect.viewport = viewportGo.GetComponent<RectTransform>();
                    scrollRect.content = contentRect;
                    return contentGo.transform;

                case "toggle":
                    Toggle toggle = go.AddComponent<Toggle>();
                    toggle.isOn = nodeData.isChecked;

                    float boxSize = Mathf.Min(nodeData.height, 30f);
                    GameObject tBgGo = UguiPrefabBakerUtils.CreateChildRect(go, "Background", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                    RectTransform tBgRect = tBgGo.GetComponent<RectTransform>();
                    tBgRect.sizeDelta = new Vector2(boxSize, boxSize);
                    tBgRect.anchoredPosition = new Vector2(boxSize / 2, 0);
                    Image tBgImg = tBgGo.AddComponent<Image>();
                    tBgImg.color = Color.white;

                    GameObject checkGo = UguiPrefabBakerUtils.CreateChildRect(tBgGo, "Checkmark", Vector2.zero, Vector2.one);
                    Image checkImg = checkGo.AddComponent<Image>();
                    checkImg.color = Color.black;
                    RectTransform checkRect = checkGo.GetComponent<RectTransform>();
                    checkRect.offsetMin = new Vector2(4, 4);
                    checkRect.offsetMax = new Vector2(-4, -4);

                    GameObject tLblGo = UguiPrefabBakerUtils.CreateChildRect(go, "Label", Vector2.zero, Vector2.one);
                    RectTransform tLblRect = tLblGo.GetComponent<RectTransform>();
                    tLblRect.offsetMin = new Vector2(boxSize + 10, 0);
                    if (useTMPText)
                    {
                        TextMeshProUGUI tLblTxt = tLblGo.AddComponent<TextMeshProUGUI>();
                        tLblTxt.text = nodeData.text;
                        tLblTxt.color = fontColor;
                        tLblTxt.fontSize = fontSize;
                        tLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        tLblTxt.enableWordWrapping = false;
                    }
                    else
                    {
                        Text tLblTxt = tLblGo.AddComponent<Text>();
                        tLblTxt.text = nodeData.text;
                        tLblTxt.color = fontColor;
                        tLblTxt.fontSize = fontSize;
                        tLblTxt.alignment = TextAnchor.MiddleLeft;
                        tLblTxt.supportRichText = true;
                    }

                    toggle.targetGraphic = tBgImg;
                    toggle.graphic = checkImg;
                    return go.transform;

                case "slider":
                    Slider slider = go.AddComponent<Slider>();
                    slider.value = Mathf.Clamp01(nodeData.value);

                    GameObject sBgGo = UguiPrefabBakerUtils.CreateChildRect(go, "Background", new Vector2(0, 0.25f), new Vector2(1, 0.75f));
                    Image sBgImg = sBgGo.AddComponent<Image>();
                    ConfigureBakedBackgroundImage(sBgImg, nodeData, false);

                    GameObject fillAreaGo = UguiPrefabBakerUtils.CreateChildRect(go, "Fill Area", Vector2.zero, Vector2.one, new Vector2(5, 0), new Vector2(-15, 0));
                    GameObject fillGo = UguiPrefabBakerUtils.CreateChildRect(fillAreaGo, "Fill", Vector2.zero, Vector2.one);
                    Image fillImg = fillGo.AddComponent<Image>();
                    fillImg.color = fontColor;

                    GameObject handleAreaGo = UguiPrefabBakerUtils.CreateChildRect(go, "Handle Slide Area", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
                    GameObject handleGo = UguiPrefabBakerUtils.CreateChildRect(handleAreaGo, "Handle", Vector2.zero, Vector2.one);
                    RectTransform handleRect = handleGo.GetComponent<RectTransform>();
                    handleRect.sizeDelta = new Vector2(20, 0);
                    Image handleImg = handleGo.AddComponent<Image>();
                    handleImg.color = Color.white;

                    slider.targetGraphic = handleImg;
                    slider.fillRect = fillGo.GetComponent<RectTransform>();
                    slider.handleRect = handleRect;
                    return go.transform;

                case "dropdown":
                    Image dBgImg = go.AddComponent<Image>();
                    ConfigureBakedBackgroundImage(dBgImg, nodeData, false);
                    
                    if (useTMPText)
                    {
                        TMP_Dropdown dropdown = go.AddComponent<TMP_Dropdown>();

                        GameObject dLblGo = UguiPrefabBakerUtils.CreateChildRect(go, "Label", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-30, 0));
                        TextMeshProUGUI dLblTxt = dLblGo.AddComponent<TextMeshProUGUI>();
                        dLblTxt.color = fontColor;
                        dLblTxt.fontSize = fontSize;
                        dLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        dLblTxt.enableWordWrapping = false;

                        GameObject arrowGo = UguiPrefabBakerUtils.CreateChildRect(go, "Arrow", new Vector2(1, 0.5f), new Vector2(1, 0.5f));
                        RectTransform arrowRect = arrowGo.GetComponent<RectTransform>();
                        arrowRect.sizeDelta = new Vector2(20, 20);
                        arrowRect.anchoredPosition = new Vector2(-15, 0);
                        Image arrowImg = arrowGo.AddComponent<Image>();
                        arrowImg.color = fontColor;

                        GameObject templateGo = UguiPrefabBakerUtils.CreateChildRect(go, "Template", new Vector2(0, 0), new Vector2(1, 0));
                        RectTransform templateRect = templateGo.GetComponent<RectTransform>();
                        templateRect.pivot = new Vector2(0.5f, 1);
                        templateRect.sizeDelta = new Vector2(0, 150);
                        templateRect.anchoredPosition = new Vector2(0, -2);
                        Image tempImg = templateGo.AddComponent<Image>();
                        tempImg.color = Color.white;

                        ScrollRect tempScroll = templateGo.AddComponent<ScrollRect>();
                        tempScroll.horizontal = false;
                        tempScroll.vertical = true;
                        templateGo.SetActive(false);

                        GameObject dViewportGo = UguiPrefabBakerUtils.CreateChildRect(templateGo, "Viewport", Vector2.zero, Vector2.one);
                        dViewportGo.AddComponent<Image>().color = Color.white;
                        dViewportGo.AddComponent<Mask>();

                        GameObject dContentGo = UguiPrefabBakerUtils.CreateChildRect(dViewportGo, "Content", new Vector2(0, 1), new Vector2(1, 1));
                        RectTransform dContentRect = dContentGo.GetComponent<RectTransform>();
                        dContentRect.pivot = new Vector2(0.5f, 1);
                        dContentRect.sizeDelta = new Vector2(0, 28);

                        GameObject itemGo = UguiPrefabBakerUtils.CreateChildRect(dContentGo, "Item", new Vector2(0, 0.5f), new Vector2(1, 0.5f));
                        RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                        itemRect.sizeDelta = new Vector2(0, 28);
                        Toggle itemToggle = itemGo.AddComponent<Toggle>();

                        GameObject itemBgGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Background", Vector2.zero, Vector2.one);
                        Image itemBgImg = itemBgGo.AddComponent<Image>();
                        itemBgImg.color = Color.white;

                        GameObject itemCheckGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Checkmark", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                        RectTransform itemCheckRect = itemCheckGo.GetComponent<RectTransform>();
                        itemCheckRect.sizeDelta = new Vector2(20, 20);
                        itemCheckRect.anchoredPosition = new Vector2(15, 0);
                        Image itemCheckImg = itemCheckGo.AddComponent<Image>();
                        itemCheckImg.color = Color.black;

                        GameObject itemLblGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Label", Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-10, 0));
                        TextMeshProUGUI itemLblTxt = itemLblGo.AddComponent<TextMeshProUGUI>();
                        itemLblTxt.color = Color.black;
                        itemLblTxt.fontSize = fontSize;
                        itemLblTxt.alignment = TextAlignmentOptions.MidlineLeft;
                        itemLblTxt.enableWordWrapping = false;

                        itemToggle.targetGraphic = itemBgImg;
                        itemToggle.graphic = itemCheckImg;

                        tempScroll.viewport = dViewportGo.GetComponent<RectTransform>();
                        tempScroll.content = dContentRect;

                        dropdown.targetGraphic = dBgImg;
                        dropdown.template = templateRect;
                        dropdown.captionText = dLblTxt;
                        dropdown.itemText = itemLblTxt;

                        if (nodeData.options != null && nodeData.options.Count > 0)
                        {
                            dropdown.ClearOptions();
                            List<TMP_Dropdown.OptionData> optList = new List<TMP_Dropdown.OptionData>();
                            foreach (var opt in nodeData.options) optList.Add(new TMP_Dropdown.OptionData(opt));
                            dropdown.AddOptions(optList);
                        }
                    }
                    else
                    {
                        Dropdown dropdown = go.AddComponent<Dropdown>();

                        GameObject dLblGo = UguiPrefabBakerUtils.CreateChildRect(go, "Label", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-30, 0));
                        Text dLblTxt = dLblGo.AddComponent<Text>();
                        dLblTxt.color = fontColor;
                        dLblTxt.fontSize = fontSize;
                        dLblTxt.alignment = TextAnchor.MiddleLeft;
                        dLblTxt.supportRichText = true;

                        GameObject arrowGo = UguiPrefabBakerUtils.CreateChildRect(go, "Arrow", new Vector2(1, 0.5f), new Vector2(1, 0.5f));
                        RectTransform arrowRect = arrowGo.GetComponent<RectTransform>();
                        arrowRect.sizeDelta = new Vector2(20, 20);
                        arrowRect.anchoredPosition = new Vector2(-15, 0);
                        Image arrowImg = arrowGo.AddComponent<Image>();
                        arrowImg.color = fontColor;

                        GameObject templateGo = UguiPrefabBakerUtils.CreateChildRect(go, "Template", new Vector2(0, 0), new Vector2(1, 0));
                        RectTransform templateRect = templateGo.GetComponent<RectTransform>();
                        templateRect.pivot = new Vector2(0.5f, 1);
                        templateRect.sizeDelta = new Vector2(0, 150);
                        templateRect.anchoredPosition = new Vector2(0, -2);
                        Image tempImg = templateGo.AddComponent<Image>();
                        tempImg.color = Color.white;

                        ScrollRect tempScroll = templateGo.AddComponent<ScrollRect>();
                        tempScroll.horizontal = false;
                        tempScroll.vertical = true;
                        templateGo.SetActive(false);

                        GameObject dViewportGo = UguiPrefabBakerUtils.CreateChildRect(templateGo, "Viewport", Vector2.zero, Vector2.one);
                        dViewportGo.AddComponent<Image>().color = Color.white;
                        dViewportGo.AddComponent<Mask>();

                        GameObject dContentGo = UguiPrefabBakerUtils.CreateChildRect(dViewportGo, "Content", new Vector2(0, 1), new Vector2(1, 1));
                        RectTransform dContentRect = dContentGo.GetComponent<RectTransform>();
                        dContentRect.pivot = new Vector2(0.5f, 1);
                        dContentRect.sizeDelta = new Vector2(0, 28);

                        GameObject itemGo = UguiPrefabBakerUtils.CreateChildRect(dContentGo, "Item", new Vector2(0, 0.5f), new Vector2(1, 0.5f));
                        RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                        itemRect.sizeDelta = new Vector2(0, 28);
                        Toggle itemToggle = itemGo.AddComponent<Toggle>();

                        GameObject itemBgGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Background", Vector2.zero, Vector2.one);
                        Image itemBgImg = itemBgGo.AddComponent<Image>();
                        itemBgImg.color = Color.white;

                        GameObject itemCheckGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Checkmark", new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                        RectTransform itemCheckRect = itemCheckGo.GetComponent<RectTransform>();
                        itemCheckRect.sizeDelta = new Vector2(20, 20);
                        itemCheckRect.anchoredPosition = new Vector2(15, 0);
                        Image itemCheckImg = itemCheckGo.AddComponent<Image>();
                        itemCheckImg.color = Color.black;

                        GameObject itemLblGo = UguiPrefabBakerUtils.CreateChildRect(itemGo, "Item Label", Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-10, 0));
                        Text itemLblTxt = itemLblGo.AddComponent<Text>();
                        itemLblTxt.color = Color.black;
                        itemLblTxt.fontSize = fontSize;
                        itemLblTxt.alignment = TextAnchor.MiddleLeft;
                        itemLblTxt.supportRichText = true;

                        itemToggle.targetGraphic = itemBgImg;
                        itemToggle.graphic = itemCheckImg;

                        tempScroll.viewport = dViewportGo.GetComponent<RectTransform>();
                        tempScroll.content = dContentRect;

                        dropdown.targetGraphic = dBgImg;
                        dropdown.template = templateRect;
                        dropdown.captionText = dLblTxt;
                        dropdown.itemText = itemLblTxt;

                        if (nodeData.options != null && nodeData.options.Count > 0)
                        {
                            dropdown.ClearOptions();
                            List<Dropdown.OptionData> optList = new List<Dropdown.OptionData>();
                            foreach (var opt in nodeData.options) optList.Add(new Dropdown.OptionData(opt));
                            dropdown.AddOptions(optList);
                        }
                    }
                    return go.transform;

                default:
                    Debug.LogWarning($"[UguiBake] 未知节点类型: {nodeData.type}");
                    return go.transform;
            }
        }

        public static void ConfigureCanvasScaler(Canvas canvas, Vector2? targetRes = null)
        {
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // 默认使用1920×1080，如果提供了targetRes则使用提供的值
            Vector2 resolution = targetRes ?? new Vector2(1920, 1080);
            scaler.referenceResolution = resolution;
            scaler.matchWidthOrHeight = 0.5f;
        }

        public static void BakeJsonRootUnderTemplateWithoutShell(UIDataNode rootNode, Transform templateRoot, Vector2 refSize, bool useTMPText = true)
        {
            if (rootNode.children != null && rootNode.children.Count > 0)
            {
                float px = rootNode.x;
                float py = rootNode.y;
                float pw = rootNode.width;
                float ph = rootNode.height;
                foreach (var child in rootNode.children)
                    CreateUINode(child, templateRoot, px, py, pw, ph, useTMPText);
            }
            else
            {
                CreateUINode(rootNode, templateRoot, 0f, 0f, refSize.x, refSize.y, useTMPText);
            }
        }

        public static bool TryBakeJsonStringToPrefab(string jsonContent, string prefabAssetPath, Vector2 refSize, GameObject templatePagePrefab, string sourceHtmlAssetPath, out string error, bool useTMPText = true, bool applyCanvasScaler = true)
        {
            error = null;
            prefabAssetPath = prefabAssetPath?.Replace("\\", "/").Trim();
            if (string.IsNullOrEmpty(prefabAssetPath) || !prefabAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "预制体路径无效（须为 .prefab）。";
                return false;
            }

            if (!prefabAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "预制体须保存在 Assets/ 下。";
                return false;
            }

            if (!TryValidateJson(jsonContent, out error))
                return false;

            var rootNode = ParseUiDataJson(jsonContent);
            string resolveHtml = !string.IsNullOrEmpty(rootNode.sourceHtml) ? rootNode.sourceHtml : sourceHtmlAssetPath;
            BeginImageResolveSession(resolveHtml);
            try
            {
            UguiPrefabBakerUtils.EnsureAssetFoldersForPath(prefabAssetPath);
            string prefabDir = UguiPrefabBakerUtils.AssetPathGetDirectory(prefabAssetPath);
            if (string.IsNullOrEmpty(prefabDir) || !AssetDatabase.IsValidFolder(prefabDir))
            {
                error = $"预制体父目录无效或未导入：{prefabDir}";
                return false;
            }

            if (templatePagePrefab != null)
            {
                string templatePath = AssetDatabase.GetAssetPath(templatePagePrefab);
                if (string.IsNullOrEmpty(templatePath) || !templatePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    error = "页面模板必须是工程内的预制体资源。";
                    return false;
                }

                GameObject contents = null;
                try
                {
                    contents = PrefabUtility.LoadPrefabContents(templatePath);
                    Canvas canvas = contents.GetComponent<Canvas>();
                    if (canvas == null)
                    {
                        error = "页面模板根节点上需要 Canvas 组件。";
                        return false;
                    }

                    if (applyCanvasScaler)
                        ConfigureCanvasScaler(canvas, refSize);

                    BakeJsonRootUnderTemplateWithoutShell(rootNode, contents.transform, refSize, useTMPText);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabAssetPath);
                }
                finally
                {
                    if (contents != null)
                        PrefabUtility.UnloadPrefabContents(contents);
                }

                return true;
            }

            var tempRoot = new GameObject("UguiBake_Pipeline_Temp");
            try
            {
                var canvasGo = new GameObject("Canvas");
                canvasGo.transform.SetParent(tempRoot.transform, false);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<GraphicRaycaster>();

                if (applyCanvasScaler)
                    ConfigureCanvasScaler(canvas, refSize);

                var rootGo = CreateUINode(rootNode, canvas.transform, 0f, 0f, refSize.x, refSize.y, useTMPText);
                PrefabUtility.SaveAsPrefabAsset(rootGo, prefabAssetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }

            return true;
            }
            finally
            {
                EndImageResolveSession();
            }
        }

        public static bool TryValidateJson(string json, out string error)
        {
            error = null;
            UIDataNode node;
            try
            {
                node = string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<UIDataNode>(json);
            }
            catch (JsonException e)
            {
                error = "JSON 解析失败：" + e.Message;
                return false;
            }

            if (node == null)
            {
                error = "无法解析为 UI 树（请确认为单根对象的 JSON）。";
                return false;
            }

            if (string.IsNullOrEmpty(node.name))
            {
                error = "根节点缺少 name 字段。";
                return false;
            }

            if (string.IsNullOrEmpty(node.type))
            {
                error = "根节点缺少 type 字段。";
                return false;
            }

            return true;
        }

        public static UIDataNode ParseUiDataJson(string json)
        {
            return JsonConvert.DeserializeObject<UIDataNode>(json);
        }

        public static Vector2 GetDesignReferenceResolution(UguiBakeConfig config, int selectedResolutionIndex)
        {
            if (config != null && config.supportedResolutions != null && config.supportedResolutions.Count > 0)
            {
                int idx = Mathf.Clamp(selectedResolutionIndex, 0, config.supportedResolutions.Count - 1);
                return config.supportedResolutions[idx].resolution;
            }

            return new Vector2(1920f, 1080f);
        }
    }
}