# MdExplorer 原生版（C# / Avalonia）

基于 `docs/DEVELOPMENT.md`（DESKTOP.md / ANDROID.md 规范）的 .NET 10 + Avalonia 12 实现。
对应概念图 **Material Design 3 · ai-v3 稿**（主题令牌映射 `src/MdExplorer.UI/Themes/Md3.axaml`，
与 Web 版 `tokens.css` 同一色板与几何）。

> ⚠️ **重要声明：本目录为纯源码交付，尚未经过编译验证。**
> 生成环境没有 .NET SDK（也无法安装），因此 `dotnet build` / `dotnet test` / `dotnet publish`
> 均未运行过。代码严格按 DEVELOPMENT.md 的接口签名逐条落地，但首次本地构建时
> 可能出现少量编译错误需修正（尤其 Avalonia XAML 与第三方包版本适配）。

## 结构（§4.2 分层，CI 引用规则锁死）

```
src/
  MdExplorer.Domain               实体/值对象/Result·FileError（零 PackageReference）
  MdExplorer.Platform.Abstractions 端口：FileSystem / Shell / Preview / Archives
  MdExplorer.Application          用例层：BrowserSession·Destination / FileOperationQueue /
                                  TraversalSearchService / AppSettings / IDialogService 等 UI 端口
  MdExplorer.Infrastructure      System.IO / SQLite 索引 / Skia 缩略图 / ZIP / 应用私有回收站
  MdExplorer.Platform.Windows    Windows 薄适配（能力位/分享）
  MdExplorer.Platform.Linux      XDG 回收站等 Linux 适配
  MdExplorer.UI                  ViewModels + Views（断点：Compact/Medium/Expanded）+ MD3 主题
  MdExplorer.Desktop             桌面宿主：唯一 CompositionRoot（显式注册，禁扫描）
tests/
  MdExplorer.Domain.Tests        FileLocator/Result/模型
  MdExplorer.Application.Tests   BrowserSession/UniqueName/AppSettings/Destination
```

## 本地构建（.NET SDK 10.0.100，`global.json` 已钉）

```bash
cd native
dotnet restore MdExplorer.slnx
dotnet build MdExplorer.slnx -c Release
dotnet test MdExplorer.slnx
```

桌面发布（§11 `PublishAot=true`）：

```bash
dotnet publish src/MdExplorer.Desktop -c Release -r win-x64     # Windows
dotnet publish src/MdExplorer.Desktop -c Release -r linux-x64   # Linux
```

首次构建若 NuGet 报版本问题，以 `Directory.Packages.props` 为唯一版本源（CPM），
新增包必须在该文件登记 `PackageVersion`。

## 实现要点（对齐 DEVELOPMENT.md 规范）

- **P2 AOT 优先**：视图映射为显式 `ViewFactory` Dictionary（禁反射 ViewLocator）；
  JSON 走 STJ 源生成（`SettingsJsonContext`）；`PublishAot=true`。
- **P5 写操作可预期**：一切写盘经 `FileOperationQueue` 全局串行队列（Channel）；
  冲突询问回 UI 线程（`IDialogService.ResolveConflictAsync`），支持「应用到其余全部」；
  KeepBoth → `name (2).ext`；可取消（`OperationHandle.Cancellation`）。
- **P6 本地优先**：搜索 v1 为实时遍历（`TraversalSearchService`）；可选 SQLite 名字/内容索引
  默认**关闭**且只存本机；回收站为应用私有目录（三端一致）。
- **P3 响应式 = 布局切换**：`ShellViewModel.SetWidth` → Compact/Medium/Expanded；
  同一 `BrowserSession`/标签状态不重建（P4 一套状态多套视图）。
- **预览**（§5.4）：`PreviewContent` 可辨识联合 + 提供者链（Priority 竞争）；
  图片 Skia 解码；PDF/媒体 v1 元数据 + 外部打开（渲染器隔离为 Spike）。
- **主题**：MD3 令牌 Light/Dark 双主题（`ThemeDictionaries`），`RequestedThemeVariant` 切换。

## 已知边界（v1）

- PDF 页面渲染、音视频内嵌播放：降级策略（§6.5），外部打开兜底。
- 7z/RAR：仅元数据；ZIP 全功能（`ZipArchiveService`，无外部工具依赖）。
- Android 宿主（Platform.Android/SAF）：按 Spike 推进，本仓库暂未包含。
- 首次编译验证需在有 SDK 的环境执行；如 Avalonia 12.1.4 细节 API 有出入，属预期内微调。
