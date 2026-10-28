# Eidoria（万象屿）· Avalonia 原生桌面首版

基于 [开发文档](docs/DEVELOPMENT.md)、[概念设计 v3](docs/CONCEPT-DESIGN.md)、
[响应式布局](docs/RESPONSIVE-LAYOUT.md)、[插件系统](docs/PLUGIN-SYSTEM.md)、[本地 AI](docs/LOCAL-AI.md)
与 [品牌规范](docs/BRAND.md) 开始构建 **C# 14 + Avalonia 12.1 的原生桌面应用**。

> `MdExplorer` 是工程代号；产品名为 Eidoria（万象屿）。原生应用当前实现核心浏览器 UI 的第一段：
> 本地目录浏览、响应式抽屉/导轨、Windows 式路径导航、网格/列表/详细视图、搜索筛选、浅深主题与 Compact 浮窗预览。
> 原生端目前提供 06 搜索与索引、08 存储洞察、09 压缩包工作台、10 冲突与任务详情的**规划预演入口**（均使用演示数据）；07、11–15 仍未迁移。旧 Web 原型保留作为概念视觉对照，不是产品运行时。

## 运行原生应用

需要安装 .NET 10 SDK：

```bash
dotnet restore MdExplorer.slnx
dotnet run --project src/MdExplorer.Desktop/MdExplorer.Desktop.csproj
```

当前工程使用 Avalonia Fluent 基础控件与项目内 MD3 语义色资源，依赖版本锁定在 Avalonia `12.1.4`。
本原生首版为 Windows / Linux 桌面 UI 纵切；Android 宿主、SAF、正式任务队列与完整文件操作将在后续分阶段接入。
推送到当前分支后，GitHub Actions 会在 Ubuntu 与 Windows runner 上执行 Release restore/build。

## Web 对照预览

旧版零依赖概念原型仍可静态运行：

```bash
python3 -m http.server 8000 --bind 0.0.0.0
# 浏览器打开 http://localhost:8000
```

Web 的顶部控制条是设计演示壳，可切换五档画布和 06–15 规划场景；它不是原生应用运行时。

## 原生 Avalonia 首版实现范围

项目入口位于 `MdExplorer.slnx` / `src/MdExplorer.Desktop/`。当前原生窗口已落地：

- 桌面文件浏览骨架：应用头部、标签视觉框架、侧栏导航、存储卷用量、工具栏、内容区、预览属性侧栏与状态栏。
- 按窗口宽度切换 Compact（导航抽屉 + 04 式预览浮窗）、Medium（窄导轨）、Expanded / Large（永久导航 + 预览侧栏）；不是缩放整张窗口。
- 真实本地文件夹枚举与打开：主页/常用目录、路径分段导航与子文件夹菜单、路径编辑（`Ctrl+L`）、后退/前进/上级、文件名实时筛选、类型过滤、隐藏项、刷新。
- 网格 / 列表 / 详细信息视图（`Ctrl+1/2/3`）、主题切换、创建文件夹、双击文件夹进入、系统默认应用打开文件。
- 主题采用 MD3 青色种子 `#006A6A` 与浅 / 深语义色资源；窗口控件与系统窗框由原生桌面系统提供。
- 新增规划页 06「搜索与索引中心」、08「存储洞察」、09「压缩包工作台」、10「冲突与任务详情」：包含样例搜索/索引状态、空间图、重复项人工复核、虚拟压缩包浏览/解压选项、冲突策略和四态任务队列；所有数据均为虚构演示数据，按钮仅回显状态，不扫描、写入或删除真实文件。

当前有意保留为后续工作的入口或提示：多标签会话状态、系统分享、回收站、设置、复制/移动/删除、可取消任务队列、真实缩略图/文件格式预览、Android SAF，以及 07、11–15 规划场景。06/08/09/10 仅用演示数据，不会读写真实索引、清理或执行文件操作。
用户数据仅通过当前桌面进程读取；创建文件夹会写入当前目录，其余概念按钮不伪装成已完成操作。

**验证记录：**本执行环境没有本地 `dotnet` SDK；GitHub Actions 已在 Ubuntu 与 Windows runner 上成功执行 .NET 10 `restore` + Release `build`，并在 Linux 检查 Web 原型脚本（run `40644974374`）。原生桌面窗口尚未在图形环境中启动做交互/截图验收。

## Web 概念稿对照图（无头 Chromium 实拍）

### 第一版 · 核心浏览器基线（概念图 01–05）

| Large 浅色 | Large 深色 |
|---|---|
| ![Large 浅色](docs/assets/preview/01-large-light.png) | ![Large 深色](docs/assets/preview/02-large-dark.png) |

