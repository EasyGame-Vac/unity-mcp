# AI 工作流与 DSL 规范 — VfxBake × Unity MCP

> 本文件供 AI 助手（Claude / Cursor / TRAE 等）在通过 Unity MCP 制作粒子特效时参考。
> 调用 `bake_vfx(action="get_spec")` 可获取本规范全文。

## 核心理念

**工具的唯一输入格式是 VFX-DSL JSON。** AI 助手负责将用户的任意输入（自然语言、参考描述等）转换为标准 VFX-DSL JSON，然后调用 `bake_from_json` 烘焙为带 ParticleSystem 的预制体。烘焙出的预制体根节点自带 `VfxAutoDestroy` 组件，运行时 Instantiate 即用——非循环系统播完后自动销毁。

```
用户任意输入（自然语言 / 参考描述 / …）
    ↓  AI 转换
标准 VFX-DSL JSON
    ↓  bake_vfx(action="bake_from_json")
粒子特效预制体（ParticleSystem 树 + VfxAutoDestroy）
```

## 一、系统能力概览

| 能力 | MCP Action | 说明 |
|------|-----------|------|
| 获取 DSL 规范 | `get_spec` | 返回本文件全文 |
| **JSON→预制体一步烘焙** | `bake_from_json` | **唯一烘焙入口**，解析+烘焙一步到位 |
| 列出已烘焙预制体 | `list` | 查看 Baked/Prefabs 目录 |
| 删除预制体 | `delete` | 删除 .prefab 及其 JSON 快照（移入回收站） |
| 列出可用特效贴图 | `list_textures` | 递归列出指定目录下的 Texture2D（路径/尺寸），供 `renderer.texture` 选用 |

## 二、标准工作流

```
用户描述特效需求（自然语言 / 参考描述）
    ↓
① AI 调用 get_spec 获取规范（首次或需要参考时）
    ↓
② AI 按规范生成 VFX-DSL JSON
    ↓
③ AI 调用 bake_from_json（JSON→Prefab 一步到位）
    ↓
④ AI 调用 manage_camera 截图验证
    ↓
⑤ 需要调整 → 修改 JSON → 重新 bake_from_json
   （JSON 未变更时自动跳过，增量烘焙）
```

### 调用示例

```
bake_vfx(
    action="bake_from_json",
    json_content="{ \"name\": \"Explosion_Fire\", \"systems\": [...] }",
    prefab_path="Assets/GameTest/Battle2D/Vfx/Explosion_Fire.prefab"
)
```

也支持文件迭代模式：`json_content` 留空、传 `json_path`（Assets 相对或绝对路径），首次生成 JSON 文件后只改文件即可快速重烘。

## 三、JSON DSL 完整规范（v1）

### 3.1 顶层结构

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | 是 | 特效名，同时作为根 GameObject 名 |
| `systems` | array | 是 | 粒子系统数组，至少 1 个元素 |

### 3.2 system 对象

每个 system 对应一个挂有 `ParticleSystem` 的 GameObject。`children` 递归为子节点。

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | 是 | 节点名 |
| `transform` | object | 否 | 局部变换，见 3.3 |
| `main` | object | 否 | MainModule，见 3.4 |
| `emission` | object | 否 | EmissionModule，见 3.5 |
| `shape` | object | 否 | ShapeModule，见 3.6 |
| `colorOverLifetime` | object | 否 | ColorOverLifetimeModule，见 3.7 |
| `sizeOverLifetime` | object | 否 | SizeOverLifetimeModule，见 3.8 |
| `velocityOverLifetime` | object | 否 | VelocityOverLifetimeModule，见 3.9 |
| `textureSheetAnimation` | object | 否 | TextureSheetAnimationModule（序列帧），见 3.10 |
| `trails` | object | 否 | TrailModule（拖尾），见 3.11 |
| `noise` | object | 否 | NoiseModule（噪声扰动），见 3.12 |
| `renderer` | object | 否 | ParticleSystemRenderer，见 3.13 |
| `children` | array | 否 | 子 system 数组，结构同构递归 |

