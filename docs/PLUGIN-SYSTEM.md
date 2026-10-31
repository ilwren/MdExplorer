# Eidoria 插件系统路线与 MEPX v1

状态：**P0 已落地；P1 声明式主题包首个纵切已实现**。维护者已要求继续推进插件系统；本阶段不引入解释器、动态程序集或下载式代码。路线和 AOT/Android 安全边界继续有效。

## 已实现

### P0：编译期内部提供者

- 预览内容由 `IPreviewProvider` 产出，`PreviewProviderRegistry` 按优先级选择；控件和 XAML 仍由宿主负责。
- 提供者在组合根显式注册，没有运行时 DLL 发现或动态加载。

### P1 首片：本地 MEPX v1 Material 3 主题包

扩展中心可导入、扫描、应用与移除本地 `.mepx` 包。当前协议刻意只允许主题颜色，不代表外部动作、打开方式或任意界面已可用。

```json
{
  "manifestVersion": 1,
  "id": "community.calm-theme",
  "name": "Calm Surface",
  "version": "1.2.0",
  "publisher": "MdExplorer Community",
  "description": "A calm Material 3 seed color.",
  "capabilities": ["theme"],
  "theme": { "seedColor": "#6750A4" }
}
```

MEPX v1 验收/安全规则：

- 包是 ZIP，但只允许根目录唯一的 `manifest.json`；不接受任何其他文件，包括脚本、程序集、资源或 XAML。
- 压缩包 ≤ 1 MiB；清单解压后 ≤ 32 KiB；UTF-8 严格解码、JSON 最大嵌套深度为 8；拒绝重复 JSON 字段、未知字段、未知能力、不合法 ID/版本或非 `#RRGGBB` 颜色。
- 插件包保存在应用数据目录的 `plugins/`。导入只通过系统文件选择器从本地文件选取，不提供插件市场或网络下载。
- 安装确认显示名称、发布者、能力和 SHA-256；SHA-256 仅用于标识所选包，不是签名或可信证明。UI 明确提示包未签名、只从可信来源安装。
- 安装后主题只调用宿主现有的 Material 3 种子色功能；移除包不会意外更改已应用的当前主题色。
- 无效/重复 ID 的包不会被静默挑选；扫描结果在扩展中心显示错误。不同包同 ID 通过再次确认后原子替换。

## 约束与禁区

1. **Native AOT**：主线不动态加载 .NET DLL、不使用 `AssemblyLoadContext`、`Assembly.LoadFile` 或运行时代码生成。
2. **插件不提供 UI**：不执行任意 XAML/HTML，也不加载插件控件；扩展只能交声明式数据，由宿主渲染。
3. **本地优先**：当前无联网下载、统计或遥测；商店版 Android 不下载可执行代码。
4. **现阶段没有可执行插件**：主题清单是一组数据，不能启动进程、读任意文件、联网、注册事件或修改其他设置。
5. 新包格式要限长、拒绝路径条目和未知能力；写文件动作必须转换成宿主操作计划，经路径检查与用户确认后交已有任务管线。

## 后续阶段

- **P1 后续**：在 MEPX 格式、包信任与权限确认体验稳定后，再单独设计“打开方式”与外部命令动作。启动可执行文件会有明确能力声明、应用路径由用户选择、参数按数组/占位符处理，不接受 shell 命令拼接；此处尚未开放。
- **P2**：桌面进程外工作者走 JSON-RPC over stdio；进程隔离才可承载不可信解析器或较重的 Python 工具。Android 不运行用户下载的 worker。
- **P3 Lua**：MoonSharp 仍需先通过 net10、linux-x64 Native AOT 和 Android APK 的 AOT/trim spike，再做宿主白名单、指令/时间/内存限制与重命名操作计划确认；通过前不加入依赖、不执行脚本。Python 不嵌入，桌面需求走进程外 worker。
- WASM 仍为观察项；程序集插件仍不进入 AOT 主线。

## 相关代码

- P0：`src/MdExplorer.UI/Preview/PreviewProviders.cs`
- MEPX v1 验证/安装：`src/MdExplorer.Infrastructure/Plugins/PluginPackageCatalog.cs`
- 扩展中心逻辑与 UI：`src/MdExplorer.UI/ViewModels/ExtensionCenterViewModel.cs`、`src/MdExplorer.UI/Views/ExtensionCenterView.axaml`
- 包格式和拒绝代码/未知能力的回归：`tests/MdExplorer.Tests/PluginPackageCatalogTests.cs`
