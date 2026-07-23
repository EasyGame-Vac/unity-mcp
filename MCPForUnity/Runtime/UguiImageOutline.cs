using UnityEngine;
using UnityEngine.UI;

namespace MCPForUnity.Runtime.UguiBake
{
    /// <summary>
    /// HTML/CSS 式描边：基于 Unity 内置 <see cref="Outline"/>（四向挤出顶点），与设计稿像素宽度 <paramref name="widthPx"/> 对齐。
    /// 与 <see cref="UguiLinearGradient"/> 同挂时，烘焙器先加渐变再加描边，保证描边不被渐变染色。
    /// </summary>
    [AddComponentMenu("UI/MCPForUnity/Image Stroke (Outline)")]
    [DisallowMultipleComponent]
    public class UguiImageOutline : Outline
    {
        [SerializeField] float _designWidthPx = 2f;

        public float DesignWidthPx => _designWidthPx;

        /// <summary>由 JSON / 烘焙器设置；<paramref name="widthPx"/> 为设计像素（与烘焙基准分辨率一致）。</summary>
        public void ApplyBakedStroke(float widthPx, Color color)
        {
            _designWidthPx = Mathf.Max(0f, widthPx);
            effectColor = color;
            useGraphicAlpha = false;
            float w = Mathf.Clamp(_designWidthPx, 0f, 600f);
            effectDistance = new Vector2(w, w);
            if (graphic != null) graphic.SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (graphic != null) graphic.SetVerticesDirty();
        }
#endif
    }
}
