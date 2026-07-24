// SafeAreaHelper.cs
// 运行时安全区域适配组件：自动调整 RectTransform 以规避刘海屏、圆角边框等不安全区域。
// 挂载到需要适配的 UI 节点上（通常是根 Canvas 下的第一层容器），在 Awake 时自动计算并应用。
//
// 使用方式：
//   1. 烘焙时在根节点设置 safeArea:true，烘焙器会自动挂载此组件
//   2. 也可手动添加到任意 UI 节点
//
// 适配策略：
//   - 读取 Screen.safeArea，将其转换为 RectTransform 的 anchor 和 offset
//   - 支持横竖屏切换（OnRectTransformDimensionsChange 时重新计算）
//   - 可选向下传播：将适配区域作为子节点的设计区域
//

using UnityEngine;

namespace MCPForUnity.Runtime.UguiBake
{
    /// <summary>
    /// 安全区域适配组件：自动规避刘海屏、圆角边框等不安全区域。
    /// 挂载到 UI 节点上，在 Awake 时根据 Screen.safeArea 调整 RectTransform。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaHelper : MonoBehaviour
    {
        // ──────────────────── 配置 ────────────────────

        [Header("适配模式")]
        [Tooltip("水平方向是否适配安全区域（规避左右圆角/刘海）")]
        public bool adaptHorizontal = true;

        [Tooltip("垂直方向是否适配安全区域（规避顶部刘海/底部 Home Indicator）")]
        public bool adaptVertical = true;

        [Tooltip("安全区域外边距补偿（像素），正值缩小安全区域")]
        public float horizontalPadding = 0f;

        [Tooltip("安全区域外边距补偿（像素），正值缩小安全区域")]
        public float verticalPadding = 0f;

        [Header("调试")]
        [Tooltip("强制模拟指定安全区域（宽×高，单位像素），(0,0) 表示使用真实 Screen.safeArea")]
        public Vector2 simulateScreenSize = Vector2.zero;

        // ──────────────────── 内部状态 ────────────────────

        RectTransform _rectTransform;
        Rect _lastSafeArea;
        Vector2 _lastScreenSize;

        // ──────────────────── 生命周期 ────────────────────

        void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        void OnEnable()
        {
            ApplySafeArea();
        }

        void OnRectTransformDimensionsChange()
        {
            // 屏幕尺寸变化（旋转、窗口调整）时重新计算
            ApplySafeArea();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _rectTransform = GetComponent<RectTransform>();
                ApplySafeArea();
            }
        }
#endif

        // ──────────────────── 核心逻辑 ────────────────────

        /// <summary>获取当前安全区域（考虑模拟模式）。</summary>
        Rect GetSafeArea()
        {
            if (simulateScreenSize.x > 0 && simulateScreenSize.y > 0)
            {
                // 模拟模式：假设安全区域为屏幕尺寸的 90%，居中
                float w = simulateScreenSize.x;
                float h = simulateScreenSize.y;
                float margin = Mathf.Min(w, h) * 0.05f;
                return new Rect(margin, margin, w - margin * 2, h - margin * 2);
            }
            return Screen.safeArea;
        }

        /// <summary>应用安全区域到 RectTransform。</summary>
        public void ApplySafeArea()
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null) return;

            Rect safeArea = GetSafeArea();
            Vector2 screenSize = simulateScreenSize.x > 0
                ? simulateScreenSize
                : new Vector2(Screen.width, Screen.height);

            // 检查是否需要更新
            if (safeArea == _lastSafeArea && screenSize == _lastScreenSize)
                return;

            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;

            if (screenSize.x <= 0 || screenSize.y <= 0)
                return;

            // 计算安全区域在 0~1 范围内的归一化坐标
            float safeXMin = safeArea.xMin / screenSize.x;
            float safeXMax = safeArea.xMax / screenSize.x;
            float safeYMin = safeArea.yMin / screenSize.y;
            float safeYMax = safeArea.yMax / screenSize.y;

            // 应用边距补偿
            if (adaptHorizontal && horizontalPadding > 0)
            {
                float padX = horizontalPadding / screenSize.x;
                safeXMin = Mathf.Min(safeXMin + padX, 0.5f);
                safeXMax = Mathf.Max(safeXMax - padX, 0.5f);
            }
            if (adaptVertical && verticalPadding > 0)
            {
                float padY = verticalPadding / screenSize.y;
                safeYMin = Mathf.Min(safeYMin + padY, 0.5f);
                safeYMax = Mathf.Max(safeYMax - padY, 0.5f);
            }

            // 设置锚点和偏移
            if (adaptHorizontal)
            {
                _rectTransform.anchorMin = new Vector2(safeXMin, _rectTransform.anchorMin.y);
                _rectTransform.anchorMax = new Vector2(safeXMax, _rectTransform.anchorMax.y);
            }
            if (adaptVertical)
            {
                _rectTransform.anchorMin = new Vector2(_rectTransform.anchorMin.x, safeYMin);
                _rectTransform.anchorMax = new Vector2(_rectTransform.anchorMax.x, safeYMax);
            }

            // 重置偏移，使 RectTransform 完全填充锚点区域
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>重置 RectTransform 到全屏（撤销安全区域适配）。</summary>
        public void ResetToFullScreen()
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null) return;

            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.one;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }
    }
}
