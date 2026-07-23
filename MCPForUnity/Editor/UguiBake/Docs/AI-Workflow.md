# AI 工作流与 Prompt 模板 — HtmlToUGUI × Unity MCP

> 本文件供 AI 助手（Claude / Cursor / TRAE 等）在通过 Unity MCP 制作 UGUI 界面时参考。
> 调用 `bake_ugui(action="get_dsl")` 可获取完整 DSL 规范与本文件。

## 一、系统能力概览

| 能力 | MCP Action | 说明 |
|------|-----------|------|
| 获取 DSL 规范 | `get_dsl` | 返回 UI-DSL 全控件版规范 + 可用画风列表 |
| **缩进 DSL→JSON 解析** | `parse_dsl` | 轻量格式，比 HTML 少 ~65% token |
| **DSL→预制体一步烘焙** | `bake_from_dsl` | **推荐入口**，解析+烘焙一步到位 |
| HTML→JSON 解析 | `parse_html` | 纯 C# 解析器，无需浏览器 |
| JSON→预制体烘焙 | `bake` | 将 UIDataNode JSON 烘焙为 .prefab |
| HTML→预制体一步烘焙 | `bake_from_html` | 解析+烘焙一步到位 |
| 批量烘焙 | `bake_batch` | 一次烘焙多个界面 |
| 增量更新子树 | `bake_partial` | 只更新预制体的某个子节点 |
| 列出已烘焙预制体 | `list` | 查看 Baked/Prefabs 目录 |
| 删除预制体 | `delete` | 删除 .prefab 及其 JSON 快照 |
| 生成 View 脚本 | `generate_view_script` | 自动生成 C# 绑定脚本 |

## 二、标准工作流（推荐）

### DSL 工作流（推荐，token 最省）

```
用户描述需求
    ↓
① AI 直接编写缩进 DSL（无需获取规范，语法见下文）
    ↓
② AI 调用 bake_from_dsl（DSL→JSON→Prefab 一步到位）
    ↓
③ AI 调用 manage_camera 截图验证
    ↓
④ 需要调整 → 修改 DSL → 重新 bake_from_dsl
    ↓
⑤ 满意后 → generate_view_script 生成绑定脚本
```

### HTML 工作流（兼容，功能更全）

```
用户描述需求
    ↓
① AI 调用 get_dsl 获取规范
    ↓
② AI 按 DSL 规范生成 HTML
    ↓
③ AI 调用 bake_from_html（HTML→JSON→Prefab 一步到位）
    ↓
④ AI 调用 manage_camera 截图验证
    ↓
⑤ 需要调整 → 修改 HTML → 重新 bake_from_html
    ↓
⑥ 满意后 → generate_view_script 生成绑定脚本
```

### DSL 工作流逐步说明

#### Step 1: 编写 DSL
直接按缩进 DSL 语法编写 UI 描述（见下方语法参考），无需获取规范。

#### Step 2: 一步烘焙
```
bake_ugui(
    action="bake_from_dsl",
    dsl_content="div MyPage 942x2048 bg:#1a1f2e flex:col center\n  ...",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/MyPage.prefab",
    reference_width=942,
    reference_height=2048,
    use_tmp=True,
    font_path="Assets/Fonts/NotoSansSC.asset"
)
```

#### Step 3: 截图验证
```
manage_camera(action="screenshot", capture_source="scene_view", view_target="MyPage", include_image=True)
```

#### Step 4: 迭代调整
修改 DSL 后重新调用 `bake_from_dsl`，系统会自动备份旧预制体。

#### Step 5: 生成绑定脚本
```
bake_ugui(
    action="generate_view_script",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/MyPage.prefab",
    namespace="Game.UI"
)
```

### HTML 工作流逐步说明

#### Step 1: 获取规范
```
bake_ugui(action="get_dsl")
```
AI 首次操作时应获取 DSL 规范，确保生成的 HTML 符合要求。

#### Step 2: 生成 HTML
按 DSL 规范生成 UI-DSL HTML。关键规则：
- 唯一根节点：`data-u-type="div" data-u-name="rootName"`
- 根节点 `style` 必须含 `width: {W}px; height: {H}px;`
- 每个需要进 Unity 的节点必须有 `data-u-type` + `data-u-name`
- 组件命名用小驼峰：`btn.startNewGame`、`txt.title`、`img.logo`
- 全屏底图加 `data-u-layout="stretch"`
- 居中内容块加 `data-u-layout="center"`
- 通栏顶栏加 `data-u-layout="stretch-h"`