| Compact 浅色 | Compact 深色 · 悬浮预览 |
|---|---|
| ![Compact 浅色](docs/assets/preview/03-compact-light.png) | ![Compact 深色悬浮预览](docs/assets/preview/04-compact-float-dark.png) |

### 第二版 · 四项修正（对照 02 / 04）

| 窗口控制 + Windows 面包屑 + 磁盘上移 | 面包屑 chevron 下拉 |
|---|---|
| ![窗口/面包屑/存储](docs/assets/preview/v2/01-windows-breadcrumb-storage.jpg) | ![面包屑下拉](docs/assets/preview/v2/02-crumb-dropdown.jpg) |

| Compact 04 风格浮窗（深色） |
|---|
| ![Compact 浮窗](docs/assets/preview/v2/03-compact-float-04-dark.jpg) |

### 第二版 · 规划场景 06–15（概念预演，非已落地功能）

| 06 搜索与索引中心 | 08 存储洞察 | 15 Android 授权 / 回收站 |
|---|---|---|
| ![06](docs/assets/preview/v2/04-search-index-center.jpg) | ![08](docs/assets/preview/v2/05-storage-insights.jpg) | ![15](docs/assets/preview/v2/06-android-access-trash.jpg) |

| 09 压缩包工作台 | 10 冲突 / 任务中心 | 07 XLarge 双窗格 |
|---|---|---|
| ![09](docs/assets/preview/v2/07-archive-workbench.jpg) | ![10](docs/assets/preview/v2/08-conflict-task-center.jpg) | ![07](docs/assets/preview/v2/09-dual-pane-xlarge.jpg) |

| 11 编辑 / PDF | 12 媒体播放 | 13 扩展 / Lua | 14 本地 AI 搜索 |
|---|---|---|---|
| ![11](docs/assets/preview/v2/10-editor-pdf-workbench.jpg) | ![12](docs/assets/preview/v2/11-media-player.jpg) | ![13](docs/assets/preview/v2/12-extensions-lua.jpg) | ![14](docs/assets/preview/v2/13-local-ai-search.jpg) |

## Web 原型 · 第一版核心浏览器基线（概念图 01–05）

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

## Web 原型 · 第二版补齐与修正

### ① Windows 文件资源管理器式面包屑（对照 02）

