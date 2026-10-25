# MdExplorer 开发设计文档

> 版本：0.1（方案稿）　|　日期：2026-10-25　|　技术目标：Avalonia 12+ / .NET 10.0

## 1. 项目目标与边界

构建一款 Material Design 风格、适配 Windows/Linux/Android 的本地文件资源管理器。核心能力包括目录浏览、文件和目录 CRUD、常见文件预览、搜索、排序筛选、选择与批量操作。桌面端针对键鼠与宽屏优化，Android 针对触控、窄屏、系统返回和存储授权优化。响应式是**布局与交互模式变化**，不是缩放同一窗口。

**首期范围**：用户可访问的本地文件系统；多选、复制/剪切/粘贴、重命名、移动、删除（含回收站可用性降级）、新建目录、属性、隐藏文件开关、排序、面包屑/路径导航、搜索、预览及错误处理。Android 受 Storage Access Framework (SAF) 授权的目录也视为本地资源。

**暂不纳入**：WebDAV、SMB、云盘、远程同步、归档内直接编辑、桌面级全盘索引默认开启。不同平台的“本地”访问边界、符号链接/权限及删除语义不同，必须通过能力接口呈现，不能假定各平台行为完全一致。

## 2. 技术基线与架构原则

- UI：Avalonia 12+；目标框架按平台拆分，例如 `net10.0`、`net10.0-android`。Avalonia 12、.NET 10、Android workload 与第三方控件必须在首个可运行纵向切片中验证并锁定具体稳定版本。
- 视觉：本桌面 MVP 已接入 `Md3.Avalonia`（NuGet `Md3.Avalonia` 0.5.0-preview.2），引用 Avalonia 12.1.2；使用其 `MaterialTheme`、`MdButton`、`MdTextBox` 和 `MdCard`。主题模式提供系统跟随/浅色/深色切换，由 `MdThemeManager` 按 Material 3 token 更新颜色方案，并将用户选择保存到用户配置目录（文件不可写时仅对当前会话生效）；应用自绘色只作为文件类型标识等语义色，其余面板与文字跟随 M3 色彩角色。该库当前处于 preview，后续升级需先审查 API 变更并由 CI 验证。上游声明 Apache-2.0；发版前仍需保存对应版本许可证和传递依赖清单。主题令牌与业务视图保持松耦合，避免组件库 API 扩散到领域/应用核心。
- 分层依赖方向：平台/UI → 应用用例 → 领域/抽象；基础设施实现抽象。领域与应用核心不引用 Avalonia、Android API、Win32/Linux API。
- 异步 I/O、取消令牌、流式预览；任何耗时枚举/搜索/缩略图不得阻塞 UI 线程。
- 权限、文件名、路径均视为不可信输入；文件操作使用明确确认、错误反馈和可恢复策略。

## 3. 建议解决方案结构

```text
MdExplorer.sln
src/
  MdExplorer.Domain/          # Entry、目录位置、排序、能力、文件操作结果
  MdExplorer.Application/     # 浏览/搜索/操作/预览用例、接口、验证
  MdExplorer.Infrastructure/  # 本地文件系统、缩略图、索引和预览器实现
  MdExplorer.UI/              # Avalonia 视图、ViewModel、主题、响应式状态
  MdExplorer.Platform.Abstractions/ # 平台服务契约（或并入 Application）
  MdExplorer.Platform.Desktop/ # Windows/Linux 桌面服务注册及适配
  MdExplorer.Platform.Android/ # SAF、权限、Android 生命周期适配
 tests/
  MdExplorer.Domain.Tests/
  MdExplorer.Application.Tests/
  MdExplorer.Infrastructure.Tests/
```

部署可采用共享 UI + 平台宿主；若 Avalonia Android 与桌面目标需要不同启动项目/TFM，分别建立 Host 项目并共享 UI/应用程序集。不要为追求形式上的单一项目而将平台条件编译散落到核心。

### 依赖规则

| 项目 | 可依赖 | 不可依赖 |
|---|---|---|
| Domain | BCL | UI、平台 SDK、基础设施 |
| Application | Domain、抽象与 BCL | Avalonia、具体文件系统实现 |
| Infrastructure | Domain、Application 抽象、BCL/经审查依赖 | UI 视图 |
| UI/Host | Application、Avalonia、平台适配 | 绕过用例直接执行文件操作 |

## 4. 核心模型与接口

核心以 `IFileSystemProvider`（或等价抽象）表达资源操作，`FileEntry` 不暴露 `System.IO.FileInfo` 等平台实现对象。建议接口按用例分拆，避免一个巨大服务：

