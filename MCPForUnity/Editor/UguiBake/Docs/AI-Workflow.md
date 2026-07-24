# AI 工作流与 Prompt 模板 — HtmlToUGUI × Unity MCP

> 本文件供 AI 助手（Claude / Cursor / TRAE 等）在通过 Unity MCP 制作 UGUI 界面时参考。
> 调用 `bake_ugui(action="get_spec")` 可获取本规范全文。

## 核心理念

**工具的唯一输入格式是 HTML。** AI 助手负责将用户的任意输入（自然语言、截图、已有 HTML 等）转换为标准 UI-DSL HTML，然后调用 `bake_from_html` 烘焙为预制体。烘焙结果中会返回 `htmlContent` 字段，供调用方确认生成的 HTML。

```
用户任意输入（自然语言 / 截图 / HTML / …）
    ↓  AI 转换
标准 UI-DSL HTML
    ↓  bake_ugui(action="bake_from_html")
UGUI 预制体 + htmlContent（回传 HTML 供确认）
```

## 一、系统能力概览

| 能力 | MCP Action | 说明 |
|------|-----------|------|
| 获取 HTML 规范 | `get_spec` | 返回本文件全文 + 可用画风列表 |
| **HTML→预制体一步烘焙** | `bake_from_html` | **唯一烘焙入口**，解析+烘焙一步到位，返回 htmlContent |
| 列出已烘焙预制体 | `list` | 查看 Baked/Prefabs 目录 |
| 删除预制体 | `delete` | 删除 .prefab 及其 JSON 快照 |
| 生成 View 脚本 | `generate_view_script` | 自动生成 C# 绑定脚本 |

## 二、标准工作流

```
用户描述需求（自然语言 / 截图 / 已有 HTML）
    ↓
① AI 调用 get_spec 获取规范（首次或需要参考时）
    ↓
② AI 按规范生成 UI-DSL HTML
    ↓
③ AI 调用 bake_from_html（HTML→Prefab 一步到位）
   返回结果含 htmlContent 字段，供 AI 确认生成的 HTML
    ↓
④ AI 调用 manage_camera 截图验证
    ↓
⑤ 需要调整 → 修改 HTML → 重新 bake_from_html
    ↓
⑥ 满意后 → generate_view_script 生成绑定脚本
```

### 逐步说明

#### Step 1: 获取规范（可选）
```
bake_ugui(action="get_spec")
```
AI 首次操作或需要参考语法时应获取规范，确保生成的 HTML 符合要求。

#### Step 2: 生成 HTML
按规范生成 UI-DSL HTML。关键规则：
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
返回结果包含 `htmlContent` 字段（即传入的 HTML），供 AI 确认。

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

`bake_from_html` 支持以下参数：

### use_tmp — 文本组件类型

| 值 | 文本组件 | 适用场景 |
|----|---------|---------|
| `true`（默认） | `TextMeshProUGUI` / `TMP_InputField` / `TMP_Dropdown` | 推荐用于正式项目，渲染质量高、支持 SDF |
| `false` | `UnityEngine.UI.Text` / `InputField` / `Dropdown` | 无 TMP 包或需要轻量兼容时使用 |

```
bake_ugui(action="bake_from_html", html_content="...", prefab_path="...", use_tmp=False)
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
bake_ugui(action="bake_from_html", html_content="...", prefab_path="...",
          use_tmp=True, font_path="Assets/Fonts/NotoSansSC.asset")
```

### template_prefab — 页面模板预制体

指定烘焙的父级模板预制体路径（Assets 相对路径），模板根节点须含 `Canvas` 组件。

- 烘焙时加载模板内容，在其 `transform` 下创建 UI 节点，然后另存为目标预制体
- 适用于所有页面共享统一的 Canvas / EventSystem / 背景层结构
- 省略时从零创建 Canvas（ScreenSpaceOverlay + CanvasScaler）

```
bake_ugui(action="bake_from_html", html_content="...", prefab_path="...",
          template_prefab="Assets/Prefabs/UI/UITemplate.prefab")
```

### source_html — 源 HTML 路径

当 HTML 中引用了相对图片路径时，提供源 HTML 的 Assets 路径用于解析图片。

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

## 四、HTML 语法参考

> UI-DSL HTML 是标准 HTML 的子集，通过 `data-u-*` 属性标注 Unity 控件类型，用 inline `style` 描述布局与样式。
> 纯 C# 解析器实现，无需浏览器。

### 节点类型（data-u-type）

