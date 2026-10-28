# MdExplorer 原生版（C# / Avalonia）

基于 `docs/DEVELOPMENT.md`（DESKTOP.md / ANDROID.md 规范）的 .NET 10 + Avalonia 12 实现。
对应概念图 **Material Design 3 · ai-v3 稿**（主题令牌映射 `src/MdExplorer.UI/Themes/Md3.axaml`，
与 Web 版 `tokens.css` 同一色板与几何）。

> ✅ **验证状态：已通过 GitHub Actions 持续集成验证**（工作流 `.github/workflows/native-ci.yml`）。
> Ubuntu + .NET SDK 10.0.100（`global.json`）：`dotnet restore` / `dotnet build -c Release` /
> `dotnet test`（Domain.Tests + Application.Tests + ArchitectureChecks）**全部通过**，
> 无编译错误、无测试失败、无 NuGet 高危告警（SQLitePCLRaw 已钉 2.1.12）。
> CI 覆盖三项：**编译+测试**（Domain / Application / ArchitectureChecks）、**AOT 发布 linux-x64**（含原生工具链与产物检查）、
> **AOT 发布 win-x64**。分层引用规则（§4.2）由 `tests/MdExplorer.ArchitectureChecks` 在 CI 中锁死。

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
- AOT 发布（`dotnet publish -r linux-x64/win-x64`）已在 CI 中通过；如本地新增代码遇到裁剪告警，按 §11 白名单登记。
- CI 未覆盖 Android/macOS 目标；Avalonia 绑定为反射式（非 compiled bindings，便于渐进迁移）。
- CI 状态：`native-ci` 工作流（push 触发）= restore/build/test 三阶段全绿。