- **分段点击**：每个路径段都是独立按钮，点击直接切换到对应目录。
- **可点击 chevron**：段间下拉箭头点击后弹出该段的**子文件夹列表**，点击即可跳转；当前段末尾也有 chevron 展开当前目录的子文件夹。
- **空白区变编辑框**：点击面包屑胶囊内的空白区域（或 `Ctrl+L`）进入路径输入。
- **路径解析**：支持 `file://`、`~`、`$HOME`、`%USERPROFILE%`、`内部存储 / 此电脑 / 盘符` 前缀，
  `/` `\` `>` 多种分隔符、目录名前缀匹配，以及**完整文件路径**（自动定位到父文件夹并选中该文件）。
- **回车跳转 / Esc 还原**；无效路径保留输入并提示。

### ② 可交互窗口控制（对照 02）

标题栏右上角是参考 02 图的最小化、最大化 / 恢复、关闭三枚按钮，不再是纯装饰圆点：

- **最小化**：窗口缩到舞台一角成为任务栏卡片，可点击恢复。
- **最大化 / 恢复**：画布铺满可用区，按钮图标在方框与双框之间切换。
- **关闭**：显示「应用已关闭」占位，可重新打开。

### ③ Compact 04 风格预览浮窗（对照 04 / 05）

Compact / Medium 的预览窗改为概念图 04 的大圆角浮窗，深色下**不反色**纸面与图像：

- 顶部栏：左侧关闭 `X`、右侧全屏展开控件（可切换全屏预览 / 还原），整条可拖动。
- 居中大文件名标题 + MD3 Secondary Tabs「预览 / 属性」。
- 大幅图像预览 + 尺寸 / 大小 / 修改时间事实芯片。
- 底部「打开」主按钮（带播放三角）+ 圆形共享按钮。

### ④ 磁盘列表上移（对照 02）

左侧导航的 C: / D: / SD 卡卷列表从底部上移到导航项正下方、
「设置」上方，并加「存储量」分组标题 + 三条使用量进度条，与 02 图一致。

### ⑤ 规划场景 06–15（概念预演）

顶部「场景」下拉可切换 10 个规划场景，每个场景带「概念预演」横幅，
明确**不代表功能已落地**（不是正式导航）。切换场景会自动应用概念图对应的断点 / 主题：

| 场景 | 断点 / 主题 | 表现的概念预演 |
|---|---|---|
| ① 搜索与索引中心 06 | Large / 深色 | 文件名·正文模式、类型过滤芯片、结果网格、索引体积环、暂停/重建/清空索引、隐私边界 |
| ② 存储洞察 08 | Large / 深色 | 68% 已用环、类别条形、重复文件组、空间地图 treemap、大文件 Top、复核而非自动清理 |
| ② Android 存储与回收站 15 | Compact / 深色 | SAF 逐项授权（含失效项重新授权）、1.8GB 配额环、回收站还原与清空 |
| ③ 压缩包工作台 09 | Large / 浅色 | 包内目录树、条目网格、条目预览、解压选项、环形进度 |
| ③ 冲突与任务详情 10 | Large / 浅色 | 同名文件校验和对比、保留两者/替换/跳过策略、应用到剩余 12 项、任务四态 + 队列进度 |
| ④ ExtraLarge 双窗格 07 | ExtraLarge / 浅色 | 左右独立窗格（各自面包屑/工具栏/网格）、活动窗格高亮、跨窗格拖影 `Ctrl=复制` |
| ④ 编辑与 PDF 工作台 11 | Large / 深色 | 代码编辑器（保存进入任务队列）、文档内搜索命中高亮、目录、页码/缩放、TTS 无障碍入口；PDF 纸面在深色下不反色 |
| ⑤ 内置媒体播放 12 | Large / 深色 | 播放队列、字幕、进度条、播放/暂停/音量/字幕、章节缩略图时间线 |
| ⑥ 扩展与 Lua 13 | Large / 深色 | 扩展开关 + 权限矩阵、Lua 编辑器、运行生成操作计划（**等待确认，尚未写入磁盘**）、Android 商店限制提示 |
| ⑥ 本地 AI 语义搜索 14 | Large / 浅色 | 自然语言搜索、相关度徽标、AI 识别标签、SigLIP 模型卡（体积/索引根/可删除）、本地索引不改 EXIF |

所有场景内的按钮、开关、页签、进度条均可交互（点击有 snackbar 反馈或状态变化），
但**不会修改真实文件**，也不代表这些功能已经进入正式产品。

### 快捷键

`Ctrl+F/K` 搜索 · `Ctrl+L` 地址栏 · `Ctrl+A` 全选 · `Ctrl+T/W` 标签 · `Ctrl+1/2/3` 视图 ·
`Ctrl+C/X/V` 剪贴板 · `Ctrl+R` 刷新 · `Alt+←/→/↑` 历史与上级 · `F2` 重命名 · `Delete` 删除 ·
`F1` 说明 · `Esc` 逐层关闭浮层。

## 文件

```
MdExplorer.slnx                         原生 Avalonia 解决方案
src/MdExplorer.Desktop/                 C# 桌面应用（浏览器 + 规划页 06/08/09/10）
Directory.Build.props                    C# 14 / nullable / 可重复构建设置
Directory.Packages.props                 Avalonia NuGet 版本集中锁定
index.html      应用骨架 + Material Symbols 图标 sprite + 演示控制条 + 场景覆盖层
styles.css      MD3 设计系统、组件、五档断点与动效
scenes.css      窗口控件 / Windows 面包屑 / Compact 浮窗 / 规划场景样式
app.js          虚拟文件系统、渲染、交互、任务队列、窗口/面包屑/场景切换、快捷键
scenes.js       规划场景 06–15 的独立渲染与交互模块（通过 EidoriaScenes 接口接入）
assets/         Eidoria 品牌 SVG
docs/           从设计分支同步的规格文档（作为实现依据）+ 实现对照图
```

## 验证

- `node --check app.js` / `node --check scenes.js` 通过。
- jsdom 交互回归：第一版基线 33 步 + 第二版 39 步（面包屑 / 窗口 / 浮窗 / 10 场景切换 / 场景内交互）均 0 错误。
- 无头 Chromium 生成 17 张场景实拍图，0 页面错误、无横向溢出；CJK 字体仅在截图时注入。

## 边界

- **Web 对照原型**使用固定虚构样例，不访问真实文件系统；SVG 风景缩略图不含外部图片请求。
- **Avalonia 原生首版**读取当前用户可访问的本地目录；仅“新建文件夹”会写入磁盘，其余尚未实现的操作明确显示阶段提示。
- 两端均无账号、云服务、网络搜索与遥测；原生 06/08 页面仅为规划预演，07、09–15 尚未迁移。
- Web 顶部深色控制条与画布缩放属于演示壳，不是原生界面。
- 本沙箱无 .NET SDK，因此没有本地启动原生窗口；GitHub Actions 已验证跨平台 Release 编译，但尚未做原生 UI 截图/交互验收。Web 概念原型只作视觉参考，不替代原生端。
