using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace MCPForUnity.Runtime.UguiBake
{
    /// <summary>
    /// UI 分辨率配置数据结构
    /// </summary>
    [System.Serializable]
    public class UIResolutionConfig
    {
        public string displayName;
        public Vector2 resolution;
    }

    /// <summary>
    /// UGUI 烘焙器全局配置 (ScriptableObject)
    /// 用于统一管理多分辨率预设与对应的 DSL 规范文档模板
    /// </summary>
    [CreateAssetMenu(fileName = "UguiBakeConfig", menuName = "UI Architecture/UGUI Bake Config")]
    public class UguiBakeConfig : ScriptableObject
    {
        [Header("支持的分辨率预设")]
        public List<UIResolutionConfig> supportedResolutions = new List<UIResolutionConfig>()
        {
            new UIResolutionConfig { displayName = "PC 横屏 (1920x1080)", resolution = new Vector2(1920, 1080) },
            new UIResolutionConfig { displayName = "Mobile 竖屏 (1080x1920)", resolution = new Vector2(1080, 1920) },
            new UIResolutionConfig { displayName = "Pad 横屏 (2048x1536)", resolution = new Vector2(2048, 1536) }
        };

        [Header("DSL 文档模板 (.md 文件)")]
        [Tooltip("请拖入包含 {WIDTH} 和 {HEIGHT} 占位符的 Markdown 模板文件")]
        public TextAsset dslTemplateAsset;

        [Header("文本组件设置")]
        [Tooltip("勾选使用 TextMeshPro (TMP)，取消勾选使用旧版 UnityEngine.UI.Text")]
        public bool useTMPText = true;

        [Tooltip("TMP 默认字体资源（留空则使用 TMP 全局默认字体）")]
        public TMP_FontAsset defaultTmpFont;

        [Tooltip("旧版 Text 默认字体（留空则使用系统默认字体）")]
        public Font defaultLegacyFont;

        [Header("页面模板预制体")]
        [Tooltip("默认页面模板预制体（根节点需有 Canvas 组件）。留空则从零创建 Canvas。")]
        public GameObject defaultTemplatePrefab;
    }
}
