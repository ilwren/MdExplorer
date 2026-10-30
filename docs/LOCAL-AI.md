# 本地 AI 接入计划与可行性评估

状态：**A0 落地（模型分发 + 应用侧状态），推理未接线**（2026-10-30，v0.37）。
范围：全部**本地推理**——无云端 API、无遥测、不发起任何网络请求。取模型是显式的、用户自己动手的一次性动作（`scripts/fetch-ai-models.*`），应用本体离线可用。插件侧通道见 `docs/PLUGIN-SYSTEM.md` §5。

> 这一版不吹能力：设置页只说「装没装、占多大、缺几个文件」，没有装模型时明确写「现在不可用」，不会出现按下去没反应的 AI 按钮。

## 1. 场景与顺序

| 场景 | 可行性 | 主路线 | 模型与许可 | 体积（按需下载） | 分期 |
|---|---|---|---|---|---|
| **文本朗读（TTS）** | ✅ 高 | OS 原生 → 之后可选模型 | 系统 TTS（0 增量） | 0 | A1 |
| **语义检索（文件名/正文）** | ✅ 高 | ONNX Runtime + 本地向量索引 | BGE small zh v1.5（MIT）/ all-MiniLM-L6-v2（Apache-2.0） | ≈23–26 MB（int8） | **A2（下一步）** |
| **多模态语义搜图** | ✅ 高 | 同上 + 图像双塔 | SigLIP 2 Base（Apache-2.0） | ≈150–200 MB（int8） | A2 之后 |
| **识图：标签 + 一句话说明** | ✅ 中高 | ONNX（Florence-2-base，0.23B，MIT） | 英文输出为主，主要喂索引 | ≈250 MB | A3 |
| **语音转写（ASR）** | ✅ 中 | sherpa-onnx / whisper.cpp | Whisper tiny/base（MIT） | ≈75–150 MB | A4 |
| **本地对话（LLM）** | △ 中（体积/速度） | llama.cpp / ONNX（Qwen2.5-0.5B 级） | 权重许可逐个核（优先 Apache-2.0） | ≥0.4–1.5 GB | A5，仅桌面 |

排序依据：**嵌入模型最小、链路最短、对文件管理器最有辨识度**（「找上周那张落日」「找讲部署的笔记」），所以 A2 先做；对话体量大、对 4 GB 内存的 Android 不友好，放最后且默认关。

## 2. 运行时选型

| 运行时 | 许可 | AOT | 三平台 | 结论 |
|---|---|---|---|---|
| **ONNX Runtime**（`Microsoft.ML.OnnxRuntime`） | MIT | 可用但非一等公民：Native AOT 下要自己管修剪根与原生库加载 | win-x64 / linux-x64 / android-arm64 均有预编译 | **A2 采用**：CPython 生态的量化模型都能直接用 |
| **Microsoft.ML.Tokenizers** | MIT | 纯托管，AOT 友好 | 全平台 | 与 ONNX 配套做 BERT/WordPiece 分词 |
| llama.cpp（`LLamaSharp`） | MIT | 需原生库，AOT 可用 | 三平台有 binding | 留给 A5（对话），且仅桌面 |
| sherpa-onnx | Apache-2.0 | 原生库，AOT 可用 | 三平台 | 留给 A4（ASR/TTS 离线模型） |
| 系统 TTS | 随系统 | — | Windows SAPI / Linux speech-dispatcher / Android TextToSpeech | A1，零依赖 |

## 3. 模型分发（v0.37 已落地）

模型几十 MB，**不进 git**，也不在安装包里；取模型是用户显式动作。

### 3.1 三方共用一份清单

`scripts/ai-models.manifest` 是唯一真源，纯文本、`|` 分隔——因为 bash 里没有 JSON 解析器，而脚本、PowerShell 与应用必须看到同一份定义。

```
model|<id>|<名称>|<用途>|<许可>|<许可链接>|<HF 仓库>|<运行时>|<约体积 MB>
file|<id>|<仓库内路径>
```

应用把它作为**嵌入资源**（`MdExplorer.Ai.Models.manifest`）编译进来，所以「脚本下的目录」与「程序找的目录」不会漂移；清单写错会在开发期就抛 `InvalidDataException`，而不是给用户一张残缺的表。

### 3.2 目录布局与校验

```
<模型目录>/<模型 id>/<仓库内路径>      例：…/models/text-embed-zh/onnx/model_quantized.onnx
<模型目录>/checksums.txt             首次下载后记录每个文件的 SHA-256
```

- 默认目录：`MDEXPLORER_MODELS` → 否则 `<应用数据目录>/models`（Linux `~/.local/share/MdExplorer/models`、Windows `%LOCALAPPDATA%\MdExplorer\models`）。应用与脚本用的是同一套优先级。
- 下载：`curl -C -` 断点续传、`-f` 让 4xx/5xx 直接失败（绝不把错误页当模型落盘）、小于 1 KiB 的文件判为失败。
- 校验：首次下载记录 SHA-256；之后每次运行按记录校验，坏文件重新下载；`--verify` 只校验不下载。
- 镜像：`HF_ENDPOINT=https://hf-mirror.com` 或 `--mirror`，给国内网络用。
- 命令：`scripts/fetch-ai-models.sh`（Linux/macOS/WSL）、`scripts/fetch-ai-models.ps1`（Windows）。两边参数一致：`--list / --only / --dir / --verify / --dry-run / --clean / --force`。

