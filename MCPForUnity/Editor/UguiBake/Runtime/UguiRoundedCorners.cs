using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>
    /// 将矩形 <see cref="Image"/> 网格替换为凸多边形近似圆角矩形，半径为设计像素（与烘焙坐标一致）。
    /// 烘焙器先于 <see cref="UguiLinearGradient"/> 添加本组件，以便渐变仍按顶点位置染色。
    /// </summary>
    [AddComponentMenu("UI/MCPForUnity/Rounded Corners Mesh")]
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public class UguiRoundedCorners : BaseMeshEffect
    {
        const int SegmentsPerCorner = 6;

        [SerializeField] float _radiusTL;
        [SerializeField] float _radiusTR;
        [SerializeField] float _radiusBR;
        [SerializeField] float _radiusBL;

        public void ApplyBakedRadii(float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            _radiusTL = Mathf.Max(0f, topLeft);
            _radiusTR = Mathf.Max(0f, topRight);
            _radiusBR = Mathf.Max(0f, bottomRight);
            _radiusBL = Mathf.Max(0f, bottomLeft);
            if (graphic != null) graphic.SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (graphic != null) graphic.SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (graphic != null) graphic.SetVerticesDirty();
        }
#endif

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            float rMax = Mathf.Max(_radiusTL, _radiusTR, _radiusBR, _radiusBL);
            if (rMax < 0.01f) return;
            if (vh.currentVertCount < 3) return;

            var rect = graphic.rectTransform.rect;
            float rw = rect.width;
            float rh = rect.height;
            if (rw < 1e-4f || rh < 1e-4f) return;

            UIVertex sample = default;
            vh.PopulateUIVertex(ref sample, 0);
            Color32 col = sample.color;

            float x0 = rect.xMin;
            float x1 = rect.xMax;
            float y0 = rect.yMin;
            float y1 = rect.yMax;

            float tl = Mathf.Clamp(_radiusTL, 0f, rw * 0.5f);
            float tr = Mathf.Clamp(_radiusTR, 0f, rw * 0.5f);
            float br = Mathf.Clamp(_radiusBR, 0f, rw * 0.5f);
            float bl = Mathf.Clamp(_radiusBL, 0f, rw * 0.5f);
            tl = Mathf.Min(tl, rh * 0.5f);
            tr = Mathf.Min(tr, rh * 0.5f);
            br = Mathf.Min(br, rh * 0.5f);
            bl = Mathf.Min(bl, rh * 0.5f);

            var boundary = new List<Vector2>(64);
            void AppendArc(Vector2 c, float rad, float a0, float a1, bool dropFirst)
            {
                if (rad < 1e-4f) return;
                int start = dropFirst ? 1 : 0;
                for (int i = start; i <= SegmentsPerCorner; i++)
                {
                    float t = i / (float)SegmentsPerCorner;
                    float ang = Mathf.Lerp(a0, a1, t);
                    boundary.Add(c + rad * new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)));
                }
            }

            AppendArc(new Vector2(x0 + bl, y0 + bl), bl, Mathf.PI, Mathf.PI * 1.5f, false);
            AppendArc(new Vector2(x1 - br, y0 + br), br, Mathf.PI * 1.5f, Mathf.PI * 2f, true);
            AppendArc(new Vector2(x1 - tr, y1 - tr), tr, 0f, Mathf.PI * 0.5f, true);
            AppendArc(new Vector2(x0 + tl, y1 - tl), tl, Mathf.PI * 0.5f, Mathf.PI, true);

            if (boundary.Count < 3) return;

            vh.Clear();

            int AddVert(Vector2 p)
            {
                UIVertex v = default;
                v.position = new Vector3(p.x, p.y, 0f);
                v.color = col;
                v.uv0 = new Vector2(
                    (p.x - x0) / Mathf.Max(rw, 1e-6f),
                    (p.y - y0) / Mathf.Max(rh, 1e-6f));
                v.normal = Vector3.back;
                v.tangent = new Vector4(1f, 0f, 0f, -1f);
                vh.AddVert(v);
                return vh.currentVertCount - 1;
            }

            var idx = new int[boundary.Count];
            for (int i = 0; i < boundary.Count; i++)
                idx[i] = AddVert(boundary[i]);

            Vector2 cen = rect.center;
            int ic = AddVert(cen);
            int n = idx.Length;
            for (int i = 0; i < n; i++)
                vh.AddTriangle(ic, idx[i], idx[(i + 1) % n]);
        }
    }
}
