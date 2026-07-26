// VfxPrefabBakerCore.cs
// VfxBake 烘焙核心：VFX-DSL JSON → GameObject 树（ParticleSystem）→ Prefab 资产。
// 纯编辑器逻辑，不依赖 MCP 传输层；由 VfxBakeBridge 编排调用。
//
// DSL 概览（完整规范见 Editor/VfxBake/Docs/AI-Workflow.md）：
//   { "name": "...", "systems": [ { "name", "transform", "main", "emission",
//     "shape", "colorOverLifetime", "sizeOverLifetime", "velocityOverLifetime",
//     "textureSheetAnimation", "trails", "noise", "renderer", "children" } ] }
//
// 通用约定：
//   - 数值字段：number 或 [min, max] 两元素数组（MinMaxCurve 两常数随机）
//   - 颜色字段："#rgb" / "#rgba" / "#rrggbb" / "#rrggbbaa" 十六进制
//   - 曲线字段：[[time, value], ...] 点数组（线性切线 AnimationCurve）
//   - 未出现的字段一律保留 ParticleSystem 默认值

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using MCPForUnity.Runtime.VfxBake;
using UnityEngine.Rendering;

namespace MCPForUnity.Editor.VfxBake
{
    /// <summary>
    /// VFX 烘焙核心：将 VFX-DSL JSON 构建为 ParticleSystem 预制体。
    /// </summary>
    public static class VfxPrefabBakerCore
    {
        // ──────────────────── 贴图材质缓存 ────────────────────

        /// <summary>贴图材质输出目录（烘焙出的 .mat 资产存放处）。</summary>
        public const string DefaultMaterialDir = "Assets/MCP/VfxBake/Baked/Materials";

        /// <summary>贴图→材质缓存，避免同一贴图重复创建材质资产（key: texPath|shaderName|blendMode）。</summary>
        static readonly Dictionary<string, Material> _texMaterialCache = new Dictionary<string, Material>();

        // ──────────────────── 管线 / Shader / 混合模式 ────────────────────

        /// <summary>粒子混合模式。</summary>
        enum ParticleBlend { Alpha, Additive }

        /// <summary>当前是否 URP/HDRP（SRP 管线）。Built-in 返回 false。</summary>
        static bool IsSrp() => GraphicsSettings.currentRenderPipeline != null;