#### Step 3: 一步烘焙
```
bake_ugui(
    action="bake_from_html",
    html_content="<div data-u-type='div' ...>...</div>",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/MyPage.prefab",
    reference_width=942,
    reference_height=2048,
    use_tmp=True
)
```

#### Step 4: 截图验证
```
manage_camera(action="screenshot", capture_source="scene_view", view_target="MyPage", include_image=True)
```
或直接在 Game 视图中查看。

#### Step 5: 迭代调整
修改 HTML 后重新调用 `bake_from_html`，系统会自动备份旧预制体。

#### Step 6: 生成绑定脚本
```
bake_ugui(
    action="generate_view_script",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/MyPage.prefab",
    namespace="Game.UI"
)
```

## 三、烘焙参数详解

所有烘焙类 action（`bake`、`bake_batch`、`bake_partial`、`bake_from_html`、`bake_from_dsl`）均支持以下参数：

### use_tmp — 文本组件类型

| 值 | 文本组件 | 适用场景 |
|----|---------|---------|
| `true`（默认） | `TextMeshProUGUI` / `TMP_InputField` / `TMP_Dropdown` | 推荐用于正式项目，渲染质量高、支持 SDF |
| `false` | `UnityEngine.UI.Text` / `InputField` / `Dropdown` | 无 TMP 包或需要轻量兼容时使用 |

```
bake_ugui(action="bake_from_dsl", dsl_content="...", prefab_path="...", use_tmp=False)
```

### font_path — 默认字体

指定烘焙时所有文本组件使用的字体资源路径（Assets 相对路径）。

| use_tmp | font_path 格式 | 加载类型 |
|---------|---------------|---------|
| `true` | `Assets/Fonts/MyFont.asset` | `TMP_FontAsset` |
| `false` | `Assets/Fonts/MyFont.ttf` | `Font` |

- 省略时自动回退到 `UguiBakeConfig` 配置资源中的 `defaultTmpFont` / `defaultLegacyFont`
- 若配置也为空，TMP 使用全局默认字体，旧版 Text 使用系统默认字体

```
bake_ugui(action="bake_from_dsl", dsl_content="...", prefab_path="...",
          use_tmp=True, font_path="Assets/Fonts/NotoSansSC.asset")
```

### template_prefab — 页面模板预制体

指定烘焙的父级模板预制体路径（Assets 相对路径），模板根节点须含 `Canvas` 组件。

- 烘焙时加载模板内容，在其 `transform` 下创建 UI 节点，然后另存为目标预制体
- 适用于所有页面共享统一的 Canvas / EventSystem / 背景层结构
- 省略时从零创建 Canvas（ScreenSpaceOverlay + CanvasScaler）

```
bake_ugui(action="bake_from_dsl", dsl_content="...", prefab_path="...",
          template_prefab="Assets/Prefabs/UI/UITemplate.prefab")
```

### 参数优先级

```
MCP 调用参数 > UguiBakeConfig 配置 > 代码内置默认值
```

`UguiBakeConfig` ScriptableObject 字段（通过 `Create > UI Architecture > UGUI Bake Config` 创建）：

| 字段 | 说明 |
|------|------|
| `useTMPText` | 默认文本组件类型 |
| `defaultTmpFont` | TMP 默认字体 |
| `defaultLegacyFont` | 旧版 Text 默认字体 |
| `defaultTemplatePrefab` | 默认页面模板预制体 |

## 四、缩进 DSL 语法参考

> 缩进 DSL 是一种轻量 UI 描述格式，用 2 空格缩进表达层级，比 HTML 减少 ~65% token。
> 底层复用同一套布局引擎，烘焙结果与 HTML 完全一致。

### 基本语法

每行一个节点，格式：`类型 名称 属性... "文本内容"`

```
# 注释行（# 开头）
div MyPage 942x2048 bg:#1a1f2e flex:col center
  img bg stretch bg:#141824
  div @topBar stretch-h bg:#0d1117 h:80 flex:center
    text txt.Title "系统设置" color:#e0e6ed fs:36
  btn btn.Save "保存设置" bg:#3182ce color:#fff fs:22 h:56 r:12
```

### 节点类型

