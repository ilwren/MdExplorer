# 插件系统开发计划与可行性评估

状态：**评估完成，路线已收敛**（2026-10-26），待维护者确认决策 D-07 后按期推进。本文回答三个问题：插件在 Native AOT 前提下**能不能做**、**用哪种机制做**、**分几步做**。与《开发文档》§4 分层、§11 AOT 合规、§16 路线图配套阅读；本地 AI 的"模型提供者"最终也复用本插件通道，见 `docs/LOCAL-AI.md`。

## 1. 结论（先行）

| 机制 | AOT 兼容 | Android（含商店政策） | 扩展能力 | 安全/隔离 | 工作量 | 结论 |
|---|---|---|---|---|---|---|
| M1 声明式插件（JSON 清单：主题包、外部命令动作、"打开方式"） | ✅ 完全 | ✅ | 低（无代码） | 高（无可执行代码） | S | **做，P1 首发** |
| M2 进程外工作者（插件为独立可执行，stdio/管道 JSON 协议） | ✅ 完全 | ⚠️ 桌面可；Android 仅限应用内附带的工作者（不能执行用户下载的可执行文件） | 高（可做重预览/索引/AI） | 高（进程边界，可回收） | M | **做，P2 主力机制** |
| M3 脚本插件（Lua 首选，宿主能力白名单；MoonSharp 做纯托管 spike，NLua/KeraLua 做原生性能候选，Jint 为 JS 备选） | ⚠️ 需按引擎逐一过 AOT spike（细节见 §4） | ⚠️ 用户本地脚本可；联网下载脚本触 Play 代码下发政策，商店版禁用 | 中（胶水逻辑/自定义动作/规则） | 中（引擎级沙箱 + 宿主白名单 + 操作计划确认） | M | **做，P3 按需；Python 只走进程外** |
| M4 WASM 组件（Wasmtime/Extism 嵌入运行时） | ⚠️ 可行但重（原生运行时 ≥10 MB/平台、.NET 宿主绑定成熟度一般） | ⚠️ 同 M3 的政策问题 + 体积 | 高 | 高 | L | 观察，不进近规划 |
| M5 程序集插件（AssemblyLoadContext 动态加载 .NET DLL） | ❌ **与 Native AOT 根本冲突** | ❌（同上 + 动态代码政策） | 最高（全 API、UI 组合） | 低（进程内） | S–M | **不做主线**；若未来发布非 AOT 的桌面 JIT 口味，可在该口味下开放 |

**推荐组合**：P0 先把"内置功能"插件化（内部提供者注册表）→ P1 声明式插件公开可用 → P2 进程外工作者承载 PDF 预览、重编解码、AI 模型等重扩展 → P3 视生态需求开脚本插件。一句话：**数据进、数据出的扩展（预览/索引/标记）用进程外，行为小扩展（动作/命令）用声明式+脚本，UI 任意组合不开放**——这是 AOT 与安全约束下能拿到的最优组合。

## 2. 约束盘点（为什么必须这样选）

