# VfxBake 可制作特效清单

> 基于当前 VFX-DSL 能力（见 [AI-Workflow.md](AI-Workflow.md)）整理的可烘焙特效目录。
> 每个特效给出推荐的多子系统组合思路，生成 JSON 时可直接参考。
> 状态标记：⬜ 未制作 / ✅ 已烘焙（制作后在清单中勾选并标注预制体路径）。

## 一、爆发类（bursts + 多子系统叠加）

| 状态 | 特效 | 组合思路 | 预制体路径 |
|---|---|---|---|
| ⬜ | 爆炸（火/冰/雷/毒等属性） | 冲击波 circle burst + 核心闪光（大 startSize、1 粒子）+ 火花 cone + 烟雾慢速上升 | |
| ⬜ | 命中/受击反馈 | 小 burst 四散 + 中心闪光 + 碎屑（gravityModifier 下落） | |
| ⬜ | 技能施放/出手光效 | startDelay 分段：前摇聚光 → 出手闪光 burst | |
| ⬜ | 升级/完成庆祝 | 星星/彩带上抛（startSpeed 向上 + gravityModifier 回落）+ 闪光 | |
| ⬜ | 拾取/收集吸附 | circle 外圈发射 + velocityOverLifetime 指向中心收缩 + 终点闪光 | |
| ⬜ | 死亡消散 | 碎片四散 burst + 残影 colorOverLifetime alpha 淡出 | |

## 二、持续/循环类（looping: true）

| 状态 | 特效 | 组合思路 | 预制体路径 |
|---|---|---|---|
| ⬜ | 光环/脚底光圈 | circle shape 平面发射 + 低透明度贴图循环旋转 | |
| ⬜ | 火焰/篝火/火把 | cone 上喷 rateOverTime + 颜色抖动渐变 + 烟雾子系统 | |
| ⬜ | 烟雾/雾气/尘埃氛围 | box/sphere 大范围慢速漂浮 + 低 alpha | |
| ⬜ | 魔法阵/能量柱 | 多层 circle 环 + stretch 光束 + startRotation 旋转 | |
| ⬜ | 瀑布/水流/喷泉 | cone 向下 + gravityModifier + 底部水花子系统 | |
| ⬜ | 雨/雪/落叶/花瓣 | box 顶部大范围发射 + 重力 + startSpeed [min,max] 随机 | |
| ⬜ | 萤火虫/漂浮光点 | sphere 慢速 + 发光贴图 + 少量粒子 | |
| ⬜ | 毒池/灼烧地面 | circle 平面 + 循环气泡上升 + 属性色渐变 | |
| ⬜ | 吸血/回蓝/治疗光环 | 上升粒子 + 属性色（红/蓝/绿）渐变 | |

## 三、飞行/拖尾类

| 状态 | 特效 | 组合思路 | 预制体路径 |
|---|---|---|---|
| ⬜ | 弹道拖尾（火球/箭矢） | renderMode: stretch + rateOverTime 连续发射，挂到弹道物体上 | |
| ⬜ | 剑气/刀光 | stretch + sizeOverLifetime 收缩曲线 + 短 lifetime burst | |
| ⬜ | 闪电链/电弧 | 多段 stretch 子系统 + 随机 startRotation 抖动 | |

## 四、UI/2D 类（renderer.sortingOrder 高层级）

| 状态 | 特效 | 组合思路 | 预制体路径 |
|---|---|---|---|
| ⬜ | 按钮点击反馈 | 小星星迸发 burst + sortingOrder 高层级 | |
| ⬜ | 稀有度闪光/扫光 | 边缘扫过 + 高亮渐变 | |
| ⬜ | 抽卡/开箱爆发 | 多层 burst 叠加 + 金色粒子雨下落 | |

## 五、护盾/Buff 类

| 状态 | 特效 | 组合思路 | 预制体路径 |
|---|---|---|---|
| ⬜ | 护盾罩 | hemisphere 包裹角色 + 循环流动粒子 | |
| ⬜ | Buff/Debuff 环绕 | 小半径 circle 绕体旋转粒子 + 属性色 | |

## 六、当前 DSL 能力边界（暂不支持）

以下 ParticleSystem 模块 DSL 已支持：~~TextureSheetAnimation（序列帧）~~、~~Trails（拖尾）~~、~~Noise（噪声）~~（见 AI-Workflow.md 3.10~3.12，renderer 新增 `trailMaterial` 字段）。

仍未覆盖的模块，需要时先扩展 [VfxPrefabBakerCore.cs](../VfxPrefabBakerCore.cs)：

| 模块 | 用途 | 扩展优先级 |
|---|---|---|
| SubEmitters | 子发射器（爆炸后二次粒子、粒子死亡分裂） | ★★ |
| Lights | 粒子灯光 | ★ |
| Collision | 粒子碰撞 | ★ |

其他限制：
- 无复杂轨迹运动（只有 velocityOverLifetime 直线附加速度，无贝塞尔/环绕路径）
- 不支持 VFX Graph / 自定义 Shader 级特效

## 七、制作流程速查

```
① bake_vfx(action="get_spec")        → 获取 DSL 规范
② bake_vfx(action="list_textures")   → 查看可用贴图
③ 生成 VFX-DSL JSON（多子系统叠加，每个子系统只做一件事）
④ bake_vfx(action="bake_from_json")  → 烘焙预制体
⑤ manage_camera 截图验证 → 调整 JSON → 重烘（增量自动跳过未变更）
```

制作完成一个后，在上表中将状态改为 ✅ 并填入预制体路径。