| DSL 类型 | 对应 UGUI 控件 | 说明 |
|---------|---------------|------|
| `div` | RectTransform 容器 | 通用容器节点 |
| `img` / `image` | Image | 图片节点 |
| `text` | Text / TMP_Text | 文本节点 |
| `btn` / `button` | Button | 按钮节点 |
| `input` | InputField / TMP_InputField | 输入框，文本作为 placeholder |
| `toggle` | Toggle | 开关，加 `checked` 标志位 |
| `slider` | Slider | 滑动条 |
| `select` / `dropdown` | Dropdown / TMP_Dropdown | 下拉菜单，文本用逗号分隔选项 |
| `scroll` | ScrollRect | 滚动列表 |

### 布局关键字

| 关键字 | 对应 data-u-layout | 说明 |
|--------|-------------------|------|
| `stretch` | stretch | 全屏拉伸（left:0 top:0 width:100% height:100%） |
| `center` | center | 居中布局 |
| `stretch-h` | stretch-h | 水平通栏拉伸 |
| `stretch-v` | stretch-v | 垂直通栏拉伸 |
| `absolute` | absolute | 绝对定位（需配合 left/top） |

### 属性速查

| 属性 | 对应 CSS | 示例 | 说明 |
|------|---------|------|------|
| `bg` | background-color | `bg:#1a1f2e` | 背景色 |
| `color` | color | `color:#e0e6ed` | 文字颜色 |
| `w` | width | `w:600` / `w:100%` | 宽度（像素或百分比） |
| `h` | height | `h:80` / `h:100%` | 高度 |
| `maxw` / `minw` | max-width / min-width | `maxw:600` | 最大/最小宽度 |
| `maxh` / `minh` | max-height / min-height | `maxh:400` | 最大/最小高度 |
| `fs` | font-size | `fs:36` | 字号（px） |
| `bold` | font-weight: bold | `bold` | 粗体（标志位） |
| `ta` | text-align | `ta:center` | 文字对齐 |
| `flex` | display:flex + 方向 | `flex:col` / `flex:row` | flex 布局 |
| `flex` | + 对齐 | `flex:col:center` | flex + align-items |
| `flex` | + 对齐 + 分布 | `flex:col:center:between` | flex + align + justify |
| `align` | align-items | `align:center` | 交叉轴对齐 |
| `justify` | justify-content | `justify:between` | 主轴分布 |
| `gap` | gap | `gap:24` | flex 间距（px） |
| `r` | border-radius | `r:12` | 圆角（px） |
| `border` | border-bottom | `border:2:#2d4a7a` | 底边框（宽度:颜色） |
| `pad` | padding | `pad:20` / `pad:20,10` | 内边距（逗号分隔） |
| `pos` | position | `pos:absolute` | 定位方式 |
| `left` / `top` | left / top | `left:0` / `top:80` | 定位偏移 |
| `right` / `bottom` | right / bottom | `right:20` | 定位偏移 |
| `z` | z-index | `z:1` | 层级 |
| `img` | background-image: url() | `img: textures/ui/bg` | 背景图路径 |
| `grad` | linear-gradient | `grad:165:#c1,#c2` | 渐变（角度:颜色） |
| `outline` | data-u-outline-* | `outline:2:#000000` | 文字描边（宽度:颜色） |
| `value` | data-u-value | `value:0.8` | slider 默认值 |
| `checked` | data-u-checked | `checked` | toggle 默认勾选（标志位） |
| `dir` | data-u-dir | `dir:ltr` | 文字方向 |
| `export` | data-u-export | `export:true` | 导出标记 |

### flex 属性组合写法

`flex` 支持用冒号组合多个值，顺序：`方向:对齐:分布`

```
flex:col                    → display:flex; flex-direction:column
flex:row                    → display:flex; flex-direction:row
flex:col:center             → + align-items:center
flex:col:center:between     → + justify-content:space-between
flex:row:start:evenly       → align-items:flex-start; justify-content:space-evenly
```

对齐值简写：`start` → `flex-start`，`end` → `flex-end`
分布值简写：`between` → `space-between`，`evenly` → `space-evenly`

### 文本内容

- 用双引号包裹文本：`text txt.Title "系统设置"`
- dropdown 用逗号分隔选项：`select dropdown.Quality "低画质,中画质,高画质"`
- input 的文本作为 placeholder：`input input.Name "请输入用户名"`
- 不含冒号且非关键字的裸文本也会被当作文本内容

### 根节点尺寸

根节点行可包含 `WxH` 格式的尺寸声明（如 `942x2048`），等效于 HTML 的 `width:942px; height:2048px`。

### DSL vs HTML 对照

