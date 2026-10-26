# 插件系统开发计划与可行性评估

状态：**评估完成，路线已收敛**（2026-10-26），待维护者确认决策 D-07 后按期推进。本文回答三个问题：插件在 Native AOT 前提下**能不能做**、**用哪种机制做**、**分几步做**。与《开发文档》§4 分层、§11 AOT 合规、§16 路线图配套阅读；本地 AI 的"模型提供者"最终也复用本插件通道，见 `docs/LOCAL-AI.md`。

## 1. 结论（先行）

| 机制 | AOT 兼容 | Android（含商店政策） | 扩展能力 | 安全/隔离 | 工作量 | 结论 |
|---|---|---|---|---|---|---|
| M1 声明式插件（JSON 清单：主题包、外部命令动作、"打开方式"） | ✅ 完全 | ✅ | 低（无代码） | 高（无可执行代码） | S | **做，P1 首发** |
| M2 进程外工作者（插件为独立可执行，stdio/管道 JSON 协议） | ✅ 完全 | ⚠️ 桌面可；Android 仅限应用内附带的工作者（不能执行用户下载的可执行文件） | 高（可做重预览/索引/AI） | 高（进程边界，可回收） | M | **做，P2 主力机制** |
| M3 脚本插件（Jint JavaScript 解释器，宿主能力白名单） | ✅ 纯托管解释器（细节见 §4.3） | ⚠️ 用户本地脚本可；联网下载脚本触 Play 代码下发政策，商店版禁用 | 中（胶水逻辑/自定义动作） | 中（引擎级沙箱 + 宿主白名单） | M | **做，P3 按需** |
| M4 WASM 组件（Wasmtime/Extism 嵌入运行时） | ⚠️ 可行但重（原生运行时 ≥10 MB/平台、.NET 宿主绑定成熟度一般） | ⚠️ 同 M3 的政策问题 + 体积 | 高 | 高 | L | 观察，不进近规划 |
| M5 程序集插件（AssemblyLoadContext 动态加载 .NET DLL） | ❌ **与 Native AOT 根本冲突** | ❌（同上 + 动态代码政策） | 最高（全 API、UI 组合） | 低（进程内） | S–M | **不做主线**；若未来发布非 AOT 的桌面 JIT 口味，可在该口味下开放 |

**推荐组合**：P0 先把"内置功能"插件化（内部提供者注册表）→ P1 声明式插件公开可用 → P2 进程外工作者承载 PDF 预览、重编解码、AI 模型等重扩展 → P3 视生态需求开脚本插件。一句话：**数据进、数据出的扩展（预览/索引/标记）用进程外，行为小扩展（动作/命令）用声明式+脚本，UI 任意组合不开放**——这是 AOT 与安全约束下能拿到的最优组合。

## 2. 约束盘点（为什么必须这样选）

1. **Native AOT 红线：禁止动态加载程序集**。官方限制清单明确"No dynamic loading（Assembly.LoadFile）/ 无运行时代码生成"（[微软官方 Native AOT 部署文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)）；AOT 发布的程序调用 `Assembly.LoadFile` 会直接抛 `PlatformNotSupportedException`（[实际堆栈](https://stackoverflow.com/questions/79071252/assembly-loadfile-throws-if-calling-app-is-aot-published-in-net8)）。dotnet/runtime 维护者与社区的共识同样是：AOT 下要么 JIT 插件、要么原生非托管边界，别无现成路（[runtime#117470 讨论](https://github.com/dotnet/runtime/discussions/117470)）。因此"把 DLL 丢进 plugins 文件夹"的经典模型在本项目主线构建（AOT 桌面 + Android）上**物理不可行**，这是选型起点而非偏好。
2. **进程边界是唯一可回收的隔离**。进程内插件（哪怕脚本）崩溃即宿主崩溃、原生依赖版本无法隔离；业界经验亦指"需要硬隔离/不可信代码/原生依赖冲突时，进程外是唯一真正的边界"（[Real Plugin Systems in .NET](https://jordansrowles.medium.com/real-plugin-systems-in-net-assemblyloadcontext-unloadability-and-reflection-free-discovery-81f920c83644)）。文件预览恰恰要喂不可信输入（畸形文件、解码器漏洞面），进程隔离与可回收是刚需。
3. **Android 侧**：Play 政策禁止下载可执行代码；Android 亦不可执行任意下载的可执行文件（W^X）。故 Android 上"第三方代码插件"只剩 M1 声明式与"应用自带工作者进程"两种合规形态；M3 脚本商店版需关闭（侧载变体届时可议，与 D-02 联动）。
4. **UI 组合边界**：Avalonia 的 XAML 在 AOT 下须编译期确定（编译绑定为既定规范），插件**不能**下发任意 XAML 界面。插件可扩展的是：预览内容（图像字节/文本/结构化条目）、文件动作（命令）、搜索索引数据源、主题令牌、元数据列——全是"数据与命令"，宿主渲染。这条边界反而让跨断
...[truncated 6648 chars]