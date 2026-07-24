using System.Collections.Generic;
using MCPForUnity.Runtime.UguiBake;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>从 HTML <c>linear-gradient(...)</c> 解析出的数据，供 Unity <see cref="UguiLinearGradient"/> 使用。</summary>
    [System.Serializable]
    public class UIDataLinearGradient
    {
        /// <summary>角度（CSS <c>deg</c>：0 朝上、顺时针）。</summary>
        public float angle = 180f;
        /// <summary>色标颜色（#RRGGBB 或 #RRGGBBAA）。</summary>
        public List<string> colors;
        /// <summary>与 <see cref="colors"/> 等长的 0~1 位置；可省略，由烘焙器均分。</summary>
        public List<float> positions;
    }

    /// <summary>描边（<c>data-u-outline-*</c> 或 CSS <c>outline</c>），由 <see cref="UguiImageOutline"/> 表现。</summary>
    [System.Serializable]
    public class UIDataOutline
    {
        /// <summary>设计稿像素宽度（与 DSL 基准分辨率一致）。</summary>
        public float width;
        /// <summary>描边颜色 #RRGGBB / #RRGGBBAA。</summary>
        public string color;
    }

    /// <summary>CSS <c>border</c>（四边宽度一致时由 HTML 烘焙导出；非一致时取最大边宽度）。</summary>
    [System.Serializable]
    public class UIDataCssBorder
    {
        public float width;
        public string color;
    }

    [System.Serializable]
    public class UIDataNode
    {
        public string name;
        public string type;
        public string dir;
        public float value;
        public bool isChecked;
        public List<string> options;
        public float x;
        public float y;
        public float width;
        public float height;
        public string color;
        public string fontColor;
        public int fontSize;
        public string textAlign;
        public string text;
        /// <summary>布局：<c>absolute</c>（默认）|<c>stretch</c>|<c>center</c>|<c>stretch-h</c>|<c>stretch-v</c>，来自 data-u-layout。</summary>
        public string layout;
        public UIDataLinearGradient linearGradient;
        public UIDataOutline outline;
        /// <summary>CSS 圆角半径（像素），顺序：左上、右上、右下、左下；可 1~4 个元素（同 CSS 简写规则）。</summary>
        public List<float> borderRadius;
        /// <summary>CSS <c>border</c>；与 <see cref="outline"/> 同时存在时烘焙器优先应用 DSL 描边。</summary>
        public UIDataCssBorder border;
        /// <summary>背景图片路径，来自 CSS background-image 属性</summary>
        [Newtonsoft.Json.JsonProperty("image")]
        public string image;
        /// <summary>源 HTML 的 Assets 相对路径（如 Assets/_Test/TempPage/battle-record.html），用于将 <see cref="image"/> 相对路径解析为工程内 Sprite。</summary>
        [Newtonsoft.Json.JsonProperty("sourceHtml")]
        public string sourceHtml;
        /// <summary>自适应尺寸模式：none(默认) | horizontal | vertical | both。启用后烘焙时挂载 ContentSizeFitter。</summary>
        [Newtonsoft.Json.JsonProperty("autoSize")]
        public string autoSize;
        /// <summary>是否挂载 SafeAreaHelper 组件以适配刘海屏/圆角边框。仅在根节点生效。</summary>
        [Newtonsoft.Json.JsonProperty("safeArea")]
        public bool safeArea;
        /// <summary>CanvasScaler 的 matchWidthOrHeight 值（0=完全匹配宽度，1=完全匹配高度，0.5=平衡）。仅根节点生效，默认 0.5。</summary>
        [Newtonsoft.Json.JsonProperty("scalerMatch")]
        public float scalerMatch = 0.5f;
        /// <summary>模板自带 bg 子节点处理：keep(默认) | hide。仅根节点生效（data-u-template-bg）。</summary>
        [Newtonsoft.Json.JsonProperty("templateBg")]
        public string templateBg;
        /// <summary>ScrollRect Content 布局：none(默认) | vertical | horizontal。非 none 时烘焙器在 Content 上挂载对应 LayoutGroup + ContentSizeFitter。</summary>
        [Newtonsoft.Json.JsonProperty("contentLayout")]
        public string contentLayout;
        /// <summary>Content 布局项间距（来自 CSS gap）。</summary>
        [Newtonsoft.Json.JsonProperty("contentSpacing")]
        public float contentSpacing;
        /// <summary>Content 布局内边距（来自 CSS padding，顺序：上右下左，同 CSS 简写规则解析）。</summary>
        [Newtonsoft.Json.JsonProperty("contentPadding")]
        public List<float> contentPadding;
        public List<UIDataNode> children;
    }
}