| 维度 | 缩进 DSL | HTML |
|------|---------|------|
| Token 用量 | ~35%（基准） | ~100%（基准） |
| 层级表达 | 2 空格缩进 | 标签嵌套 `</div>` |
| 属性 | `bg:#1a1f2e` | `style="background-color:#1a1f2e"` |
| 文本 | `"系统设置"` | `>系统设置</div>` |
| 注释 | `# 注释` | `<!-- 注释 -->` |
| 布局关键字 | `stretch` | `data-u-layout="stretch"` |
| 适用场景 | AI 快速生成、迭代 | 复杂样式、精细控制 |

### 警告与错误处理

- `parse_dsl` / `bake_from_dsl` 的返回中可能包含 `warnings` / `dslWarnings` 数组。**AI 必须检查该字段**，常见警告：
  - `未知节点类型 'xxx'，已按 div 处理` → 类型拼写错误，修正后重新烘焙
  - `未知属性 'xxx'（值已忽略）` → 属性名拼写错误
  - `文本未加引号，已将 N 个片段拼接` → 文本应加双引号
  - `缩进为 N 空格，不是单位 M 的整数倍` → 缩进不一致
- **解析错误**会带行号抛出（如 `第 5 行：缩进跳级`），AI 应根据行号定位并修复后重试
- 硬错误（直接失败）：多根节点、缩进跳级、根节点带缩进、DSL 为空

## 五、Prompt 模板

### 模板 A：从零创建新界面

```
请帮我制作一个 [界面名称] 界面。

需求描述：
- [具体功能需求]
- 画风偏好：[修仙风格 / 国风扁平 / 极简扁平 / 水墨风 / 科幻HUD / 长安荔枝 / 二次元卡牌]
- 基准分辨率：[宽] × [高]

请按以下步骤执行：
1. 调用 bake_ugui(action="get_dsl") 获取 DSL 规范
2. 按规范生成 HTML 代码
3. 调用 bake_ugui(action="bake_from_html") 烘焙为预制体
4. 截图验证效果
5. 如需调整，修改 HTML 后重新烘焙
6. 满意后调用 generate_view_script 生成绑定脚本

预制体输出路径：Assets/3rd/HtmlToUGUI/Baked/Prefabs/[PageName].prefab
```

### 模板 B：修改现有界面

```
请帮我修改 [界面名称] 界面中的 [具体区域]。

当前预制体路径：Assets/3rd/HtmlToUGUI/Baked/Prefabs/[PageName].prefab
修改需求：[具体修改内容]

请按以下步骤执行：
1. 调用 bake_ugui(action="list") 确认预制体存在
2. 生成修改后的完整 HTML
3. 调用 bake_ugui(action="bake_from_html") 重新烘焙（会自动备份旧版本）
4. 截图验证

如果只需要更新某个子区域，可以使用增量更新：
bake_ugui(action="bake_partial", prefab_path="...", node_path="[目标节点路径]", json_content="[子树JSON]")
```

### 模板 C：批量制作多个界面

```
请帮我批量制作以下界面：
1. [界面A名称] - [简要描述]
2. [界面B名称] - [简要描述]
3. [界面C名称] - [简要描述]

统一画风：[画风名]
统一分辨率：[宽] × [高]

请按以下步骤执行：
1. 获取 DSL 规范
2. 为每个界面生成 HTML
3. 逐个调用 bake_from_html 烘焙
4. 全部完成后截图验证
5. 为每个界面生成 View 脚本
```

### 模板 D：增量更新子区域

```
请帮我更新 [界面名称] 预制体中的 [子区域名称] 区域。

预制体路径：Assets/3rd/HtmlToUGUI/Baked/Prefabs/[PageName].prefab
目标节点路径：[如 "@menuContentLayer" 或 "content/@topHud"]
更新内容：[具体修改]

步骤：
1. 生成该子区域的 HTML 片段
2. 调用 parse_html 解析为 JSON
3. 调用 bake_partial 更新预制体
4. 截图验证
```

## 六、命名规范

### 节点命名（data-u-name）

| 前缀 | 用途 | 示例 |
|------|------|------|
| `btn.` | 按钮 | `btn.startNewGame` |
| `txt.` | 文本 | `txt.title` |
| `img.` | 图片 | `img.logo` |
| `input.` | 输入框 | `input.username` |
| `dropdown.` | 下拉菜单 | `dropdown.quality` |
| `toggle.` | 开关 | `toggle.fullscreen` |
| `slider.` | 滑动条 | `slider.volume` |
| `scroll.` | 滚动列表 | `scroll.itemList` |
| `@` | 变量前缀容器 | `@header` (子节点变量名带 header_ 前缀) |
| `.` | 跳过绑定 | `.decorator` (纯装饰，不生成绑定) |
| `bg` | 全屏背景 | `bg` (image 类型固定名，不加 img. 前缀) |

