using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MCPForUnity.Editor.UguiBake
{
    /// <summary>
    /// 近似 HTML/CSS <c>linear-gradient()</c>：在 <see cref="Image"/>（或其它 <see cref="Graphic"/>）网格上按顶点着色做线性多色标渐变。
    /// 使用前请保证 <see cref="Image.sprite"/> 非空（烘焙器会挂白块 Sprite），且渐变叠加在 <see cref="Graphic.color"/> 之上（通常设为白）。
    /// </summary>
    [AddComponentMenu("UI/MCPForUnity/Css Linear Gradient")]
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public class UguiLinearGradient : BaseMeshEffect
    {
        [Tooltip("与 CSS deg 一致：0° 朝上，顺时针。")]
        [SerializeField] float _angleDeg = 180f;
        [SerializeField] List<Color> _colors = new List<Color> { Color.white, Color.black };
        [Tooltip("与 Color 数量相同、0~1；留空则均分。")]
        [SerializeField] List<float> _positions = new List<float>();

        public float AngleDeg
        {
            get => _angleDeg;
            set { _angleDeg = value; if (graphic != null) graphic.SetVerticesDirty(); }
        }

        public IReadOnlyList<Color> GradientColors => _colors;

        public void SetGradient(float angleDeg, IReadOnlyList<Color> colors, IReadOnlyList<float> normalizedPositions)
        {
            _angleDeg = angleDeg;
            _colors = new List<Color>(colors);
            _positions = normalizedPositions != null && normalizedPositions.Count > 0
                ? new List<float>(normalizedPositions)
                : null;
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
            if (!IsActive() || vh.currentVertCount == 0 || _colors == null || _colors.Count < 2)
                return;

            var verts = new List<UIVertex>(vh.currentVertCount);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                UIVertex v = default;
                vh.PopulateUIVertex(ref v, i);
                verts.Add(v);
            }

            float left = verts[0].position.x;
            float right = verts[0].position.x;
            float bottom = verts[0].position.y;
            float top = verts[0].position.y;
            for (var i = 1; i < verts.Count; ++i)
            {
                Vector3 p = verts[i].position;
                if (p.x > right) right = p.x;
                else if (p.x < left) left = p.x;
                if (p.y > top) top = p.y;
                else if (p.y < bottom) bottom = p.y;
            }

            float width = right - left;
            float height = top - bottom;
            if (width < 1e-4f || height < 1e-4f)
                return;

            float rad = _angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            dir.Normalize();

            float minProj = float.MaxValue;
            float maxProj = float.MinValue;
            var corners = new[]
            {
                new Vector2(left, bottom),
                new Vector2(left, top),
                new Vector2(right, top),
                new Vector2(right, bottom),
            };
            foreach (Vector2 c in corners)
            {
                float p = Vector2.Dot(c, dir);
                minProj = Mathf.Min(minProj, p);
                maxProj = Mathf.Max(maxProj, p);
            }

            float span = maxProj - minProj;
            if (span < 1e-6f)
                span = 1f;

            for (var i = 0; i < verts.Count; i++)
            {
                UIVertex v = verts[i];
                Vector2 pos = new Vector2(v.position.x, v.position.y);
                float t = (Vector2.Dot(pos, dir) - minProj) / span;
                Color g = SampleGradient(Mathf.Clamp01(t));
                v.color = new Color(g.r, g.g, g.b, g.a * v.color.a);
                vh.SetUIVertex(v, i);
            }
        }

        Color SampleGradient(float t)
        {
            int n = _colors.Count;
            if (n == 0) return Color.white;
            if (n == 1) return _colors[0];

            float[] stops = GetStops(n);
            if (t <= stops[0]) return _colors[0];
            if (t >= stops[n - 1]) return _colors[n - 1];

            int seg = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (t >= stops[i] && t <= stops[i + 1])
                {
                    seg = i;
                    break;
                }
            }

            float t0 = stops[seg];
            float t1 = stops[seg + 1];
            float u = t1 > t0 ? (t - t0) / (t1 - t0) : 0f;
            u = Mathf.Clamp01(u);
            return Color.LerpUnclamped(_colors[seg], _colors[seg + 1], u);
        }

        float[] GetStops(int n)
        {
            if (_positions != null && _positions.Count == n)
            {
                var arr = new float[n];
                for (int i = 0; i < n; i++) arr[i] = Mathf.Clamp01(_positions[i]);
                for (int i = 1; i < n; i++)
                    if (arr[i] < arr[i - 1])
                        arr[i] = arr[i - 1];
                return arr;
            }

            var fallback = new float[n];
            if (n == 1)
            {
                fallback[0] = 0f;
                return fallback;
            }

            for (int i = 0; i < n; i++)
                fallback[i] = i / (float)(n - 1);
            return fallback;
        }
    }
}