**未出现的字段一律保留 ParticleSystem 默认值**（烘焙时先 `AddComponent<ParticleSystem>()` 再按字段覆盖）。

### 3.3 transform

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `position` | [x, y, z] | [0,0,0] | 局部坐标 |
| `rotation` | [x, y, z] | [0,0,0] | 欧拉角（度） |
| `scale` | [x, y, z] | [1,1,1] | 局部缩放 |

### 3.4 main（MainModule）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `duration` | number | 5 | 系统单次播放时长（秒） |
| `looping` | bool | false | 是否循环。循环系统不会被 VfxAutoDestroy 销毁 |
| `startDelay` | number / [min,max] | 0 | 启动延迟（秒） |
| `startLifetime` | number / [min,max] | 5 | 粒子存活时长（秒） |
| `startSpeed` | number / [min,max] | 5 | 初始速度 |
| `startSize` | number / [min,max] | 1 | 初始大小 |
| `startRotation` | number / [min,max] | 0 | 初始旋转（**角度制**，自动转弧度） |
| `startColor` | hex 颜色 | "#ffffff" | 初始颜色 |
| `gravityModifier` | number / [min,max] | 0 | 重力系数 |
| `simulationSpace` | "local" / "world" | "local" | 模拟空间 |
| `maxParticles` | int | 1000 | 粒子数上限 |
| `playOnAwake` | bool | true | 激活即播放 |

### 3.5 emission（EmissionModule）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `rateOverTime` | number / [min,max] | 10 | 每秒发射速率。爆发型特效设为 0 并配合 bursts |
| `bursts` | array | 无 | 爆发数组：`[{ "time": 0, "count": 24 }]`，time 为秒、count 为数量 |

### 3.6 shape（ShapeModule）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enabled` | bool | true | 模块开关 |
| `type` | string | "cone" | cone / circle / sphere / hemisphere / box / edge |
| `radius` | number | 1 | 发射半径 |
| `angle` | number | 25 | 锥角（度），cone/circle 有效 |
| `arc` | number | 360 | 弧度范围（度），circle/sphere 等有效 |

### 3.7 colorOverLifetime

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enabled` | bool | false | 模块开关 |
| `gradient` | array | 白→白 | 渐变 stop 数组：`[{ "t": 0, "color": "#ffffffff" }, ...]`，t ∈ [0,1]，alpha 由颜色第 4 分量表达 |

### 3.8 sizeOverLifetime

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enabled` | bool | false | 模块开关 |
| `curve` | array | 线性 0→1 | 关键点数组：`[[time, value], ...]`，time ∈ [0,1]（归一化生命周期），value 为大小倍率；自动生成线性切线 |

### 3.9 velocityOverLifetime

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `enabled` | bool | false | 模块开关 |
| `x` / `y` / `z` | number / [min,max] | 0 | 生命周期内附加速度（沿各轴） |

### 3.10 renderer（ParticleSystemRenderer）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `renderMode` | string | "billboard" | billboard / stretch |
| `sortingOrder` | int | 0 | 排序层级（2D 常用，如 100） |
| `texture` | string | 无 | **特效贴图**，Assets 相对路径（带或不带扩展名均可）。指定后以基底材质克隆并写入 `_MainTex`，生成可持久化 `.mat` 资产；同贴图自动复用同一材质。优先级高于 `material` |
| `material` | string | Unity 默认 | `"default-particle"` 使用内置 Default-Particle.mat；其它值视为 Assets 相对材质路径，加载失败自动回退默认材质。当 `texture` 同时存在时，本字段作为克隆基底；未指定时基底为内置 Default-Particle |

**贴图用法**：先用 `list_textures` 查看可用贴图，再将返回的 `path` 填入 `renderer.texture`。烘焙出的材质保存在 `Assets/MCP/VfxBake/Baked/Materials/VfxTex_{贴图名}.mat`。

```json
"renderer": {
  "renderMode": "billboard",
  "sortingOrder": 100,
  "texture": "Assets/GameEffect/Texture/Glow/tex_glow_round_001_v.png"
}
```