### 文件命名

| 类型 | 路径 | 命名规则 |
|------|------|---------|
| 预制体 | `Assets/3rd/HtmlToUGUI/Baked/Prefabs/` | `{PageName}.prefab` |
| JSON 快照 | `Assets/3rd/HtmlToUGUI/Baked/Json/` | `{PageName}.ugui.json` |
| View 脚本 | 同 Prefabs 目录 | `{PageName}View.cs` |
| 源 HTML | `Assets/3rd/HtmlToUGUI/HTML/` | `{PageName}.html` |

## 七、布局引擎说明

C# 解析器实现了简化 CSS 布局引擎，支持以下模式：

### 已支持
- `position: absolute` + `left/top/width/height`（精确像素）
- `display: flex` + `flex-direction: column/row`
- `gap`、`align-items: center/flex-start/flex-end/stretch`
- `justify-content: center/flex-start/flex-end/space-between/space-evenly`
- 百分比尺寸 `width: 100%` / `height: 100%`
- `box-sizing: border-box` + `padding`
- `inset` 简写
- `min-width/max-width/min-height/max-height`

### 已知限制
- 不支持 CSS Grid（请用 flex 替代）
- 不支持 `flex-grow/shrink` 精确分配（简化为等分或显式尺寸）
- 不支持 `::before/::after` 伪元素
- 不支持外部 CSS 文件（仅解析 inline style）
- 文字尺寸不参与流式布局计算（与浏览器一致，DSL 要求显式尺寸）

### 最佳实践
1. **根节点必须声明显式尺寸**：`width: 942px; height: 2048px;`
2. **flex 容器的子节点尽量显式声明 width/height**
3. **绝对定位节点用 `position: absolute; left: Xpx; top: Ypx;`**
4. **全屏覆盖用 `position: absolute; left: 0; top: 0; width: 100%; height: 100%;` + `data-u-layout="stretch"`**
5. **居中内容用 `data-u-layout="center"` + flex 父级**

## 八、协作规范

### 版本管理
- 每次烘焙自动保存 JSON 快照到 `Baked/Json/`
- 旧预制体自动备份到 `Baked/Prefabs/.backup/`
- 建议将 HTML 源文件也提交到版本控制

### AI 与人工协作
- AI 负责：生成 DSL/HTML、调用烘焙、截图验证、生成脚本骨架
- 人工负责：微调视觉效果、编写业务逻辑、最终审美确认
- 命名约定：AI 生成的 DSL/HTML 放 `HTML/` 目录，预制体放 `Baked/Prefabs/`

### 性能注意
- 单次烘焙建议不超过 200 个节点
- 批量烘焙使用 `bake_batch` 而非多次 `bake` 调用
- 增量更新使用 `bake_partial` 避免全量重建
- 烘焙后 `AssetDatabase.Refresh()` 会自动触发

## 九、常见问题

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| 预制体位置偏移 | flex 布局计算与浏览器有差异 | 改用 `position: absolute` + 显式坐标 |
| 组件未生成 | 缺少 `data-u-type` 或 `data-u-name` | 检查每个需要进 Unity 的节点 |
| 颜色不正确 | CSS 颜色格式不被解析 | 使用 `#RRGGBB` 或 `#RRGGBBAA` 格式 |
| 渐变丢失 | `background-image` 中 gradient 格式不标准 | 确保用 `linear-gradient(angle, color, color)` |
| 图片未加载 | 图片路径无法解析 | 提供 `source_html` 参数 |
| 编译报错 | HtmlToUGUI 包未正确安装 | 确认 `Assets/3rd/HtmlToUGUI/` 存在 |
| bake_ugui 工具不可见 | MCP 工具组未启用 | 在 MCP for Unity 窗口启用 core 组 |

## 十、完整示例

### 示例 A：DSL 格式（推荐）