```csharp
public interface IFileSystemProvider
{
    IAsyncEnumerable<FileEntry> EnumerateAsync(Location location,
        EnumerationOptions options, CancellationToken cancellationToken);
    ValueTask<FileEntry?> GetEntryAsync(Location location, CancellationToken cancellationToken);
    Task<OperationResult> CreateDirectoryAsync(Location parent, string name, CancellationToken ct);
    Task<OperationResult> RenameAsync(Location source, string newName, CancellationToken ct);
    Task<OperationResult> MoveAsync(Location source, Location destination, CancellationToken ct);
    Task<OperationResult> CopyAsync(Location source, Location destination, CancellationToken ct);
    Task<OperationResult> DeleteAsync(Location source, DeleteOptions options, CancellationToken ct);
    Task<Stream> OpenReadAsync(Location location, CancellationToken ct);
}
```

契约需覆盖：路径/URI 表示、名称冲突策略、跨卷移动退化为复制+删除、部分失败、取消后状态、权限不足、只读/不可访问条目、符号链接环路、大小与时间戳不支持等。不要只返回 bool；使用有类型的错误码/结果。

平台能力接口示例：

- `IStorageAccessService`：根目录/目录授权、授权持久化、权限说明；Android 由 SAF `DocumentFile`/文档提供器 URI 实现，桌面由常规路径实现。
- `IPlatformFileActions`：系统“打开方式”、分享、外部应用打开、剪贴板/拖放（能力存在时）。
- `ITrashService`：移动到回收站；若不支持，明确告知永久删除并再次确认。
- `IThumbnailProvider`：按 MIME/扩展名/平台能力生成缩略图，受大小和并发限制。

URI/location 必须可表达普通绝对路径以及 Android 授权文档 URI；应用核心不自行拼接平台路径。授权失效时提供重新授权流程，不静默回退到越权访问。

## 5. 功能设计

### 浏览与操作

- 目录视图：列表为默认；桌面支持紧凑列表/网格，Android 提供列表/网格切换。显示名称、类型、大小、修改时间；大目录分批加载/虚拟化。
- 导航：桌面面包屑与可编辑路径；窄屏使用标题栏返回、位置菜单/抽屉；最近位置和收藏夹仅记录用户明确授权可复用的位置。
- 操作：新建文件夹、重命名、复制、剪切、粘贴、移动、删除、属性；支持批量操作、进度、取消与冲突处理。删除前区分回收站与永久删除。
- 选择：键鼠 Ctrl/Shift、多点触控长按、多选操作栏；键盘快捷键按桌面端启用。
- 排序：名称、大小、类型、修改时间，升降序；隐藏文件开关。目录优先规则需明确且可配置。

### 搜索

首期实现当前目录/子树的异步增量搜索（名称匹配、类型/时间/大小筛选），可限制深度、跳过无权目录、取消和报告已跳过项目。大目录不得默认递归扫描整个设备。

后续可加本地索引：用户选择索引位置、后台构建、SQLite FTS5（若所选驱动的 AOT 与平台原生库兼容）或自建索引；提供暂停、排除目录、清除索引、陈旧结果更新策略。Android SAF 的搜索只能覆盖已授权树。

### 预览

使用 `IPreviewProvider` 注册式路由，提供 `CanPreview`、元信息与异步预览内容。首期可支持纯文本/Markdown（只读渲染）、常见图片（缩放/旋转）、PDF（需审查所选渲染方案）、音频/视频由系统打开或在能力验证后提供轻量播放。未知格式显示类型/大小并提供系统打开方式。

安全边界：限制文本读取字节数、图片解码像素与内存、视频/文档并发；预览不执行脚本/宏、不加载远程资源；Markdown HTML 必须净化并禁用远程内容。缩略图缓存有大小上限并可清理。

## 6. 响应式与 Material 3 交互

采用 `AdaptiveLayout` 状态，由可用内容宽度、输入模式和平台共同决定，而不是窗口像素缩放。下列宽度为初始设计断点，开发中以设备实测和可访问性验证调整：

| 状态 | 参考内容宽度 | 布局与交互 |
|---|---:|---|
| Compact | < 600 dp | 单栏文件列表；顶部应用栏；侧边导航改为导航抽屉/底部入口；搜索全屏或独立搜索页；预览作为新页面；触控目标约 48 dp |
| Medium | 600–839 dp | 文件列表为主，导航抽屉可常驻或可展开；预览使用独立页/模态面板；工具栏按空间折叠 |
| Expanded | ≥ 840 dp | 导航栏 + 文件列表 + 可选预览详情侧栏；面包屑、工具栏、键盘快捷键；列宽可调 |

