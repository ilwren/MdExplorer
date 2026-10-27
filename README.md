# Eidoria（万象屿）· 概念第一版

基于 `arena/c665818d-mdexplorer` 分支的 [开发文档](docs/DEVELOPMENT.md)、[概念设计 v3](docs/CONCEPT-DESIGN.md)、
[响应式布局](docs/RESPONSIVE-LAYOUT.md) 与 [品牌规范](docs/BRAND.md) 实现的**可交互设计第一版**。

> 本仓库的 `MdExplorer` 工程代号保持不变；此第一版是**设计落地原型**，不是最终的 Avalonia / .NET 10 应用。

## 运行

无需构建、无需依赖，直接静态托管即可：

```bash
python3 -m http.server 8000
# 浏览器打开 http://localhost:8000
```

Live preview 已绑定 `0.0.0.0:8000`，打开后即可交互。

## 实现对照图

> 无头浏览器实拍，含演示控制条；可逐张与 `arena/c665818d-mdexplorer` 分支的 `docs/assets/mockups/ai-v3/01–05` 对照。

| Large 浅色 | Large 深色 |
|---|---|
| ![Large 浅色](docs/assets/preview/01-large-light.png) | ![Large 深色](docs/assets/preview/02-large-dark.png) |

| Compact 浅色 | Compact 深色 · 悬浮预览 |
|---|---|
| ![Compact 浅色](docs/assets/preview/03-compact-light.png) | ![Compact 深色悬浮预览](docs/assets/preview/04-compact-float-dark.png) |

## 第一版覆盖了什么

依据概念图 `ai-v3/01–05`（当前基线）实现：

| 概念图 | 落地内容 |
|---|---|
| 01 / 02 Large 浅色 / 深色 | 永久抽屉 + 连接式文件页签 + 面包屑 + 迷你搜索岛 + 网格 + 预览侧栏 |
| 03 / 04 Compact 浅色 / 深色 | 顶部栏 + 折叠页签图标（带数量角标）+ 底部 NavigationBar + 应用内悬浮预览窗 |
| 05 Medium | NavigationRail + 悬浮属性窗 |

### 设计系统

- **MD3 颜色角色**：青色种子 `#006A6A`，浅/深两套语义 Token（`surface / surfaceContainerLow / primary / secondaryContainer / outlineVariant …`）。业务样式只引用 Token，没有散写 HEX。
- **几何**：8dp 间距、卡片 12dp、浮窗 28dp、芯片 8dp、按钮与搜索全胶囊、Material Symbols 线性图标、Title/Body/Label 字阶。
- **品牌**：[Eidoria 折页图标](assets/eidoria-icon.svg)（蓝竖页 + 橙折角 + 绿背页 + 薄荷后页）。
- **文字预算**：每张文件卡只留一个数值元数据；工具栏全部图标化（`title` + `aria-label` 兜底）；删除确认保留一行影响范围与恢复方式。

### 五档断点

顶栏「断点」可切换，画布按线框规范等比缩放：

| 断点 | 画布 | 导航形态 | 预览形态 |
|---|---|---|---|
| Compact | 390 × 844 | 底部 NavigationBar + 模态抽屉 | 悬浮窗 |
| Medium | 768 × 1024 | NavigationRail | 悬浮窗 |
| Expanded | 1100 × 760 | 准永久抽屉 | 侧栏 |
| Large | 1440 × 900 | 永久抽屉 + 标签 | 侧栏 |
| ExtraLarge | 1920 × 1080 | 加宽抽屉 | 加宽侧栏 |

切换断点时保留当前目录、选集、排序与搜索词，符合「一套状态、多套视图」。

### 交互

- 单击选中 → 右侧/浮窗预览；双击文件夹进入；面包屑分段跳转，空白区或 `Ctrl+L` 进入路径编辑。
- **搜索岛**：点击或 `Ctrl+F` / `Ctrl+K` 展开悬浮面板，输入即筛选当前文件夹，回车升级为递归搜索。
- **预览 / 属性** MD3 Secondary Tabs：图片、PDF（纸面在深色下不反色）、代码、文本、压缩包、音视频等各自有降级预览；属性页含校验和事实卡。
- 网格 / 列表 / 详细信息三视图、排序、显示隐藏文件。
- 新建、复制/剪切/粘贴、重命名、删除（先显示影响范围 + 可撤销），全部写入**任务中心**。
- 浅/深主题成对、减少动态效果开关。

### 快捷键

`Ctrl+F/K` 搜索 · `Ctrl+L` 地址栏 · `Ctrl+A` 全选 · `Ctrl+T/W` 标签 · `Ctrl+1/2/3` 视图 ·
`Ctrl+C/X/V` 剪贴板 · `Ctrl+R` 刷新 · `Alt+←/→/↑` 历史与上级 · `F2` 重命名 · `Delete` 删除 ·
`F1` 说明 · `Esc` 逐层关闭浮层。

## 文件

```
index.html      应用骨架 + Material Symbols 图标 sprite + 演示控制条
styles.css      MD3 设计系统、组件、五档断点与动效
app.js          虚拟文件系统、渲染、交互、任务队列、快捷键（零依赖）
assets/         Eidoria 品牌 SVG
docs/           从设计分支同步的规格文档（作为实现依据）
```

## 边界

- 数据为固定虚构样例（与 `docs/IMAGE-PROMPTS.md` 的固定样例一致），**不访问真实文件系统**。
- 缩略图为纯 SVG 生成的风景图，不含外部图片请求；无网络、无账号、无遥测。
- 顶部深色控制条与画布缩放是**演示壳**，不属于应用界面本身。
- 概念图 `ai-v3/06–15`（搜索中心、双窗格、存储洞察、插件、本地 AI 等）为规划预演，未进入本第一版。
- 本沙箱无 .NET SDK，因此这一版不是 Avalonia 工程；视觉与交互按概念图收敛，便于先验收设计再决定后续实现栈。