        /// <summary>
        /// 按当前管线解析粒子 shader：显式 shaderRef 优先；否则 SRP 用 Particles/Unlit，Built-in 回退 Default-Particle 基底。
        /// shaderRef 支持别名："urp-particle-unlit" / "particle-unlit" / 直接 shader 名。
        /// </summary>
        static Shader ResolveParticleShader(string shaderRef)
        {
            if (!string.IsNullOrWhiteSpace(shaderRef))
            {
                string s = shaderRef.Trim();
                if (s.Equals("urp-particle-unlit", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("particle-unlit", StringComparison.OrdinalIgnoreCase))
                    s = "Universal Render Pipeline/Particles/Unlit";
                var sh = Shader.Find(s);
                if (sh != null) return sh;
                Debug.LogWarning($"[VfxPrefabBakerCore] 找不到 shader '{shaderRef}'，按管线自动选择。");
            }
            if (IsSrp())
            {
                var urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (urp != null) return urp;
            }
            // Built-in 回退：沿用内置 Default-Particle 的 shader
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat")?.shader;
        }

        /// <summary>解析 blendMode 字符串（additive/add → Additive；其余 → Alpha）。</summary>
        static ParticleBlend ParseBlendMode(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ParticleBlend.Alpha;
            s = s.Trim().ToLowerInvariant();
            return (s == "additive" || s == "add") ? ParticleBlend.Additive : ParticleBlend.Alpha;
        }

        /// <summary>把混合模式写入 URP Particles/Unlit 材质（_Surface=0 不透明/1透明；_Blend 见 BlendMode 枚举）。</summary>
        static void ApplyUrpBlendMode(Material mat, ParticleBlend blend)
        {
            if (mat == null || mat.shader == null) return;
            if (!mat.shader.name.Contains("Universal Render Pipeline/Particles/Unlit")) return;
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", blend == ParticleBlend.Additive ? 4f : 0f); // 4=Additive, 0=Alpha
        }

        /// <summary>
        /// 按贴图文件名后缀推断混合模式：_add/_addb → Additive；_alpha → Alpha；无后缀 → null（无法判断）。
        /// </summary>
        static ParticleBlend? InferBlendFromTextureName(string texRef)
        {
            if (string.IsNullOrWhiteSpace(texRef)) return null;
            string fn = Path.GetFileNameWithoutExtension(texRef).ToLowerInvariant();
            if (fn.EndsWith("_add") || fn.EndsWith("_addb")) return ParticleBlend.Additive;
            if (fn.EndsWith("_alpha")) return ParticleBlend.Alpha;
            return null;
        }

        /// <summary>
        /// 解析最终混合模式：显式 blendModeRef 优先；缺省按贴图后缀推断；仍无 → Alpha 兜底并告警。
        /// </summary>
        static ParticleBlend ResolveBlend(string blendModeRef, string texRef)
        {
            if (!string.IsNullOrWhiteSpace(blendModeRef))
                return ParseBlendMode(blendModeRef);

            var inferred = InferBlendFromTextureName(texRef);
            if (inferred.HasValue)
                return inferred.Value;

            Debug.LogWarning($"[VfxPrefabBakerCore] 贴图 '{texRef}' 无 _add/_addb/_alpha 后缀，无法推断混合模式，按 Alpha 兜底（建议补后缀）。");
            return ParticleBlend.Alpha;
        }

        /// <summary>
        /// 从 JSON 字符串烘焙预制体。成功返回 true，失败返回 false 并输出错误信息。
        /// </summary>
        /// <param name="jsonContent">VFX-DSL JSON 字符串</param>
        /// <param name="prefabPath">输出预制体路径（Assets 相对，.prefab 结尾）</param>
        /// <param name="error">失败时的错误描述</param>
        public static bool TryBakeJsonStringToPrefab(string jsonContent, string prefabPath, out string error)
        {
            error = null;

            JObject rootJson;
            try
            {
                rootJson = JObject.Parse(jsonContent);
            }
            catch (Exception e)
            {
                error = $"JSON 解析失败: {e.Message}";
                return false;
            }

            string vfxName = rootJson["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(vfxName))
            {
                error = "JSON 缺少必填字段 'name'";
                return false;
            }

            var systems = rootJson["systems"] as JArray;
            if (systems == null || systems.Count == 0)
            {
                error = "JSON 字段 'systems' 必须是非空数组";
                return false;
            }

            GameObject root = null;
            try
            {
                // 根节点只挂 VfxAutoDestroy，粒子系统全部作为子节点（与 DSL 同构）。
                root = new GameObject(vfxName);
                root.AddComponent<VfxAutoDestroy>();

                foreach (var sysToken in systems)
                {
                    if (!(sysToken is JObject sysObj))
                    {
                        error = "systems 数组元素必须是对象";
                        UnityEngine.Object.DestroyImmediate(root);
                        return false;
                    }

                    string sysError;
                    if (!BuildSystemObject(sysObj, root.transform, out sysError))
                    {
                        error = sysError;
                        UnityEngine.Object.DestroyImmediate(root);
                        return false;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            catch (Exception e)
            {
                error = $"烘焙异常: {e.Message}";
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                return false;
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ──────────────────── 单个粒子系统构建 ────────────────────

        /// <summary>构建一个粒子系统节点（含 children 递归）。</summary>
        static bool BuildSystemObject(JObject sys, Transform parent, out string error)
        {
            error = null;
            string sysName = sys["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(sysName))
            {
                error = "粒子系统缺少必填字段 'name'";
                return false;
            }

            try
            {
                var go = new GameObject(sysName);
                go.transform.SetParent(parent, false);

                // Transform
                ApplyTransform(go.transform, sys["transform"] as JObject);

                // ParticleSystem 各模块（先 AddComponent 拿默认值，再按字段覆盖）
                var ps = go.AddComponent<ParticleSystem>();
                ApplyMain(ps, sys["main"] as JObject);
                ApplyEmission(ps, sys["emission"] as JObject);
                ApplyShape(ps, sys["shape"] as JObject);
                ApplyColorOverLifetime(ps, sys["colorOverLifetime"] as JObject);
                ApplySizeOverLifetime(ps, sys["sizeOverLifetime"] as JObject);
                ApplyVelocityOverLifetime(ps, sys["velocityOverLifetime"] as JObject);
                ApplyTextureSheetAnimation(ps, sys["textureSheetAnimation"] as JObject);
                ApplyTrails(ps, sys["trails"] as JObject);
                ApplyNoise(ps, sys["noise"] as JObject);
                ApplyRenderer(go, sys["renderer"] as JObject);

                // 子系统递归
                if (sys["children"] is JArray children)
                {
                    foreach (var childToken in children)
                    {
                        if (!(childToken is JObject childObj))
                        {
                            error = $"系统 '{sysName}' 的 children 数组元素必须是对象";
                            UnityEngine.Object.DestroyImmediate(go);
                            return false;
                        }

                        if (!BuildSystemObject(childObj, go.transform, out error))
                        {
                            UnityEngine.Object.DestroyImmediate(go);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"系统 '{sysName}' 构建失败: {e.Message}";
                return false;
            }
        }

        // ──────────────────── 模块应用 ────────────────────

        static void ApplyTransform(Transform t, JObject node)
        {
            if (node == null) return;

            if (node["position"] is JArray pos && pos.Count >= 3)
                t.localPosition = new Vector3(ToFloat(pos[0]), ToFloat(pos[1]), ToFloat(pos[2]));
            // 旋转为欧拉角（度）
            if (node["rotation"] is JArray rot && rot.Count >= 3)
                t.localRotation = Quaternion.Euler(ToFloat(rot[0]), ToFloat(rot[1]), ToFloat(rot[2]));
            if (node["scale"] is JArray scl && scl.Count >= 3)
                t.localScale = new Vector3(ToFloat(scl[0]), ToFloat(scl[1]), ToFloat(scl[2]));
        }

        static void ApplyMain(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var main = ps.main;

            if (node["duration"] != null)
                main.duration = ToFloat(node["duration"]);
            if (node["looping"] != null)
                SetLooping(ps, node["looping"].Value<bool>());
            if (node["startDelay"] != null)
                main.startDelay = ParseMinMaxCurve(node["startDelay"], 0f);
            if (node["startLifetime"] != null)
                main.startLifetime = ParseMinMaxCurve(node["startLifetime"], 5f);
            if (node["startSpeed"] != null)
                main.startSpeed = ParseMinMaxCurve(node["startSpeed"], 5f);
            if (node["startSize"] != null)
                main.startSize = ParseMinMaxCurve(node["startSize"], 1f);
            // 初始旋转：DSL 用角度制，Unity 内部为弧度
            if (node["startRotation"] != null)
                main.startRotation = ParseMinMaxCurve(node["startRotation"], 0f, Mathf.Deg2Rad);
            if (node["startColor"] != null)
                main.startColor = new ParticleSystem.MinMaxGradient(ParseColor(node["startColor"].ToString(), Color.white));
            if (node["gravityModifier"] != null)
                main.gravityModifier = ParseMinMaxCurve(node["gravityModifier"], 0f);
            if (node["simulationSpace"] != null)
                main.simulationSpace = ParseSimulationSpace(node["simulationSpace"].ToString());
            if (node["maxParticles"] != null)
                main.maxParticles = node["maxParticles"].Value<int>();
            if (node["playOnAwake"] != null)
                main.playOnAwake = node["playOnAwake"].Value<bool>();
        }

        /// <summary>
        /// 设置循环标记。2022.3.62t4（腾讯定制版）的 MainModule 移除了 looping 托管属性，
        /// 改走序列化属性写入；属性不存在时仅告警跳过，不阻断烘焙。
        /// </summary>
        static void SetLooping(ParticleSystem ps, bool looping)
        {
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("looping");
            if (prop != null && prop.propertyType == SerializedPropertyType.Boolean)
            {
                prop.boolValue = looping;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 looping，已跳过。");
            }
        }

        static void ApplyEmission(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var emission = ps.emission;

            if (node["rateOverTime"] != null)
                emission.rateOverTime = ParseMinMaxCurve(node["rateOverTime"], 10f);

            if (node["bursts"] is JArray bursts)
            {
                var list = new List<ParticleSystem.Burst>();
                foreach (var burstToken in bursts)
                {
                    if (!(burstToken is JObject b)) continue;
                    float time = b["time"] != null ? ToFloat(b["time"]) : 0f;
                    float count = b["count"] != null ? ToFloat(b["count"]) : 1f;
                    list.Add(new ParticleSystem.Burst(time, count));
                }
                emission.SetBursts(list.ToArray());
            }
        }

        static void ApplyShape(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var shape = ps.shape;

            if (node["enabled"] != null)
                shape.enabled = node["enabled"].Value<bool>();
            if (node["type"] != null)
                shape.shapeType = ParseShapeType(node["type"].ToString());
            if (node["radius"] != null)
                shape.radius = ToFloat(node["radius"]);
            if (node["angle"] != null)
                shape.angle = ToFloat(node["angle"]);
            if (node["arc"] != null)
                shape.arc = ToFloat(node["arc"]);
        }

        static void ApplyColorOverLifetime(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var col = ps.colorOverLifetime;

            if (node["enabled"] != null)
                col.enabled = node["enabled"].Value<bool>();

            if (node["gradient"] is JArray gradientArr && gradientArr.Count > 0)
                col.color = new ParticleSystem.MinMaxGradient(ParseGradient(gradientArr));
        }

        static void ApplySizeOverLifetime(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var sol = ps.sizeOverLifetime;

            if (node["enabled"] != null)
                sol.enabled = node["enabled"].Value<bool>();

            if (node["curve"] is JArray curveArr && curveArr.Count > 0)
                sol.size = new ParticleSystem.MinMaxCurve(1f, ParseAnimationCurve(curveArr));
        }

        static void ApplyVelocityOverLifetime(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var vol = ps.velocityOverLifetime;

            if (node["enabled"] != null)
                vol.enabled = node["enabled"].Value<bool>();
            if (node["x"] != null)
                vol.x = ParseMinMaxCurve(node["x"], 0f);
            if (node["y"] != null)
                vol.y = ParseMinMaxCurve(node["y"], 0f);
            if (node["z"] != null)
                vol.z = ParseMinMaxCurve(node["z"], 0f);
        }

        /// <summary>
        /// 序列帧动画：贴图按 tilesX×tilesY 网格切分，frameOverTime 控制帧推进。
        /// frameOverTime 归一化到 [0,1]（0=第一帧，1=最后一帧），曲线语义与 sizeOverLifetime 一致。
        /// 2022.3.62t4（腾讯定制版）移除了 tilesX/tilesY/animationType/cycles 的托管属性，
        /// 这些字段改走序列化属性写入（属性不存在时仅告警跳过）。
        /// </summary>
        static void ApplyTextureSheetAnimation(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var uv = ps.textureSheetAnimation;

            if (node["enabled"] != null)
                uv.enabled = node["enabled"].Value<bool>();

            // 2022.3.62t4（腾讯定制版）移除了 tilesX/tilesY/animationType/cycles 的托管属性，
            // 全部改走序列化属性写入（属性不存在时仅告警跳过，不阻断烘焙）。
            bool needApply = false;
            var so = new SerializedObject(ps);

            if (node["tilesX"] != null)
            {
                int v = Mathf.Max(1, node["tilesX"].Value<int>());
                if (TrySetSerializedInt(so, "UVModule.tilesX", v)) needApply = true;
                else Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 tilesX，已跳过。");
            }
            if (node["tilesY"] != null)
            {
                int v = Mathf.Max(1, node["tilesY"].Value<int>());
                if (TrySetSerializedInt(so, "UVModule.tilesY", v)) needApply = true;
                else Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 tilesY，已跳过。");
            }
            if (node["animationType"] != null)
            {
                int v = (int)ParseSheetAnimationType(node["animationType"].ToString());
                if (TrySetSerializedInt(so, "UVModule.animationType", v)) needApply = true;
                else Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 animationType，已跳过。");
            }
            if (node["rowIndex"] != null)
                uv.rowIndex = Mathf.Max(0, node["rowIndex"].Value<int>());

            // frameOverTime：曲线数组 → 归一化曲线；number/[min,max] → 常数帧位置（少用）
            if (node["frameOverTime"] is JArray curveArr && curveArr.Count > 0
                && curveArr[0] is JArray)
            {
                uv.frameOverTime = new ParticleSystem.MinMaxCurve(1f, ParseAnimationCurve(curveArr));
            }
            else if (node["frameOverTime"] != null)
            {
                uv.frameOverTime = ParseMinMaxCurve(node["frameOverTime"], 0f);
            }

            // startFrame：帧索引（0 起）
            if (node["startFrame"] != null)
                uv.startFrame = ParseMinMaxCurve(node["startFrame"], 0f);

            if (node["cycles"] != null)
            {
                float v = ToFloat(node["cycles"]);
                if (TrySetSerializedFloat(so, "UVModule.cycles", v)) needApply = true;
                else Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 cycles，已跳过。");
            }

            if (needApply)
                so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 拖尾模块：粒子运动轨迹生成拖尾网格。
        /// 注意：拖尾材质由 renderer.trailMaterial 指定（未指定时复用 renderer 主材质）。
        /// </summary>
        static void ApplyTrails(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var trails = ps.trails;

            if (node["enabled"] != null)
                trails.enabled = node["enabled"].Value<bool>();
            // 产生拖尾的粒子比例 [0,1]
            if (node["ratio"] != null)
                trails.ratio = ToFloat(node["ratio"]);
            // 拖尾存活时长（相对粒子 lifetime 的倍率）
            if (node["lifetime"] != null)
                trails.lifetime = ParseMinMaxCurve(node["lifetime"], 1f);
            // 拖尾宽度曲线（沿拖尾方向，[[t, v], ...]）
            if (node["widthOverTrail"] is JArray widthArr && widthArr.Count > 0)
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, ParseAnimationCurve(widthArr));
            // 拖尾颜色渐变（沿拖尾方向 / 沿粒子生命周期）
            if (node["colorOverTrail"] is JArray trailGrad && trailGrad.Count > 0)
                trails.colorOverTrail = new ParticleSystem.MinMaxGradient(ParseGradient(trailGrad));
            if (node["colorOverLifetime"] is JArray lifeGrad && lifeGrad.Count > 0)
                trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(ParseGradient(lifeGrad));

            if (node["inheritParticleColor"] != null)
                trails.inheritParticleColor = node["inheritParticleColor"].Value<bool>();
            if (node["dieWithParticles"] != null)
                trails.dieWithParticles = node["dieWithParticles"].Value<bool>();
            if (node["sizeAffectsWidth"] != null)
                trails.sizeAffectsWidth = node["sizeAffectsWidth"].Value<bool>();
        }

        /// <summary>噪声模块：对粒子位置施加湍流扰动（烟雾/火焰更自然）。</summary>
        static void ApplyNoise(ParticleSystem ps, JObject node)
        {
            if (node == null) return;
            var noise = ps.noise;

            if (node["enabled"] != null)
                noise.enabled = node["enabled"].Value<bool>();
            if (node["strength"] != null)
                noise.strength = ParseMinMaxCurve(node["strength"], 1f);
            if (node["frequency"] != null)
                noise.frequency = Mathf.Max(0.01f, ToFloat(node["frequency"]));
            if (node["scrollSpeed"] != null)
                noise.scrollSpeed = ParseMinMaxCurve(node["scrollSpeed"], 0f);
            if (node["damping"] != null)
                noise.damping = node["damping"].Value<bool>();

            // octaves：腾讯定制版移除了托管属性，统一走序列化属性写入
            if (node["octaves"] != null)
            {
                int v = Mathf.Clamp(node["octaves"].Value<int>(), 1, 4);
                var so = new SerializedObject(ps);
                if (TrySetSerializedInt(so, "NoiseModule.octaves", v))
                    so.ApplyModifiedPropertiesWithoutUndo();
                else
                    Debug.LogWarning("[VfxPrefabBakerCore] 当前 Unity 版本无法设置 octaves，已跳过。");
            }
        }

        static void ApplyRenderer(GameObject go, JObject node)
        {
            if (node == null) return;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            if (node["renderMode"] != null)
                renderer.renderMode = ParseRenderMode(node["renderMode"].ToString());
            if (node["sortingOrder"] != null)
                renderer.sortingOrder = node["sortingOrder"].Value<int>();

            string textureRef = node["texture"]?.ToString();
            string materialRef = node["material"]?.ToString();
            string shaderRef = node["shader"]?.ToString();
            string blendModeRef = node["blendMode"]?.ToString();
            float hdrIntensity = node["hdrIntensity"] != null ? node["hdrIntensity"].Value<float>() : 0f;

            // 1) texture 优先：以基底材质（默认或 material 指定）克隆后写入 _MainTex，
            //    生成可持久化的 .mat 资产并缓存，使预制体保存后贴图不丢失。
            if (!string.IsNullOrWhiteSpace(textureRef))
            {
                Texture2D tex = ResolveTexture(textureRef);
                if (tex != null)
                {
                    // 混合模式：显式 blendMode > 贴图后缀推断 > Alpha 兜底（告警在 ResolveBlend 内）
                    var blend = ResolveBlend(blendModeRef, textureRef);
                    Material baseMat = LoadMaterialForClone(materialRef, shaderRef);
                    renderer.sharedMaterial = GetOrCreateParticleMaterial(tex, baseMat, blend, hdrIntensity);
                    return;
                }
                Debug.LogWarning($"[VfxPrefabBakerCore] 无法加载贴图 '{textureRef}'，回退到 material 字段解析。");
            }

            // 2) 仅 material：沿用原逻辑（default-particle 或 Assets 相对路径）
            if (materialRef != null)
                renderer.sharedMaterial = LoadMaterialForAssign(materialRef);

            // 3) 拖尾材质：trails 模块启用时拖尾网格需要独立材质。
            //    未显式指定时复用主材质，避免拖尾不可见。
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null && ps.trails.enabled)
            {
                string trailMaterialRef = node["trailMaterial"]?.ToString();
                renderer.trailMaterial = !string.IsNullOrWhiteSpace(trailMaterialRef)
                    ? LoadMaterialForAssign(trailMaterialRef)
                    : renderer.sharedMaterial;
            }
        }

        // ──────────────────── 贴图 / 材质解析 ────────────────────

        /// <summary>
        /// 加载贴图：支持带/不带扩展名的 Assets 相对路径。
        /// 不带扩展名时按常见格式依次尝试。
        /// </summary>
        static Texture2D ResolveTexture(string texRef)
        {
            if (string.IsNullOrWhiteSpace(texRef))
                return null;

            string path = texRef.Replace("\\", "/").Trim();

            // 精确路径
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            // 无扩展名时尝试常见贴图格式
            if (Path.GetExtension(path).Length == 0)
            {
                string[] exts = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".hdr", ".bmp" };
                foreach (var ext in exts)
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path + ext);
                    if (tex != null) return tex;
                }
            }

            Debug.LogWarning($"[VfxPrefabBakerCore] 无法加载贴图: '{texRef}'");
            return null;
        }

        /// <summary>
        /// 取基底材质（用于克隆）：null / "default-particle" → 按当前管线取正确 shader 的临时基底；
        /// 其它视为 Assets 相对路径，加载失败回退管线基底。显式 shaderRef 优先。
        /// </summary>
        static Material LoadMaterialForClone(string materialRef, string shaderRef = null)
        {
            // 用户显式给了材质路径：直接用其 shader 作基底
            if (!string.IsNullOrWhiteSpace(materialRef) &&
                !string.Equals(materialRef, "default-particle", StringComparison.OrdinalIgnoreCase))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialRef.Replace("\\", "/"));
                if (mat != null) return mat;
                Debug.LogWarning($"[VfxPrefabBakerCore] 无法加载材质 '{materialRef}'，回退管线默认基底。");
            }

            // 管线默认基底：URP→Particles/Unlit，Built-in→Default-Particle
            Shader shader = ResolveParticleShader(shaderRef);
            if (shader == null)
                return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            return new Material(shader);
        }

        /// <summary>
        /// 直接赋值用材质："default-particle" → 内置；其它视为 Assets 相对路径，失败回退内置。
        /// </summary>
        static Material LoadMaterialForAssign(string materialRef)
        {
            if (string.Equals(materialRef, "default-particle", StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialRef.Replace("\\", "/"));
            if (mat == null)
            {
                Debug.LogWarning($"[VfxPrefabBakerCore] 无法加载材质 '{materialRef}'，回退到 Default-Particle。");
                return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            }
            return mat;
        }

        /// <summary>
        /// 以 baseMat 为基底克隆并写入贴图，保存为 .mat 资产（按贴图+shader+blend 缓存，避免重复创建）。
        /// 已存在的同名材质会复用并刷新贴图/混合模式，支持贴图重导入后的热更新。
        /// 缓存 key 含 blendMode，使同贴图可分别生成 additive（发光）与 alpha（烟雾）两种材质。
        /// </summary>
        static Material GetOrCreateParticleMaterial(Texture2D texture, Material baseMat, ParticleBlend blend, float hdrIntensity = 0f)
        {
            if (baseMat == null || baseMat.shader == null)
                baseMat = LoadMaterialForClone(null);

            string blendSuffix = blend == ParticleBlend.Additive ? "Add" : "Alpha";

            string texPath = AssetDatabase.GetAssetPath(texture);
            string cacheKey = (string.IsNullOrEmpty(texPath) ? texture.name : texPath)
                              + "|" + baseMat.shader.name + "|" + blendSuffix;

            if (_texMaterialCache.TryGetValue(cacheKey, out Material cached) && cached != null)
                return cached;

            EnsureAssetFolder(DefaultMaterialDir);
            string safeName = SanitizeFileName(texture.name);
            string matPath = $"{DefaultMaterialDir}/VfxTex_{safeName}_{blendSuffix}.mat";

            // 复用已存在材质资产并刷新（贴图/shader/混合模式变更后保持同步）
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Material mat;
            if (existing != null)
            {
                mat = existing;
                if (mat.shader != baseMat.shader)
                    mat.shader = baseMat.shader;
                mat.mainTexture = texture;
            }
            else
            {
                mat = new Material(baseMat) { name = $"VfxTex_{safeName}_{blendSuffix}" };
                mat.mainTexture = texture;
                AssetDatabase.CreateAsset(mat, matPath);
            }

            ApplyUrpBlendMode(mat, blend);
            if (hdrIntensity > 0f)
            {
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", UnityEngine.Color.white * hdrIntensity);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", UnityEngine.Color.white * hdrIntensity);
            }

            EditorUtility.SetDirty(mat);
            _texMaterialCache[cacheKey] = mat;
            return mat;
        }

        /// <summary>清理贴图材质缓存（编辑器域重载后调用）。</summary>
        public static void ClearMaterialCache() => _texMaterialCache.Clear();

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static void EnsureAssetFolder(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return;

            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ──────────────────── 值解析 ────────────────────

        /// <summary>
        /// 解析数值字段：number → 常数；[min, max] → 两常数随机。
        /// multiplier 用于单位换算（如角度 → 弧度）。
        /// </summary>
        static ParticleSystem.MinMaxCurve ParseMinMaxCurve(JToken token, float defaultValue, float multiplier = 1f)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new ParticleSystem.MinMaxCurve(defaultValue * multiplier);

            if (token is JArray arr && arr.Count >= 2)
                return new ParticleSystem.MinMaxCurve(ToFloat(arr[0]) * multiplier, ToFloat(arr[1]) * multiplier);

            return new ParticleSystem.MinMaxCurve(ToFloat(token) * multiplier);
        }

        /// <summary>解析曲线字段：[[time, value], ...] → 线性切线 AnimationCurve。</summary>
        static AnimationCurve ParseAnimationCurve(JArray points)
        {
            var keys = new List<Keyframe>();
            foreach (var pointToken in points)
            {
                if (!(pointToken is JArray p) || p.Count < 2) continue;
                keys.Add(new Keyframe(ToFloat(p[0]), ToFloat(p[1])));
            }

            if (keys.Count == 0)
                return AnimationCurve.Linear(0f, 1f, 1f, 1f);

            // 线性切线：取相邻关键帧斜率
            for (int i = 0; i < keys.Count; i++)
            {
                float tangent;
                if (keys.Count == 1)
                {
                    tangent = 0f;
                }
                else if (i < keys.Count - 1)
                {
                    float dt = keys[i + 1].time - keys[i].time;
                    tangent = Mathf.Approximately(dt, 0f) ? 0f : (keys[i + 1].value - keys[i].value) / dt;
                }
                else
                {
                    float dt = keys[i].time - keys[i - 1].time;
                    tangent = Mathf.Approximately(dt, 0f) ? 0f : (keys[i].value - keys[i - 1].value) / dt;
                }

                var k = keys[i];
                k.inTangent = tangent;
                k.outTangent = tangent;
                keys[i] = k;
            }

            return new AnimationCurve(keys.ToArray());
        }

        /// <summary>解析渐变字段：[{t, color}, ...] → Gradient（rgb 与 alpha 拆分为两组 key）。</summary>
        static Gradient ParseGradient(JArray stops)
        {
            var colorKeys = new List<GradientColorKey>();
            var alphaKeys = new List<GradientAlphaKey>();

            foreach (var stopToken in stops)
            {
                if (!(stopToken is JObject stop)) continue;
                float t = stop["t"] != null ? ToFloat(stop["t"]) : 0f;
                Color c = ParseColor(stop["color"]?.ToString(), Color.white);
                colorKeys.Add(new GradientColorKey(c, t));
                alphaKeys.Add(new GradientAlphaKey(c.a, t));
            }

            var gradient = new Gradient();
            if (colorKeys.Count == 0)
            {
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            }
            else
            {
                gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
            }
            return gradient;
        }

        /// <summary>解析颜色：#rgb / #rgba / #rrggbb / #rrggbbaa 十六进制。</summary>
        static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            hex = hex.Trim().TrimStart('#');

            try
            {
                switch (hex.Length)
                {
                    case 3: // #rgb → #rrggbb
                        hex = string.Concat(
                            hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                        goto case 6;
                    case 4: // #rgba → #rrggbbaa
                        hex = string.Concat(
                            hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3]);
                        goto case 8;
                    case 6: // #rrggbb
                    {
                        float r = ParseHexByte(hex, 0) / 255f;
                        float g = ParseHexByte(hex, 2) / 255f;
                        float b = ParseHexByte(hex, 4) / 255f;
                        return new Color(r, g, b, 1f);
                    }
                    case 8: // #rrggbbaa
                    {
                        float r = ParseHexByte(hex, 0) / 255f;
                        float g = ParseHexByte(hex, 2) / 255f;
                        float b = ParseHexByte(hex, 4) / 255f;
                        float a = ParseHexByte(hex, 6) / 255f;
                        return new Color(r, g, b, a);
                    }
                    default:
                        Debug.LogWarning($"[VfxPrefabBakerCore] 无法识别的颜色格式 '#{hex}'，使用默认色。");
                        return fallback;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VfxPrefabBakerCore] 颜色解析失败 '#{hex}': {e.Message}，使用默认色。");
                return fallback;
            }
        }

        static int ParseHexByte(string hex, int startIndex)
        {
            return int.Parse(hex.Substring(startIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        static ParticleSystemShapeType ParseShapeType(string type)
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "cone": return ParticleSystemShapeType.Cone;
                case "circle": return ParticleSystemShapeType.Circle;
                case "sphere": return ParticleSystemShapeType.Sphere;
                case "hemisphere": return ParticleSystemShapeType.Hemisphere;
                case "box": return ParticleSystemShapeType.Box;
                case "edge": return ParticleSystemShapeType.SingleSidedEdge;
                default:
                    Debug.LogWarning($"[VfxPrefabBakerCore] 未知 shape.type '{type}'，回退为 cone。");
                    return ParticleSystemShapeType.Cone;
            }
        }

        static ParticleSystemSimulationSpace ParseSimulationSpace(string space)
        {
            return string.Equals(space, "world", StringComparison.OrdinalIgnoreCase)
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;
        }

        static ParticleSystemRenderMode ParseRenderMode(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "stretch": return ParticleSystemRenderMode.Stretch;
                case "billboard":
                default:
                    return ParticleSystemRenderMode.Billboard;
            }
        }

        static ParticleSystemAnimationType ParseSheetAnimationType(string type)
        {
            switch ((type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "singlerow": return ParticleSystemAnimationType.SingleRow;
                case "wholesheet":
                default:
                    return ParticleSystemAnimationType.WholeSheet;
            }
        }

        static float ToFloat(JToken token)
        {
            return token.Value<float>();
        }

        // ── 序列化属性写入（腾讯定制版 Unity 移除部分托管属性后的兜底通道）──

        static bool TrySetSerializedInt(SerializedObject so, string path, int value)
        {
            var prop = so.FindProperty(path);
            if (prop == null || prop.propertyType != SerializedPropertyType.Integer)
                return false;
            prop.intValue = value;
            return true;
        }

        static bool TrySetSerializedFloat(SerializedObject so, string path, float value)
        {
            var prop = so.FindProperty(path);
            if (prop == null || prop.propertyType != SerializedPropertyType.Float)
                return false;
            prop.floatValue = value;
            return true;
        }
    }
}
