# Eidoria（万象屿）

<img src="docs/assets/brand/eidoria-icon.png" alt="Eidoria 图标：蓝色竖向页片左上角翻起，露出绿色背页与橙黄色折面" width="96" />

基于 **Avalonia 12 + .NET 10** 的跨平台（Windows / Linux / Android）**本地**文件资源管理器，采用 **Material Design 3**（`Md3.Avalonia`）设计语言，按窗口尺寸类做真响应式（Adaptive）布局：底部导航栏（Compact）→ 导轨（Medium）→ 永久抽屉 + 预览侧栏（Expanded+）。核心层平台无关，平台能力经接口适配；许可证 Apache-2.0（见 `LICENSE`）。

> **状态：v0.10**——浏览/搜索/预览（图片、文本、**Markdown 渲染、代码高亮、PDF 分页**、ZIP 条目）/复制移动删除（含回收站语义）/ZIP/收藏/设置已可用；v0.2 新增文件夹外部变更自动刷新、文件夹大小、校验和与批量重命名；**v0.3 新增 Android SAF 授权文件夹浏览**；**v0.4 新增详细信息视图**；**v0.5 新增拖放与系统剪贴板互通**；**v0.6 新增浏览器标签页与 PDF 分页预览，并将产品品牌定为 Eidoria（万象屿）**（名称与图标见 `docs/BRAND.md`；仓库、命名空间、Android ApplicationId 与数据目录仍保留内部代号 MdExplorer 以兼容既有数据）；**v0.7 新增串行任务中心、可编辑地址栏、最近访问与标签页恢复**；**v0.8 按概念稿重塑视觉层级：柔和底板承载白色工作区与预览浮动卡，标签页改为与工作区同面相连的浏览器式页签，路径胶囊、类型色图标井、圆角文件卡与精确 `MdList` 选中态统一收敛，Compact 将标签轨与面包屑折叠为当前目录**；**v0.9 换装极简新 Logo（深湖拱门·金日屿浪），三栏加入自研 Material 分隔条（拖动/键盘调整并持久化宽度，原型将回流 Md3.Avalonia），目录切换/页面与侧栏补齐入场与点击动效，插件脚本完成 Lua 优先评估（MoonSharp spike、Python 走进程外）**；**v0.10 新增按文件夹记忆视图与排序（可关、可清除、最多 200 个）和应用内“减少动态效果”，Material 分隔条补齐双击/Home 复位、16px 热区与 `:reduce-motion` 状态，设置页版本改为动态读取**。标签页各自保存路径、前进/后退栈、过滤词与选集，并可跨启动恢复；PDF 预览采用 MIT 许可的 PDFtoImage/PDFium 按页光栅化（Android 内置预览需 API 31+）。音视频播放为后续里程碑；**插件系统与本地 AI 两条轨道已完成评估、仍排在核心完善之后**。

## 效果（CI 无头渲染实拍，归档于 `docs/assets/screenshots/`，含 Markdown/代码/批量重命名/详细信息视图/拖放提示/标签页/PDF/地址栏/任务中心/精修主页 共 21 张）

<img src="docs/assets/screenshots/17-large-tabs.png" alt="Large 1440：连接式双标签页 + 白色工作区 + 网格卡片 + 预览浮动卡" width="720" />

<img src="docs/assets/screenshots/01-compact-390x844.png" alt="Compact 390：底部导航栏 + 列表 + FAB" width="240" /><img src="docs/assets/screenshots/02-compact-home.png" alt="Compact 主页：存储卡与分类" width="240" /><img src="docs/assets/screenshots/03-medium-768x1024.png" alt="Medium 768：导轨 + 网格" width="300" />

## 构建与运行

前置：.NET 10 SDK（`global.json`）；Android 另需 `dotnet workload install android` + JDK 17；Linux Native AOT 另需 `clang`、`zlib1g-dev`。

```bash
# 桌面调试（Windows/Linux 通用）
dotnet run --project src/MdExplorer.Desktop

# 测试（含文件系统真实 IO 与 Zip Slip 防护用例）
dotnet test MdExplorer.slnx -c Release

# 无头渲染五档断点截图到 tests/MdExplorer.Screenshots/bin/Release/net10.0/screenshots/
dotnet test tests/MdExplorer.Screenshots -c Release

# 桌面自包含发布 / Native AOT 发布
dotnet publish src/MdExplorer.Desktop -c Release -r linux-x64 --self-contained true
dotnet publish src/MdExplorer.Desktop -c Release -r linux-x64 -p:PublishAot=true

# Android APK（独立于 .slnx 构建，避免无 Android 工作负载的环境被卡）
dotnet publish src/MdExplorer.Android -c Release -p:PublishTrimmed=true -p:RunAOTCompilation=true
```

CI（`.github/workflows/ci.yml`）在每次推送时构建、出 UI 截图工件，并产出 `linux-x64` / `win-x64` 桌面包与 Android APK（Actions 页的 Artifacts 可直接下载试用）。

## 结构速览

```text
src/MdExplorer.Domain                领域类型/排序/格式化（零依赖）
src/MdExplorer.Platform.Abstractions 平台端口接口
src/MdExplorer.Application           名称校验/路径分段等应用规则
src/MdExplorer.Infrastructure        文件系统/操作队列/回收站/搜索/ZIP/设置
src/MdExplorer.UI                    共享 Avalonia 界面（五断点自适应壳）
src/MdExplorer.Desktop               桌面宿主      src/MdExplorer.Android  Android 宿主
tests/                               单元测试 + 无头截图冒烟
```

## 文档

| 文档 | 内容 |
|---|---|
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 开发文档：选型与许可证/AOT 考据、分层架构、接口规范、功能规格、平台适配、AOT 合规、路线图与决策记录 |
| [docs/BRAND.md](docs/BRAND.md) | Eidoria（万象屿）命名、图标构造、核心色与品牌资产落点 |
| [docs/RESPONSIVE-LAYOUT.md](docs/RESPONSIVE-LAYOUT.md) | 五档断点布局规格（含 `docs/assets/layouts/` 五张 SVG 线框与高保真方向图） |
| [docs/IMAGE-PROMPTS.md](docs/IMAGE-PROMPTS.md) | GPT-image-2 效果图提示词（中英双语） |
| [docs/PLUGIN-SYSTEM.md](docs/PLUGIN-SYSTEM.md) | 插件系统计划与可行性评估（AOT 约束下的五机制选型、P0–P3 分期） |
| [docs/LOCAL-AI.md](docs/LOCAL-AI.md) | 本地 AI 接入评估（多模态语义搜图、识图、TTS/ASR：模型许可、体积、A1–A4 分期） |

响应式断点（与 Material 3 一致，dp）：Compact `<600` / Medium `600–839` / Expanded `840–1199` / Large `1200–1599` / ExtraLarge `≥1600`。