Android 额外处理系统栏/安全区域、横竖屏、分屏和返回栈；桌面考虑高 DPI、窄窗口、键盘导航与最小窗口尺寸。列表在窄宽下隐藏次要列而非压缩到不可读；工具优先级低时进入溢出菜单。主题支持亮/暗/跟随系统与高对比度可读性。

布局示意详见 [`layout-design.svg`](layout-design.svg)，包括桌面三栏（导航/文件列表/预览）、左右并列双栏和上下并列双栏。

### 桌面双栏与预览交互规范

桌面双栏指 **两个独立文件浏览面板**，每个面板各自维护位置、排序、视图模式、选择和滚动位置；两栏之间可拖放复制/移动，并提供明确的目标提示和冲突处理。工具栏应提供“单栏 / 左右分栏 / 上下分栏”切换，面板分隔条可拖拽调整比例、双击恢复均分；活动面板有清晰焦点态，路径导航和操作默认作用于活动面板。初始比例 50:50，允许保存用户偏好。窗口空间不足时自动回退到单栏或仅显示活动面板，不强制挤压内容。建议最小有效面板宽度约 320 dp、高度约 220 dp，最终通过真实窗口测试校准。

文件预览/信息侧栏与双栏是不同模式：三栏工作区的第三栏为所选文件预览及元信息，不是第二个文件浏览器；窄屏预览改为详情/预览页面。预览区域支持关闭、调整宽度；预览文本采用只读、滚动和截断保护。元信息至少包括名称、类型、大小、修改时间、完整位置；可用时再展示创建时间、尺寸、媒体时长、哈希等，无法获取的字段不伪造。提供“打开文件”、复制路径及更多操作入口，信息呈现避免把所有低频属性堆在列表列中。

### 程序 UI 交付稿约定

SVG 是比例示意与信息架构稿，不等于最终像素级交付。实现前应在 Figma 等设计工具按 8dp 间距网格、Material 3 色彩/类型令牌建立组件稿，使用桌面 1440×900、平板约 800×1000、手机约 390×844 等代表画板，并标注组件状态（默认/悬停/焦点/选中/禁用/加载/错误）、间距、响应式切换规则和无障碍对比度。尺寸依据有效内容区而非设备外框；单位按平台 dp/逻辑像素语义校准。

## 7. AOT、裁剪和第三方库治理

目标是 **Native AOT 友好并在可支持的平台启用**，但不可承诺每个平台/每个依赖均支持相同发布模式。Avalonia UI、反射式绑定/序列化、字体/图像解码、SQLite 原生组件等都需以实际发布和运行测试确认；Android 的发布链路与桌面 Native AOT 并不等价。对不支持 AOT 的平台采用受控的裁剪发布或 JIT 发布，不得以关闭警告掩盖风险。

每个依赖入库前核对：

1. NuGet 元数据、上游仓库与维护状态；是否支持 Avalonia 12 / .NET 10 和目标 RID。
2. 许可证全文与传递依赖许可证（优先 MIT、Apache-2.0、BSD 等宽松许可；GPL/AGPL/LGPL 等须法务/产品明确批准）；记录版本、许可证、来源、用途与 NOTICE 要求。
3. trimming/AOT 声明、动态代码/反射、P/Invoke、平台原生资产；用发布产物实测，而非仅看 README 宣称。
4. 安全更新、体积、离线运行、Android ABI 覆盖与维护活跃度。

| 需求 | 优先方案/候选 | 采用前必须确认 |
|---|---|---|
| Material 3 控件 | `Md3.Avalonia` 0.5.0-preview.2（已集成） | 上游声明 Apache-2.0、针对 Avalonia 12；CI 验证桌面构建。发布前仍需针对目标 RID 做 trimming/AOT 和 Android 真机验证；preview API 升级须审查 |
| Markdown | Markdig（候选） | MIT 许可核验；渲染扩展关闭远程内容，AOT 发布测试 |
| 图片 | Avalonia 支持的解码能力/平台解码服务 | 大图像限制、格式支持、原生依赖及 AOT |
| PDF | 平台打开或可替换 PDF 预览适配器 | 引擎许可证（常见引擎可能为 AGPL/商业双许可）、Android 支持、体积/AOT；未核验前不纳入核心 |
| SQLite 索引 | SQLite 驱动候选 | NuGet 与 SQLite 许可/原生资产、RID、AOT 兼容及数据库迁移；首期可不依赖 |