1. **Native AOT 红线：禁止动态加载程序集**。官方限制清单明确"No dynamic loading（Assembly.LoadFile）/ 无运行时代码生成"（[微软官方 Native AOT 部署文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)）；AOT 发布的程序调用 `Assembly.LoadFile` 会直接抛 `PlatformNotSupportedException`（[实际堆栈](https://stackoverflow.com/questions/79071252/assembly-loadfile-throws-if-calling-app-is-aot-published-in-net8)）。dotnet/runtime 维护者与社区的共识同样是：AOT 下要么 JIT 插件、要么原生非托管边界，别无现成路（[runtime#117470 讨论](https://github.com/dotnet/runtime/discussions/117470)）。因此"把 DLL 丢进 plugins 文件夹"的经典模型在本项目主线构建（AOT 桌面 + Android）上**物理不可行**，这是选型起点而非偏好。
2. **进程边界是唯一可回收的隔离**。进程内插件（哪怕脚本）崩溃即宿主崩溃、原生依赖版本无法隔离；业界经验亦指"需要硬隔离/不可信代码/原生依赖冲突时，进程外是唯一真正的边界"（[Real Plugin Systems in .NET](https://jordansrowles.medium.com/real-plugin-systems-in-net-assemblyloadcontext-unloadability-and-reflection-free-discovery-81f920c83644)）。文件预览恰恰要喂不可信输入（畸形文件、解码器漏洞面），进程隔离与可回收是刚需。
3. **Android 侧**：Play 政策禁止下载可执行代码；Android 亦不可执行任意下载的可执行文件（W^X）。故 Android 上"第三方代码插件"只剩 M1 声明式与"应用自带工作者进程"两种合规形态；M3 脚本商店版需关闭（侧载变体届时可议，与 D-02 联动）。
4. **UI 组合边界**：Avalonia 的 XAML 在 AOT 下须编译期确定（编译绑定为既定规范），插件**不能**下发任意 XAML 界面。插件可扩展的是：预览内容（图像字节/文本/结构化条目）、文件动作（命令）、搜索索引数据源、主题令牌、元数据列——全是"数据与命令"，宿主渲染。这条边界反而让跨断
## 3. 宿主边界：脚本只能“提议”，宿主负责执行

脚本插件不直接拿到 `System.IO`、进程、网络、剪贴板或 Avalonia 控件。宿主只注入一个带版本号的窄 API（暂定 `eidoria.*`），且所有写操作先返回**操作计划**，由宿主做路径范围校验、冲突预检、差异预览与用户确认后，才进入既有任务中心执行：

| 能力 | 默认 | 说明 |
|---|---|---|
| `eidoria.selection()` / `eidoria.current_folder()` | 只读 | 返回路径、名称、扩展名、大小、时间、类型等普通数据，不返回文件句柄 |
| `eidoria.log()` / `eidoria.notify()` | 允许 | 输出进任务中心/脚本控制台，限频 |
| `eidoria.plan.rename/move/copy()` | 需确认 | 只生成计划；目标必须位于用户本次选集或当前文件夹范围内，仍走冲突三策略与任务中心取消链 |
| `eidoria.read_text_preview()` | 需清单声明 | 只读有界片段（如前 256 KB），供元数据/标签提取；二进制与大文件拒绝 |
| 任意 IO / 进程 / 网络 / FFI / CLR 反射 | 禁止 | 需要时改走 M2 进程外工作者，并在清单里逐项声明、安装时提示 |

清单示例（字段随 P1 声明式插件同批冻结）：

```json
{
  "id": "community.photo-rename",
  "version": "0.1.0",
  "engine": { "kind": "lua", "api": "eidoria.script/1" },
  "entry": "main.lua",
  "capabilities": ["selection.read", "plan.rename"],
  "commands": [{ "id": "rename-by-date", "title": "按拍摄日期重命名" }]
}
```

执行护栏：单次运行设墙钟与指令/内存上限，脚本异常只终止本次运行；不允许脚本自启线程、不允许递归触发宿主命令；所有计划在确认 UI 中以普通文件操作呈现，用户取消即零落盘。

## 4. 脚本语言选型：Lua 可进主线，Python 不嵌入

### 4.1 对比（许可证与 AOT 均按“引入前必须实测”处理）

| 候选 | 形态与许可 | Native AOT / Android | 适合本项目的功能 | 结论 |
|---|---|---|---|---|
| **MoonSharp**（Lua 5.2） | 纯 C# 解释器、BSD-3-Clause；官方称可运行于 AOT/IL2CPP 平台，标准库可按模块裁剪做沙箱 | 最有希望零原生依赖过桌面 Native AOT 与 Android；但包较老，且 CLR 互操作存在“支持时用运行时代码生成”的路径，必须在 spike 中禁用重互操作、只用显式宿主函数逐项注册 | 批量重命名规则、筛选/排序谓词、文件名与文本片段的标签提取、命令面板小动作 | **P3 首个 spike**：先验证 net10 + Native AOT + Android APK 三处可跑，再决定是否转正 |
| **NLua + KeraLua**（官方 Lua 5.4） | MIT；KeraLua 是原生 Lua 的 C# 绑定，宣称兼容 .NET/Android 等平台 | 性能与语言完整性最好；但每 RID 要带 `liblua54` 原生库，APK 需做原生库清单审计；宿主绑定应停在低层显式函数，避免 NLua 的反射式对象映射把 AOT 裁剪面放大 | 同上，且更适合大批量规则计算 | **生产候选**：若 MoonSharp 性能/兼容不够，用它替换引擎，`eidoria.*` API 不变 |
| **Jint**（JavaScript） | 纯托管 JS 引擎、BSD-2-Clause（引入前复核） | 纯托管路线与 MoonSharp 类似，仍需 AOT spike | 熟悉 JS 的用户写胶水逻辑 | 备选；不与 Lua 双开，避免两套脚本生态与文档成本 |
| **Python.NET**（CPython） | MIT，但它只是桥：目标机还必须有 CPython 运行时与包环境，发布物并不自带 Python | 桌面可做“调用用户已安装 Python”的进程外工作者；嵌入主进程会带来数十 MB 运行时、GIL/原生崩溃面与全量 OS 权限，Android 更要自带并维护 CPython | 调用用户现成的 Python 数据处理脚本、重型元数据/媒体管线 | **不嵌入**；归入 M2：桌面发现系统 `python` 后以子进程 + JSON 协议运行，Android 不提供 |
| **IronPython** | Apache-2.0，构建在 DLR 上 | DLR 依赖运行时代码生成（`System.Reflection.Emit`），与 Native AOT 的“无运行时代码生成”红线冲突 | — | **排除** |

### 4.2 为什么 Lua 更适合文件管理器

Lua 的语言面小、启动快、表结构天然适合表达“选集 → 新名/目标”的纯函数规则，且沙箱只需关掉 `io/os/package/debug` 等标准库再注入白名单函数。典型脚本只做三类事，正好贴合文件管理器：

1. **规则型**：批量重命名模板之外的条件规则（按尺寸/日期/扩展名分支）、自定义分组与排序键；
2. **提取型**：从文件名或有界文本片段提取标签、集数、日期，喂给搜索与元数据列；
3. **动作型**：把若干宿主命令编排成一个命令面板动作，但每一步仍以操作计划呈现。

Python 的价值在生态而非嵌入：让已安装 Python 的桌面用户把现有脚本注册为 M2 工作者（参数是选集 JSON，stdout 回操作计划 JSON），既吃到生态，又不把 CPython 塞进 AOT 主进程与 APK。

### 4.3 落地顺序（仍排在核心完善之后）

- **S-Lua-1（spike，1 天级）**：MoonSharp 在 linux-x64 Native AOT 与 Android 调试 APK 中跑通“选集只读 + 返回重命名计划”的最小闭环；记录包体积、启动耗时与裁剪警告。任一目标不过，换 KeraLua 低层绑定重复同一 spike。
- **S-Lua-2（护栏）**：指令数/墙钟/内存上限、标准库白名单、计划路径范围校验与任务中心确认 UI；补恶意脚本用例（死循环、越界路径、超大输出）。
- **S-Py-1（进程外）**：桌面临时目录协议：`python worker.py --plan` 读 stdin 选集 JSON、写 stdout 计划 JSON；找不到 Python 时功能静默缺席，不影响主程序。
- 三步都完成后才考虑脚本目录扫描、签名与“脚本市场”；在此之前脚本只能由用户手动放进本地脚本文件夹，应用不提供任何联网下载代码的入口。

## 5. 与既有路线的关系

本节不改变 D-07/D-08 的排序：插件实现仍在核心文件管理完善之后，本地 AI 仍只做可行性（`docs/LOCAL-AI.md`）。v0.9 只补充结论——**脚本语言可以引入，但只引入 Lua 这一门嵌入式语言；Python 以进程外工作者形态出现**。任何一门语言的运行时在通过对应 spike 前都不进 `Directory.Packages.props`，避免给全平台构建先背上未经验证的依赖。