### 3.3 许可链

| 模型 | 权重许可 | 结论 |
|---|---|---|
| BAAI BGE small zh v1.5（`Xenova/bge-small-zh-v1.5` 的 ONNX 导出） | MIT | 可随 Apache-2.0 项目分发 |
| sentence-transformers all-MiniLM-L6-v2（`Xenova/all-MiniLM-L6-v2`） | Apache-2.0 | 同上 |
| ONNX Runtime / Microsoft.ML.Tokenizers | MIT | 运行时依赖，AOT 前需再跑一次发布验证 |

> 权重只在用户机器上下载，不进入本仓库；`models/` 已在 `.gitignore` 里。

## 4. 应用侧现状（v0.37 第一切片）

- `AiModelCatalog`（`src/MdExplorer.Infrastructure/Ai/AiModelCatalog.cs`）：只做**文件存在性与体积**扫描，不加载权重、不初始化运行时——设置页打开它没有推理开销。
- 设置页新增「本地 AI」卡：总开关（默认关）、状态行（「尚未下载任何模型，端侧 AI 现在不可用」/「1/2 个模型就绪 · 已占用 22.8 MiB」）、逐模型行（名称、用途、状态、许可）、下载命令（可复制）、模型目录（可直接打开）、重新检查。
- `AppSettings.EnableLocalAi` 落盘；`MDEXPLORER_MODELS` 可覆盖目录。
- **这一版没有推理**：开关打开也只表示「允许以后用」，不会出现任何 AI 按钮。

## 5. A2 的实现计划（下一步）

1. `ITextEmbeddingService` 端口（`EmbedAsync(text) → float[]`，批量 + 取消），Infrastructure 侧 `OnnxTextEmbeddingService`：`Microsoft.ML.OnnxRuntime` + `Microsoft.ML.Tokenizers`（BertTokenizer + `tokenizer.json`），输入 `input_ids`/`attention_mask`/`token_type_ids`，输出取 `last_hidden_state` 做 attention-mask 均值池化 + L2 归一化（这也是 BGE 官方推荐做法）。
2. 索引：复用现有的文件名索引根（v0.13），新增 `ai_vectors` 表/旁挂文件：`<path, mtime, size, chunkIndex, vector>`；文件内容变化（mtime+size）即重建该文件的分块向量，删除即清理。
3. 检索：查询文本走同一模型，余弦相似度；结果复用浏览器网格，附相似度徽标；索引为空时引导到设置页下载模型。
4. 预算与上限：单文件分块 ≤64 块、每块 ≤512 token；一次索引 ≤200k 块；模型缺失/初始化失败时语义搜索入口**隐藏**而不是报错。
5. 验收：模型未安装 → 入口不出现；安装后索引 100 个文件 → 语义查询能命中；删模型 → 降级为文件名搜索且提示原因。

## 6. 隐私与安全边界

- **无网络**：应用运行期不发起任何 HTTP 请求；模型由用户手动下载。没有「首次启动偷偷下模型」。
- **无遥测**：不上报查询、路径或统计。
- **数据不出本机**：索引与向量都在应用数据目录；用户可整目录删除。
- **模型文件可信边界**：ONNX 模型是数据而非代码，但仍按「用户自己下载的外部文件」对待——只从清单里的路径加载，路径不允许逃出模型目录。

## 7. 风险与未验证项

- **ONNX Runtime 的 Native AOT/Android 打包尚未实测**：A2 接线时必须让 CI 的 linux-x64 AOT 与 Android trim/AOT Job 通过，否则退回「桌面开、Android 关」。
- **模型仓库路径可能漂移**：清单里的 HF 路径写的是 `Xenova/*` 镜像仓库；脚本失败时会打印完整 URL 与镜像提示，改清单即可，不需要重编译（应用读的是编译进去的同一份文件，改完重编一次）。
- **没有真机推理证据**：本版完全不跑推理，所以也就没有性能数字。A2 落地时要给出「1000 个文件的索引耗时 / 单次查询延迟 / 内存在 Android 上的实测」。
- **int8 量化的精度损失**未评估：先按官方量化版发布，检索质量不达标再换 fp32（体积 ×4）。

## 8. 参考

- BGE small zh v1.5 模型卡与许可：<https://huggingface.co/BAAI/bge-small-zh-v1.5>
- all-MiniLM-L6-v2 模型卡：<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>
- ONNX Runtime 许可与 Native AOT 说明：<https://github.com/microsoft/onnxruntime>
- Microsoft.ML.Tokenizers：<https://github.com/dotnet/machinelearning>