依赖清单随代码维护，建议 `THIRD-PARTY-NOTICES.md` 和自动化许可证清单；版本锁定并定期升级。任何许可证结论须以引入时的具体版本和分发方式复核，本表不是法律意见。

## 8. 质量、测试与发布

- 单元测试：路径/授权边界、冲突策略、操作状态机、筛选排序、错误映射、Markdown 净化。
- 文件系统集成测试：临时目录、权限拒绝、长路径、特殊字符、符号链接、并发修改、取消、磁盘空间错误；避免破坏真实用户文件。
- UI 测试：Compact/Medium/Expanded、窄窗口、亮暗主题、键盘焦点、触控命中、无障碍语义；快照仅作辅助。
- 平台验收：Windows、Linux 多发行版代表环境、Android 真机/模拟器；SAF 授权、授权撤销、后台恢复、不同 ABI。
- 发布门禁：`dotnet test`、各目标平台 build/publish、AOT/trimming 警告审查、冷启动/内存/大目录性能检查、许可证与 SBOM 检查。
- 文件操作提供审慎确认、状态可见和失败恢复；不会对用户数据做未授权的静默索引或批量修改。

## 9. 迭代计划与验收标准

1. **技术验证**：Avalonia 12 + .NET 10 三平台最小启动；验证 MD3 候选、平台 Host、AOT/裁剪和 Android SAF 授权。
2. **核心浏览**：抽象与桌面/Android provider、导航、虚拟化列表、响应式状态、错误反馈。
3. **文件操作**：新建/重命名/复制/移动/删除、批量进度、冲突与取消测试。
4. **搜索和预览**：增量搜索、文本/Markdown/图片预览、安全限制、系统打开方式。
5. **完善与发布**：收藏/最近、缩略图缓存、无障碍、性能、签名/安装包、许可证审计。

首版验收：三平台均可浏览获授权的本地位置；核心操作不会阻塞 UI，失败能解释；窄屏与宽屏采用不同信息布局；搜索可取消且不默认全盘扫描；预览对异常大/损坏文件有保护；依赖许可和 AOT 状态有记录；发布流水线可重复构建。

## 10. 尚需在实施前决策

- Android 最低 API、支持的 Linux 发行版与桌面打包格式。
- 桌面端是否实现拖放/剪贴板及系统回收站；各平台能力差异策略。
- PDF 是系统外部打开还是首发内置预览。
- 搜索仅即时遍历还是提供可选索引；索引默认不启用。
- 最终主题库及图标/字体来源与许可。

## 11. 第一版可交互 UI 原型

仓库 `prototype/` 提供无需构建工具的 HTML/CSS/JavaScript 浏览器原型，用于先行评审信息层级和自适应交互：桌面三栏预览、布局切换入口、搜索过滤、列表选择后文件预览/信息、移动端窄屏导航和预览抽屉。双栏目前以布局占位区展示，供评审分栏方向与视觉层级。

**重要：该 HTML 文件仍只是视觉/交互参考原型。** 它使用演示数据，不访问真实文件。当前实际桌面 MVP 位于 `src/MdExplorer.Desktop`，以 C# / Avalonia 实现真实本地目录浏览和基础操作；架构分层及 Android Host/SAF 等能力仍按上文迭代计划继续完善。

HTML 参考稿运行：在仓库根目录执行 `python3 -m http.server 4173`，浏览器打开 `http://localhost:4173/prototype/`。小屏可通过浏览器设备模拟器查看。Avalonia 桌面程序运行方式见仓库 README。

## 12. 桌面 MVP 当前已落地的操作边界

- 已可通过 Avalonia / Md3.Avalonia 在桌面系统访问真实用户目录；支持搜索当前目录、打开子目录、名称/修改时间/大小/类型排序、隐藏文件开关、文本/图片预览和元信息。
- 已实现新建文件夹、重命名、永久删除，以及单项复制/剪切/粘贴；支持 Ctrl/Cmd+C/X/V、F2、Delete 快捷键。粘贴遇到同名项目会自动生成副本名，不覆盖现有数据；移动失败时尝试复制后删除源项，失败则保留源并报告。首版拒绝递归复制符号链接/重解析点以降低越界风险。
- 多选、批量操作、取消/进度、系统剪贴板互通、回收站、跨 SAF 权限树操作以及 Android 应用仍未完成；UI 上的双栏第二面板目前尚未实现完整拖放/同步操作语义。