### 3.14 通用值规则

- **数值字段**：`number`（常数）或 `[min, max]` 两元素数组（两常数随机，MinMaxCurve TwoConstants）。
- **颜色**：`#rgb` / `#rgba` / `#rrggbb` / `#rrggbbaa` 十六进制，如 `#ff6b35`、`#ff6b3500`（末两位 alpha=0）。
- **曲线**：`[[time, value], ...]` 点数组，自动转为线性切线 AnimationCurve。
- **角度**：`rotation`、`startRotation`、`angle`、`arc` 均为角度制。

## 四、完整示例：爆炸特效

```json
{
  "name": "Explosion_Fire",
  "systems": [
    {
      "name": "Shockwave",
      "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1] },
      "main": {
        "duration": 0.45, "looping": false, "startDelay": 0,
        "startLifetime": 0.45, "startSpeed": 6, "startSize": [0.3, 0.6],
        "startRotation": 0, "startColor": "#ff6b35", "gravityModifier": 0,
        "simulationSpace": "world", "maxParticles": 64, "playOnAwake": true
      },
      "emission": {
        "rateOverTime": 0,
        "bursts": [ { "time": 0, "count": 24 } ]
      },
      "shape": { "enabled": true, "type": "circle", "radius": 0.1, "angle": 25, "arc": 360 },
      "colorOverLifetime": { "enabled": true, "gradient": [ {"t": 0, "color": "#ffffffff"}, {"t": 1, "color": "#ff6b3500"} ] },
      "sizeOverLifetime": { "enabled": true, "curve": [ [0, 0.2], [0.2, 1], [1, 1.5] ] },
      "velocityOverLifetime": { "enabled": false, "x": 0, "y": 0, "z": 0 },
      "renderer": { "renderMode": "billboard", "sortingOrder": 100, "material": "default-particle" },
      "children": [
        {
          "name": "Core_Flash",
          "main": {
            "duration": 0.2, "looping": false,
            "startLifetime": 0.2, "startSpeed": 0, "startSize": [1.2, 1.6],
            "startColor": "#fff3c4", "simulationSpace": "local", "maxParticles": 1
          },
          "emission": { "rateOverTime": 0, "bursts": [ { "time": 0, "count": 1 } ] },
          "shape": { "enabled": false },
          "colorOverLifetime": { "enabled": true, "gradient": [ {"t": 0, "color": "#ffffffff"}, {"t": 1, "color": "#ffffff00"} ] },
          "sizeOverLifetime": { "enabled": true, "curve": [ [0, 0.5], [0.3, 1.2], [1, 0.1] ] },
          "renderer": { "renderMode": "billboard", "sortingOrder": 101, "material": "default-particle" }
        }
      ]
    }
  ]
}
```

对应烘焙调用：

```
bake_vfx(
    action="bake_from_json",
    json_content="<上面的 JSON>",
    prefab_path="Assets/GameTest/Battle2D/Vfx/Explosion_Fire.prefab"
)
```

## 五、get_spec 的使用方式

AI 首次生成 VFX-DSL JSON 前，或对字段语义不确定时，应先获取规范：

```
bake_vfx(action="get_spec")
```

返回结果的 `spec` 字段即本文件全文。生成 JSON 时严格遵循本规范，不确定的字段直接省略（保留 ParticleSystem 默认值），不要臆造字段。

## 六、注意事项

- **增量烘焙**：JSON 与上次快照（`Assets/MCP/VfxBake/Baked/Json/{name}.vfx.json`）语义一致且预制体已存在时，自动跳过烘焙并返回 `skipped: true`。
- **备份**：重新烘焙前旧预制体自动备份到 `Assets/MCP/VfxBake/Baked/Prefabs/.backup/`。
- **生命周期**：根节点自带 `VfxAutoDestroy`——所有非循环系统播完后自动销毁实例；含循环系统时不销毁，由调用方管理。
- **设计建议**：复杂爆炸/技能特效用多个子系统叠加（冲击波 + 核心闪光 + 火花 + 烟雾），每个子系统只做一件事。
