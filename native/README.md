# MdExplorer 原生版（C# / Avalonia）

基于 `docs/DEVELOPMENT.md`（DESKTOP.md / ANDROID.md 规范）的 .NET 10 + Avalonia 12 实现。
对应概念图 **Material Design 3 · ai-v3 稿**（主题令牌映射 `src/MdExplorer.UI/Themes/Md3.axaml`，
与 Web 版 `tokens.css` 同一色板与几何）。

> ✅ **验证状态：已通过 GitHub Actions 持续集成验证**（工作流 `.github/workflows/native-ci.yml`）。
> Ubuntu + .NET SDK 10.0.100（`global.json`）：`dotnet restore` / `dotnet build -c Release` /
> `dotnet test`（Domain.Tests + Application.Tests + ArchitectureChecks）**全部通过**，
> 无编译错误、无测试失败、无 NuGet 高危告警（SQLitePCLRaw 已钉 2.1.12）。
> CI 覆盖：**编译+测试**（Domain / Application / ArchitectureChecks）、**AOT 发布 linux-x64 + 产物断言 + 运行时自检**、
> **AOT 发布 win-x64 + 产物断言**。分层引用规则（§4.2）由 `tests/MdExplorer.ArchitectureChecks` 锁死；
> AOT 运行时行为由 `MdExplorer --selftest` 在真实 AOT 可执行文件内验证（见下）。

### AOT 运行时自检（`--selftest`）

`src/MdExplorer.Desktop/SelfTest.cs` 在 Native AOT 产物内跑真实启动路径，任一项失败即返回码 1、CI 红：

| 检查项 | 验证内容 |
|---|---|
| `avalonia-setup` | Skia/平台后端在 AOT 下可用 |
| `composition-root` | 组合根显式 DI 装配（§4.3） |
| `sqlite-index` | SQLitePCLRaw 原生库加载 + 建表（AOT 下 NativeLibrary 解析） |
| `json-settings` | System.Text.Json 源生成序列化往返 |
| `search` | 用例层递归搜索（临时目录命中 ≥1） |
| `xaml-mainwindow` / `xaml-*View` | 9 个 XAML 视图真实加载并布局出尺寸 |
| `binding-reflection` | **反射绑定在裁剪后仍生值**（回归门槛） |

> 两条线程模型约束（改动时务必保留）：Avalonia 必须在主线程先 `SetupWithoutStarting()`，否则 Dispatcher
> 被线程池线程占用后报 "The calling thread cannot access this object"；初始化后主线程不能直接阻塞等待异步结果，
> 需边 `Dispatcher.UIThread.RunJobs()` 边等，否则 Avalonia 同步上下文死锁。

### AOT 与反射绑定

UI 采用 Avalonia 经典反射绑定，**Native AOT 默认会裁掉只被反射访问的属性元数据，导致绑定静默失效**
（界面能加载、值为空）。因此 `src/MdExplorer.Desktop/ILLink.Descriptors.xml` 以裁剪根描述符保留
ViewModels / Views / Application / Domain 的类型成员——该缺陷正是由 `binding-reflection` 自检发现并修复的。
后续若迁移到编译绑定（`x:DataType` + `AvaloniaUseCompiledBindingsByDefault`），该描述符可整体删除。

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
- AOT 发布与运行时自检均已在 CI 通过；发布期有 302 条 `IL2026`/`IL3050` 告警，全部来自 Avalonia 反射绑定
  相关调用（自有源码命中集中在 `BrowserView.axaml` / `TrashView.axaml` 的绑定表达式），属框架侧不可消除项；
  实际行为已由 `binding-reflection` 自检与 15s 存活冒烟兜住。迁移到编译绑定可同时消除这些告警。
- CI 未覆盖 Android/macOS 目标；Avalonia 绑定为反射式（非 compiled bindings，便于渐进迁移）。
- CI 状态：`native-ci` 工作流（push 触发）= restore/build/test 三阶段全绿。
