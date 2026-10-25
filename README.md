# MdExplorer

基于 **Avalonia 12 + .NET 10** 的跨平台（Windows / Linux / Android）**本地**文件资源管理器，采用 **Material Design 3**（`Md3.Avalonia`）设计语言，按窗口尺寸类做真响应式（Adaptive）布局，桌面端 Native AOT 发布，核心层平台无关、平台能力经接口适配。

> 当前仓库处于 **设计阶段（M0 之前）**：尚未建解决方案骨架；开发以 `docs/` 三件套为规范入口。无网络存储计划，云盘/WebDAV/SMB 等皆不在范围内。

## 文档

| 文档 | 内容 |
|---|---|
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 开发文档（主文档）：选型与许可证/AOT 考据、分层架构、平台抽象接口、文件 CRUD + 预览 + 搜索功能规格、响应式总纲、平台适配（Windows/Linux/Android SAF）、AOT 合规、性能、测试、CI、里程碑与待决策 |
| [docs/RESPONSIVE-LAYOUT.md](docs/RESPONSIVE-LAYOUT.md) | 响应式布局设计：五档断点的线框规格、度量、状态变体与断点迁移规则 |
| [docs/IMAGE-PROMPTS.md](docs/IMAGE-PROMPTS.md) | 布局效果图提示词：GPT-image-2 中英双语、逐断点 + 状态变体、固定样例数据与验收单 |
| [docs/assets/layouts/](docs/assets/layouts/) | 5 张 SVG 规范线框图（生成脚本 `scripts/generate-layout-wireframes.py`） |
| [docs/assets/mockups/](docs/assets/mockups/) | 2 张高保真方向图（Compact 手机、Large 桌面） |

## 技术基线速记

- .NET `10.0` · Avalonia `12.1.x` · `Md3.Avalonia 0.5.0-preview.2` · CommunityToolkit.Mvvm(源生成） · SQLite（Microsoft.Data.Sqlite + FTS5)
- 响应式断点（与 Material 3 一致）：Compact `<600` / Medium `600–839` / Expanded `840–1199` / Large `1200–1599` / ExtraLarge `≥1600 dp`
- AOT：桌面 Native AOT（零 IL 警告门）；Android Trimming + RunAOTCompilation；共享代码以 Native AOT 最严口径编写

许可证待定（开发文档 §17 D-01，推荐 Apache-2.0 以与 `Md3.Avalonia` 对齐）。