| data-u-type | 对应 UGUI 控件 | 说明 |
|-------------|---------------|------|
| `div` | RectTransform 容器 | 通用容器节点 |
| `image` / `img` | Image | 图片节点 |
| `text` | Text / TMP_Text | 文本节点 |
| `button` / `btn` | Button | 按钮节点 |
| `input` | InputField / TMP_InputField | 输入框，文本作为 placeholder |
| `toggle` | Toggle | 开关，加 `data-u-checked="true"` |
| `slider` | Slider | 滑动条，加 `data-u-value="0.8"` |
| `dropdown` / `select` | Dropdown / TMP_Dropdown | 下拉菜单，用 `<option>` 分隔选项 |
| `scroll` | ScrollRect | 滚动列表 |

### 布局关键字（data-u-layout）

| 值 | 说明 |
|----|------|
| `stretch` | 全屏拉伸（left:0 top:0 width:100% height:100%） |
| `center` | 居中布局 |
| `stretch-h` | 水平通栏拉伸 |
| `stretch-v` | 垂直通栏拉伸 |
| `absolute` | 绝对定位（需配合 left/top） |

### CSS 属性速查

| 属性 | 示例 | 说明 |
|------|------|------|
| `width` / `height` | `width:100%` / `height:80px` | 宽高（像素或百分比） |
| `background-color` / `background` | `background-color:#1a1f2e` | 背景色 |
| `color` | `color:#e0e6ed` | 文字颜色 |
| `font-size` | `font-size:36px` | 字号 |
| `font-weight` | `font-weight:bold` / `font-weight:700` | 粗体 |
| `text-align` | `text-align:center` | 文字对齐 |
| `display` | `display:flex` | flex 布局 |
| `flex-direction` | `flex-direction:column` / `row` | flex 方向 |
| `align-items` | `align-items:center` | 交叉轴对齐 |
| `justify-content` | `justify-content:space-between` | 主轴分布 |
| `gap` | `gap:24px` | flex 间距 |
| `border-radius` | `border-radius:12px` | 圆角 |
| `border-bottom` | `border-bottom:2px solid #2d4a7a` | 底边框 |
| `padding` | `padding:20px` / `padding:20px 10px` | 内边距 |
| `position` | `position:absolute` | 定位方式 |
| `left` / `top` / `right` / `bottom` | `left:0` / `top:80px` | 定位偏移 |
| `z-index` | `z-index:1` | 层级 |
| `max-width` / `min-width` | `max-width:600px` | 最大/最小宽度 |
| `max-height` / `min-height` | `max-height:400px` | 最大/最小高度 |
| `box-sizing` | `box-sizing:border-box` | 盒模型 |
| `overflow` | `overflow:hidden` | 溢出处理 |

### 特殊属性（data-u-*）

| 属性 | 说明 |
|------|------|
| `data-u-type` | 控件类型（必填） |
| `data-u-name` | 节点名称（必填） |
| `data-u-layout` | 布局关键字 |
| `data-u-value` | slider 默认值 |
| `data-u-checked` | toggle 默认勾选 |
| `data-u-dir` | 文字方向（ltr/rtl） |
| `data-u-outline-width` / `data-u-outline-color` | 文字描边 |
| `data-u-export` | 导出标记 |

### flex 布局写法

```css
/* 纵向排列，居中对齐 */
display:flex; flex-direction:column; align-items:center;

/* 纵向排列，居中，两端分布 */
display:flex; flex-direction:column; align-items:center; justify-content:space-between;

/* 横向排列，起点对齐，均匀分布 */
display:flex; flex-direction:row; align-items:flex-start; justify-content:space-evenly;
```

## 五、Prompt 模板

### 模板 A：从零创建新界面

```
请帮我制作一个 [界面名称] 界面。

需求描述：
- [具体功能需求]
- 画风偏好：[修仙风格 / 国风扁平 / 极简扁平 / 水墨风 / 科幻HUD / 长安荔枝 / 二次元卡牌]
- 基准分辨率：[宽] × [高]

请按以下步骤执行：
1. 调用 bake_ugui(action="get_spec") 获取 HTML 规范
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
1. 获取 HTML 规范
2. 为每个界面生成 HTML
3. 逐个调用 bake_from_html 烘焙
4. 全部完成后截图验证
5. 为每个界面生成 View 脚本
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
- 文字尺寸不参与流式布局计算（与浏览器一致，要求显式尺寸）

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
- AI 负责：生成 HTML、调用烘焙、截图验证、生成脚本骨架
- 人工负责：微调视觉效果、编写业务逻辑、最终审美确认
- 命名约定：AI 生成的 HTML 放 `HTML/` 目录，预制体放 `Baked/Prefabs/`

### 性能注意
- 单次烘焙建议不超过 200 个节点
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

### 设置界面 HTML

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