```
# 设置界面 - 缩进 DSL
div SettingsPage 942x2048 bg:#1a1f2e flex:col center
  img bg stretch bg:#141824
  div @topBar stretch-h bg:#0d1117 h:80 flex:center border:2:#2d4a7a
    text txt.Title "系统设置" color:#e0e6ed fs:36 bold
  div @contentLayer center flex:col gap:24 w:600 pad:120,0,0,0
    div @volumeRow flex:col gap:8
      text txt.VolumeLabel "音量" color:#a0aec0 fs:20
      slider slider.Volume value:0.8 bg:#2d3748 h:36 r:18
    toggle toggle.Fullscreen checked "全屏模式" color:#e0e6ed fs:20 h:48
    div @qualityRow flex:col gap:8
      text txt.QualityLabel "画质" color:#a0aec0 fs:20
      select dropdown.Quality "低画质,中画质,高画质" bg:#2d3748 h:48 color:#e0e6ed fs:18 r:8
    btn btn.Save "保存设置" bg:#3182ce color:#ffffff fs:22 bold h:56 r:12
```

调用烘焙：
```
bake_ugui(
    action="bake_from_dsl",
    dsl_content="[上面的 DSL]",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/SettingsPage.prefab",
    reference_width=942,
    reference_height=2048,
    use_tmp=True,
    font_path="Assets/Fonts/NotoSansSC.asset",
    template_prefab="Assets/Prefabs/UI/UITemplate.prefab"
)
```

### 示例 B：HTML 格式（兼容）

```html
<div data-u-type="div" data-u-name="SettingsPage"
    style="width: 942px; height: 2048px; position: relative; background-color: #1a1f2e; display: flex; flex-direction: column; align-items: center; font-family: sans-serif; box-sizing: border-box; overflow: hidden;">

    <!-- 全屏背景 -->
    <div data-u-type="image" data-u-name="bg" data-u-layout="stretch"
        style="position: absolute; left: 0; top: 0; width: 100%; height: 100%; background-color: #141824;">
    </div>

    <!-- 顶栏 -->
    <div data-u-type="div" data-u-name="@topBar" data-u-layout="stretch-h"
        style="position: absolute; left: 0; top: 0; width: 100%; height: 80px; background-color: #0d1117; display: flex; align-items: center; justify-content: center; border-bottom: 2px solid #2d4a7a;">
        <div data-u-type="text" data-u-name="txt.Title"
            style="color: #e0e6ed; font-size: 36px; text-align: center; font-weight: 700;">系统设置</div>
    </div>

    <!-- 内容区 -->
    <div data-u-type="div" data-u-name="@contentLayer" data-u-layout="center"
        style="position: relative; z-index: 1; display: flex; flex-direction: column; gap: 24px; width: 80%; max-width: 600px; padding-top: 120px; box-sizing: border-box;">

        <!-- 音量滑条 -->
        <div data-u-type="div" data-u-name="@volumeRow"
            style="display: flex; flex-direction: column; gap: 8px;">
            <div data-u-type="text" data-u-name="txt.VolumeLabel"
                style="color: #a0aec0; font-size: 20px;">音量</div>
            <div data-u-type="slider" data-u-name="slider.Volume" data-u-value="0.8"
                style="width: 100%; height: 36px; background-color: #2d3748; border-radius: 18px;"></div>
        </div>

        <!-- 全屏开关 -->
        <div data-u-type="toggle" data-u-name="toggle.Fullscreen" data-u-checked="true"
            style="width: 100%; height: 48px; color: #e0e6ed; font-size: 20px; display: flex; align-items: center;">
            全屏模式
        </div>

        <!-- 画质下拉 -->
        <div data-u-type="div" data-u-name="@qualityRow"
            style="display: flex; flex-direction: column; gap: 8px;">
            <div data-u-type="text" data-u-name="txt.QualityLabel"
                style="color: #a0aec0; font-size: 20px;">画质</div>
            <select data-u-type="dropdown" data-u-name="dropdown.Quality"
                style="width: 100%; height: 48px; font-size: 18px; background-color: #2d3748; color: #e0e6ed; border: 1px solid #4a5568; border-radius: 8px;">
                <option>低画质</option>
                <option>中画质</option>
                <option>高画质</option>
            </select>
        </div>

        <!-- 保存按钮 -->
        <button data-u-type="button" data-u-name="btn.Save"
            style="width: 100%; height: 56px; background-color: #3182ce; color: #ffffff; font-size: 22px; font-weight: 700; border: none; border-radius: 12px; margin-top: 20px;">
            保存设置
        </button>
    </div>
</div>
```

调用烘焙：
```
bake_ugui(
    action="bake_from_html",
    html_content="[上面的 HTML]",
    prefab_path="Assets/3rd/HtmlToUGUI/Baked/Prefabs/SettingsPage.prefab",
    reference_width=942,
    reference_height=2048,
    use_tmp=True,
    font_path="Assets/Fonts/NotoSansSC.asset"
)
```
